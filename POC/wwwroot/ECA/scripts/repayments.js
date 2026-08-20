// Apple Pay / Google Pay repayments demo, hosted inside the Existing Customer Area page.
//
// Apple Pay and Google Pay are card-based wallets, so the server-side lifecycle is the same
// PaymentIntent used by Debit card today. The only wallet-specific pieces on the client are:
//   1. the availability check that decides whether the options are shown at all, and
//   2. the Express Checkout Element, which renders the branded wallet button and opens
//      the native sheet.
//
// Entry point: startRepaymentDemo(customerId), called by login.js once a customer is set.

const STEP_IDS = ['stepAmount', 'stepMethod', 'stepReview', 'stepProcessing', 'stepSuccess', 'stepScheduled'];

const METHOD_DEFINITIONS = [
    {
        key: 'ApplePay',
        name: 'Apple Pay',
        glyph: '\u{1F34E}',
        sub: 'Processed instantly. 0.72% card surcharge applies.',
        isWallet: true
    },
    {
        key: 'GooglePay',
        name: 'Google Pay',
        glyph: '\u{1F535}',
        sub: 'Processed instantly. 0.72% card surcharge applies.',
        isWallet: true
    },
    {
        key: 'DebitCard',
        name: 'Debit card',
        glyph: '\u{1F4B3}',
        sub: 'Processed instantly. 0.72% card surcharge applies.',
        isWallet: false
    },
    {
        key: 'BankDirectDebit',
        name: 'Bank direct debit',
        glyph: '\u{1F3E6}',
        sub: 'Up to 2 business days to clear. No surcharge.',
        isWallet: false
    }
];

const METHOD_GLYPHS = {
    ApplePay: '\u{1F34E}',
    GooglePay: '\u{1F535}',
    DebitCard: '\u{1F4B3}',
    BankDirectDebit: '\u{1F3E6}',
    BankTransfer: '\u{1F3E6}'
};

let stripe = null;
let stripeConfig = null;
let account = null;
let repaymentCustomerId = null;
let availableWallets = {};
let historyLimit = 5;
let isDemoStarted = false;

let walletElements = null;
let walletButtonElement = null;
let cardElements = null;
let cardPaymentElement = null;
let savedCards = [];
let selectedSavedCardId = null;
let walletAvailabilityPromise = null;
let isGooglePayForced = false;

const state = {
    amountInCents: 0,
    method: null,
    isFullBalancePayment: false,
    quote: null,
    currentStep: 'stepAmount'
};

/**
 * Boots the repayments section of the Customer Area for a customer.
 * Safe to call more than once - the second call just swaps the customer.
 */
async function startRepaymentDemo(customerId) {
    repaymentCustomerId = customerId;

    if (!isDemoStarted) {
        bindRepaymentControls();
        isDemoStarted = true;
    }

    resetFlow();

    try {
        if (!stripe) {
            stripeConfig = await getJson('/api/repayment/config');
            stripe = Stripe(stripeConfig.publishableKey);

            if (stripeConfig.environment === 'test') {
                const banner = document.getElementById('envBanner');
                banner.textContent = 'Stripe test mode — no real money moves';
                banner.hidden = false;
            }
        }
    } catch (error) {
        console.error('Could not initialise Stripe', error);
        showErrorModal({
            title: 'Payments are unavailable',
            description: 'We could not start the payment system. Check the Stripe keys in appsettings and reload.'
        }, () => window.location.reload(), () => { document.getElementById('errorModal').hidden = true; });
        return;
    }

    // Start the wallet probe first so it runs alongside the account and history calls.
    // It used to be the last await, which meant a customer who clicked through quickly
    // reached the method list before availability was known and saw no wallets at all.
    walletAvailabilityPromise = detectWalletAvailability().then(available => {
        availableWallets = available;

        // If they are already on the method step, rebuild it now that we know.
        if (state.currentStep === 'stepMethod') {
            const previousMethod = state.method;
            renderMethodStep();

            if (previousMethod) {
                selectMethod(previousMethod);
            }
        }

        return available;
    });

    account = await getJson(`/api/repayment/account/${repaymentCustomerId}`);
    renderAccountSummary();
    await loadSavedCards();
    await loadHistory();
}

function bindRepaymentControls() {
    bindModalDismissals();

    document.getElementById('amountInput').addEventListener('input', event => {
        const amount = parseFloat(event.target.value);
        state.amountInCents = Number.isNaN(amount) ? 0 : Math.round(amount * 100);
        state.isFullBalancePayment = state.amountInCents === account?.outstandingBalanceInCents;
        syncQuickAmountSelection();
        document.getElementById('amountContinueButton').disabled = state.amountInCents <= 0;
    });

    document.getElementById('amountContinueButton').addEventListener('click', async () => {
        const button = document.getElementById('amountContinueButton');
        const originalLabel = button.textContent;

        // Availability decides whether the wallets are listed at all, so the list must not
        // be built before the probe has answered.
        button.disabled = true;
        button.textContent = 'Checking wallets\u2026';

        try {
            await walletAvailabilityPromise;
        } catch (error) {
            console.error('Wallet availability check failed', error);
        }

        button.disabled = false;
        button.textContent = originalLabel;

        renderMethodStep();
        showStep('stepMethod');
    });

    document.getElementById('methodContinueButton').addEventListener('click', onMethodContinue);
    document.getElementById('methodBackButton').addEventListener('click', () => showStep('stepAmount'));
    document.getElementById('reviewBackButton').addEventListener('click', () => showStep('stepMethod'));

    document.getElementById('confirmFullBalanceButton').addEventListener('click', () => {
        document.getElementById('fullBalanceModal').hidden = true;
        proceedToReview();
    });

    document.getElementById('bankTransferLink').addEventListener('click', showBankTransferModal);
    document.getElementById('copyBankDetailsButton').addEventListener('click', copyBankDetails);

    document.getElementById('useNewCardButton').addEventListener('click', () => showCardEntry(false));
    document.getElementById('useSavedCardButton').addEventListener('click', () => showCardEntry(true));

    document.getElementById('saveCardCheckbox').addEventListener('change', event => {
        // With deferred Elements, setup_future_usage has to be declared on the Elements
        // group as well; Stripe rejects the confirmation if it disagrees with the
        // PaymentIntent created server-side.
        if (cardElements) {
            cardElements.update({ setupFutureUsage: event.target.checked ? 'off_session' : null });
        }
    });

    document.getElementById('successDoneButton').addEventListener('click', restartFlow);
    document.getElementById('scheduledDoneButton').addEventListener('click', restartFlow);

    document.getElementById('viewMoreButton').addEventListener('click', () => {
        historyLimit = historyLimit === 5 ? 20 : 5;
        document.getElementById('viewMoreButton').textContent = historyLimit === 5 ? 'View more' : 'View less';
        loadHistory();
    });
}

function resetFlow() {
    state.amountInCents = 0;
    state.method = null;
    state.isFullBalancePayment = false;
    state.quote = null;
    selectedSavedCardId = null;
    cardPaymentElement = null;
    document.getElementById('amountInput').value = '';
    document.getElementById('amountContinueButton').disabled = true;
    syncQuickAmountSelection();
    showStep('stepAmount');
}

async function restartFlow() {
    resetFlow();
    await loadHistory();
}

/* --------------------------------------------------------- saved cards */

/**
 * Cards the customer has already saved. Reusing one means they never re-enter the card
 * number - this is flow 5 (Debit card, saved card) in the proposal.
 */
async function loadSavedCards() {
    try {
        savedCards = await getJson(`/api/payment/saved-payment-methods/${repaymentCustomerId}`);
    } catch (error) {
        console.error('Could not load saved cards', error);
        savedCards = [];
    }
}

function renderSavedCards() {
    const list = document.getElementById('savedCardList');
    list.innerHTML = '';

    savedCards.forEach(card => {
        const option = document.createElement('button');
        option.type = 'button';
        option.className = 'saved-card';
        option.dataset.paymentMethodId = card.id;
        option.innerHTML = `
            <span class="method-glyph">\u{1F4B3}</span>
            <span class="detail">
                <span class="name">${escapeHtml((card.brand || 'card').toUpperCase())} &bull;&bull;&bull;&bull; ${escapeHtml(card.last4 || '')}</span>
                <span class="sub">Expires ${escapeHtml(String(card.expMonth).padStart(2, '0'))}/${escapeHtml(String(card.expYear).slice(-2))}</span>
            </span>
            <span class="radio"></span>
        `;
        option.addEventListener('click', () => selectSavedCard(card.id));
        list.appendChild(option);
    });

    selectSavedCard(savedCards[0].id);
}

function selectSavedCard(paymentMethodId) {
    selectedSavedCardId = paymentMethodId;

    document.querySelectorAll('.saved-card').forEach(option => {
        option.classList.toggle('selected', option.dataset.paymentMethodId === paymentMethodId);
    });
}

/**
 * Switches between reusing a saved card and typing a new one.
 */
function showCardEntry(isSavedCardUsed) {
    document.getElementById('savedCardsBlock').hidden = !isSavedCardUsed;
    document.getElementById('newCardBlock').hidden = isSavedCardUsed;
    document.getElementById('useSavedCardButton').hidden = savedCards.length === 0;
    document.getElementById('cardError').textContent = '';

    if (isSavedCardUsed) {
        return;
    }

    selectedSavedCardId = null;

    if (!cardPaymentElement) {
        mountCardElement();
    }
}

/* ---------------------------------------------------------------- step 1 */

function renderAccountSummary() {
    document.getElementById('outstandingBalance').textContent =
        formatMoney(account.outstandingBalanceInCents, account.currency);

    document.getElementById('nextDue').innerHTML =
        `Next instalment <strong>${formatMoney(account.nextInstalmentAmountInCents, account.currency)}</strong> due ${formatDate(new Date(account.nextInstalmentDueDate))}`;

    const container = document.getElementById('quickAmounts');
    container.innerHTML = '';

    account.quickAmountsInCents.forEach(amountInCents => {
        container.appendChild(buildAmountChip(formatMoney(amountInCents, account.currency), amountInCents));
    });

    const fullBalanceChip = buildAmountChip(
        `Pay full balance · ${formatMoney(account.outstandingBalanceInCents, account.currency)}`,
        account.outstandingBalanceInCents);
    fullBalanceChip.classList.add('full-balance');
    container.appendChild(fullBalanceChip);
}

function buildAmountChip(label, amountInCents) {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'chip';
    chip.textContent = label;
    chip.dataset.amount = String(amountInCents);

    chip.addEventListener('click', () => {
        state.amountInCents = amountInCents;
        state.isFullBalancePayment = amountInCents === account.outstandingBalanceInCents;
        document.getElementById('amountInput').value = (amountInCents / 100).toFixed(2);
        document.getElementById('amountContinueButton').disabled = false;
        syncQuickAmountSelection();
    });

    return chip;
}

function syncQuickAmountSelection() {
    document.querySelectorAll('#quickAmounts .chip').forEach(chip => {
        chip.classList.toggle('selected', Number(chip.dataset.amount) === state.amountInCents);
    });
}

/* ---------------------------------------------------------------- step 2 */

function renderMethodStep() {
    const list = document.getElementById('methodList');
    list.innerHTML = '';
    state.method = null;
    document.getElementById('methodContinueButton').disabled = true;

    const visibleMethods = METHOD_DEFINITIONS.filter(method => !method.isWallet || isWalletAvailable(method.key));

    visibleMethods.forEach(method => {
        const option = document.createElement('button');
        option.type = 'button';
        option.className = 'method-option';
        option.dataset.method = method.key;
        option.innerHTML = `
            <span class="method-glyph">${method.glyph}</span>
            <span class="detail">
                <span class="name">${escapeHtml(method.name)}</span>
                <span class="sub">${escapeHtml(method.sub)}</span>
            </span>
            <span class="radio"></span>
        `;

        option.addEventListener('click', () => selectMethod(method.key));
        list.appendChild(option);
    });

    renderWalletAvailabilityNote();
}

function isWalletAvailable(methodKey) {
    if (methodKey === 'ApplePay') {
        return availableWallets.applePay === true;
    }

    if (methodKey === 'GooglePay') {
        return availableWallets.googlePay === true;
    }

    return false;
}

function renderWalletAvailabilityNote() {
    const note = document.getElementById('walletAvailabilityNote');
    const available = [];

    if (availableWallets.applePay) {
        available.push('Apple Pay');
    }

    if (availableWallets.googlePay) {
        available.push('Google Pay');
    }

    if (available.length) {
        note.innerHTML = `This device reports <strong>${available.join(' and ')}</strong> as available, so ${available.length > 1 ? 'they are' : 'it is'} shown above.`;
        return;
    }

    // Nothing available: show what Stripe actually reported plus the usual causes, so the
    // note is a checklist rather than a dead end.
    const reported = Object.keys(availableWallets).length
        ? JSON.stringify(availableWallets)
        : 'nothing (no wallet detected)';

    note.innerHTML = '<strong>Apple Pay and Google Pay are hidden</strong> because this browser reports no '
        + 'usable wallet, so they are omitted entirely rather than shown greyed out.'
        + `<br><br>Stripe reported: <code>${escapeHtml(reported)}</code>`
        + '<br><br>Common causes:'
        + '<br>&bull; Not signed in to Chrome, or no card saved at pay.google.com (Google Pay)'
        + '<br>&bull; Chrome &rsaquo; Settings &rsaquo; Autofill and passwords &rsaquo; Payment methods &rsaquo; '
        + '"Allow sites to check if you have payment methods saved" is off'
        + '<br>&bull; Incognito or private window &mdash; wallets never render there'
        + '<br>&bull; Page not served over HTTPS with a trusted certificate'
        + '<br>&bull; Apple Pay needs Safari on an Apple device with a card in Wallet; it never appears on Windows'
        + '<br><br>See the browser console for the raw availability object.';
}

function selectMethod(methodKey) {
    state.method = methodKey;

    document.querySelectorAll('.method-option').forEach(option => {
        option.classList.toggle('selected', option.dataset.method === methodKey);
    });

    document.getElementById('methodContinueButton').disabled = false;
}

function onMethodContinue() {
    if (!state.method) {
        return;
    }

    if (state.isFullBalancePayment) {
        document.getElementById('fullBalanceCopy').textContent =
            `You're about to pay off your full balance of ${formatMoney(account.outstandingBalanceInCents, account.currency)}. This closes out the loan.`;
        document.getElementById('fullBalanceModal').hidden = false;
        return;
    }

    proceedToReview();
}

/* ---------------------------------------------------------------- step 3 */

async function proceedToReview() {
    try {
        state.quote = await postJson('/api/repayment/quote', {
            amountInCents: state.amountInCents,
            method: state.method,
            currency: account.currency
        });
    } catch (error) {
        console.error('Could not price the repayment', error);
        showErrorModal({
            title: 'We could not price this repayment',
            description: error.message
        }, proceedToReview, () => showStep('stepMethod'));
        return;
    }

    renderReview();
    showStep('stepReview');

    if (state.method === 'BankDirectDebit') {
        return;
    }

    if (state.method === 'DebitCard') {
        if (savedCards.length) {
            renderSavedCards();
            showCardEntry(true);
        } else {
            showCardEntry(false);
        }

        return;
    }

    mountWalletButton();
}

function renderReview() {
    const quote = state.quote;
    const methodDefinition = METHOD_DEFINITIONS.find(method => method.key === state.method);

    document.getElementById('reviewTo').textContent = account.productName;
    document.getElementById('reviewAmount').textContent = formatMoney(quote.amountInCents, quote.currency);
    document.getElementById('reviewMethod').textContent = methodDefinition.name;
    document.getElementById('reviewTotal').textContent = formatMoney(quote.totalInCents, quote.currency);
    document.getElementById('reviewNote').textContent = quote.settlementNote;

    const surchargeRow = document.getElementById('reviewSurchargeRow');
    surchargeRow.hidden = !quote.isSurchargeApplied;

    if (quote.isSurchargeApplied) {
        document.getElementById('reviewSurchargeKey').textContent =
            `Card surcharge (${(quote.surchargeRate * 100).toFixed(2)}%)`;
        document.getElementById('reviewSurcharge').textContent =
            formatMoney(quote.surchargeInCents, quote.currency);
    }

    document.getElementById('cardEntryBlock').hidden = state.method !== 'DebitCard';
    document.getElementById('wallet-button-container').hidden =
        state.method === 'DebitCard' || state.method === 'BankDirectDebit';

    const finaliseButton = document.getElementById('finaliseButton');
    finaliseButton.hidden = state.method !== 'DebitCard' && state.method !== 'BankDirectDebit';
    finaliseButton.onclick = state.method === 'BankDirectDebit' ? scheduleDirectDebit : payWithCard;
}

/* ------------------------------------------------- wallet: availability */

/**
 * Asks Stripe which wallets this device can actually present.
 *
 * This uses stripe.paymentRequest().canMakePayment() - the literal canMakePayment check
 * the proposal refers to - rather than mounting a hidden Express Checkout Element.
 *
 * The hidden-element approach raced Chrome: the element reports ready as soon as it has
 * rendered, but Chrome resolves the Google Pay payment handler (fetching its web app
 * manifest and icons) asynchronously and often finishes later. Tearing the probe down on
 * ready destroyed the context mid-resolution, producing "Cannot download icons after the
 * webpage has been closed" and an availability answer of undefined even on devices that
 * do have Google Pay.
 *
 * canMakePayment resolves only once the browser has finished deciding, and needs no DOM
 * node at all, so there is nothing to tear down and nothing to race.
 */
async function detectWalletAvailability() {
    try {
        const paymentRequest = stripe.paymentRequest({
            country: (stripeConfig.merchantCountryCode || 'AU').toUpperCase(),
            currency: (stripeConfig.currency || 'aud').toLowerCase(),
            total: {
                label: `${stripeConfig.merchantName || 'MoneyMe'} repayment`,
                // Placeholder: availability does not depend on the amount, and the real
                // total is set on the Elements group before the sheet opens.
                amount: 1000
            },
            requestPayerName: false,
            requestPayerEmail: false
        });

        const available = await paymentRequest.canMakePayment();

        // Logged verbatim: this object is the ground truth for why a wallet does or does
        // not render, and guessing at it wastes a lot of time.
        console.info('Wallet availability reported by Stripe:', available);

        const googlePayProbe = await probeGooglePayHandler();
        probeApplePayCapability(available || {});
        const wallets = available || {};

        // canMakePayment only says yes once a card is already enrolled on this profile, but
        // the Express Checkout Element renders Google Pay whenever the browser has the
        // handler, and lets Google collect a card inside the sheet. Gating on the stricter
        // answer hid the button from every customer who had not saved one yet, so the
        // handler answer is what decides. Apple Pay keeps the strict check: it genuinely
        // cannot proceed without a card in Wallet and a verified domain.
        if (!wallets.googlePay && googlePayProbe.isHandlerAvailable === true) {
            wallets.googlePay = true;

            // Stripe's element defaults to 'auto', which hides Google Pay for exactly the
            // case we just overrode - handler present, no card enrolled. Offering the
            // option in the list but rendering no button would strand the customer on
            // Review with nothing to press, so the element is told to show it regardless.
            isGooglePayForced = true;
        }

        return wallets;
    } catch (error) {
        console.error('Wallet availability check failed', error);
        return {};
    }
}

/**
 * Asks Chrome directly why Google Pay is or is not on offer.
 *
 * canMakePayment() above answers "can a wallet be presented", but when the answer is no it
 * does not say which half failed. Two Payment Request API calls separate them:
 *
 *   canMakePayment()        is the Google Pay handler available to this browser profile at
 *                           all - signed in, not incognito, site payment checks allowed
 *   hasEnrolledInstrument() does that profile actually have a card saved
 *
 * An available handler with no enrolled card is the case that matters: the Express Checkout
 * Element still renders Google Pay and lets Google collect a card in the sheet, so an
 * available handler is treated as good enough to offer the wallet.
 *
 * Every failure is swallowed because browsers that do not know the google.com/pay method
 * reject the probe outright, which is an answer of "no", not an error worth surfacing.
 */
async function probeGooglePayHandler() {
    if (typeof PaymentRequest === 'undefined') {
        console.info('Google Pay diagnostics: this browser has no Payment Request API.');
        return {};
    }

    try {
        const probe = new PaymentRequest(
            [{ supportedMethods: 'https://google.com/pay' }],
            {
                total: {
                    label: 'Google Pay availability probe',
                    // Never charged - the probe only asks what the browser could present.
                    amount: {
                        currency: (stripeConfig.currency || 'aud').toUpperCase(),
                        value: '10.00'
                    }
                }
            }
        );

        const isHandlerAvailable = await probe.canMakePayment();

        // hasEnrolledInstrument is not implemented everywhere, so it stays optional.
        const hasEnrolledCard = typeof probe.hasEnrolledInstrument === 'function'
            ? await probe.hasEnrolledInstrument()
            : 'not supported by this browser';

        console.info('Google Pay diagnostics:', { isHandlerAvailable, hasEnrolledCard });

        if (isHandlerAvailable === false && isSafari()) {
            console.info(
                'Google Pay diagnostics: Safari has no Google Pay handler - this is expected and '
                + 'not a fault. Use Apple Pay on this browser.'
            );
        } else if (isHandlerAvailable === false) {
            console.info(
                'Chrome will not offer the Google Pay handler at all. Check that the profile is '
                + 'signed in to Chrome, the window is not incognito, and that '
                + 'chrome://settings/payments has "Allow sites to check if you have payment '
                + 'methods saved" turned on.'
            );
        } else if (hasEnrolledCard === false) {
            console.info(
                'Google Pay is available to this browser but no card is saved to the profile. '
                + 'It is still offered: Google collects a card inside the sheet.'
            );
        }

        return { isHandlerAvailable, hasEnrolledCard };
    } catch (error) {
        console.info('Google Pay diagnostics unavailable - browser rejected the probe:', error.message);
        return {};
    }
}

function isSafari() {
    const ua = navigator.userAgent;
    return /Safari/.test(ua) && !/Chrome|Chromium|Edg|OPR/.test(ua);
}

/**
 * Separates the two causes Stripe's Apple Pay warning lumps together: an unregistered
 * domain versus no card in Wallet. ApplePaySession.canMakePayments() reports whether the
 * device and browser can do Apple Pay at all, independently of any card being provisioned,
 * so comparing it against Stripe's answer isolates which half is missing.
 *
 * canMakePaymentsWithActiveCard would answer the card question directly, but it needs the
 * Apple merchant identifier, and Stripe owns that on our behalf.
 */
function probeApplePayCapability(walletsReportedByStripe) {
    try {
        if (typeof window.ApplePaySession === 'undefined') {
            console.info(
                'Apple Pay diagnostics: this browser has no ApplePaySession, so Apple Pay is not '
                + 'possible here. Apple Pay needs Safari on an Apple device - it never appears in '
                + 'Chrome, Edge or Firefox on any platform.'
            );
            return;
        }

        const canMakePayments = window.ApplePaySession.canMakePayments();
        console.info('Apple Pay diagnostics:', {
            hasApplePaySession: true,
            deviceCanMakePayments: canMakePayments,
            stripeReportedApplePay: walletsReportedByStripe.applePay === true
        });

        if (!canMakePayments) {
            console.info(
                'Apple Pay diagnostics: this device or OS cannot present Apple Pay. Check it is a '
                + 'supported Apple device and that Safari is not in a Private window.'
            );
            return;
        }

        if (walletsReportedByStripe.applePay !== true) {
            // Device is capable and the domain is registered, so the missing piece is the card.
            console.info(
                'Apple Pay diagnostics: this device CAN present Apple Pay, and the domain is '
                + 'registered with Stripe, so the missing piece is a card in Wallet. Add a real '
                + 'card in Wallet (System Settings > Wallet & Apple Pay on a Mac, or the Wallet '
                + 'app on iPhone), and make sure Safari > Settings > Advanced > "Allow websites to '
                + 'check for Apple Pay and Apple Card" is on. Stripe test cards cannot be added '
                + 'to Wallet - a real card is required, and it will not be charged on test keys.'
            );
        }
    } catch (error) {
        console.info('Apple Pay diagnostics unavailable:', error.message);
    }
}

function buildElementsOptions(amountInCents) {
    return {
        mode: 'payment',
        amount: amountInCents,
        currency: (stripeConfig.currency || 'aud').toLowerCase(),
        // Wallets settle as card payments, which keeps the intent identical to Debit card.
        paymentMethodTypes: ['card'],
        appearance: {
            variables: {
                colorPrimary: '#667eea',
                borderRadius: '8px'
            }
        }
    };
}

function buildExpressCheckoutOptions() {
    return {
        // 'plain' renders the bare wallet mark, which is what the Figma CTA shows.
        // Apple Pay and Google Pay accept different buttonType vocabularies.
        buttonType: { applePay: 'plain', googlePay: 'plain' },
        buttonTheme: { applePay: 'black', googlePay: 'black' },
        buttonHeight: 50,
        layout: { maxColumns: 1, maxRows: 2 },
        paymentMethods: {
            applePay: 'auto',
            googlePay: isGooglePayForced ? 'always' : 'auto',
            link: 'never',
            paypal: 'never',
            amazonPay: 'never'
        }
    };
}

/* ------------------------------------------------------- wallet: payment */

function mountWalletButton() {
    const container = document.getElementById('wallet-button-container');
    container.innerHTML = '';

    walletElements = stripe.elements(buildElementsOptions(state.quote.totalInCents));
    walletButtonElement = walletElements.create('expressCheckout', buildExpressCheckoutOptions());

    walletButtonElement.on('click', event => {
        const lineItems = [{ name: `${account.productName} repayment`, amount: state.quote.amountInCents }];

        if (state.quote.surchargeInCents > 0) {
            lineItems.push({ name: 'Card surcharge', amount: state.quote.surchargeInCents });
        }

        event.resolve({
            business: { name: stripeConfig.merchantName },
            lineItems
        });
    });

    walletButtonElement.on('ready', event => {
        console.info('Wallet button rendered with:', event.availablePaymentMethods);

        const hasButton = Boolean(container.querySelector('iframe'));

        if (!hasButton) {
            showWalletUnavailable();
        }
    });

    walletButtonElement.on('loaderror', event => {
        console.error('Wallet button failed to load', event?.error || event);
        showWalletUnavailable();
    });

    walletButtonElement.on('confirm', onWalletConfirm);
    walletButtonElement.on('cancel', () => {
        // Customer dismissed the native sheet - stay on Review so they can retry.
        showStep('stepReview');
    });

    walletButtonElement.mount(container);
}

/**
 * Shown when the wallet button cannot render after the customer has already chosen the
 * wallet. Without this the Review step shows an empty box and a dead end.
 */
function showWalletUnavailable() {
    const container = document.getElementById('wallet-button-container');

    container.innerHTML = '<div class="wallet-availability-note">'
        + '<strong>This wallet could not be loaded on this device.</strong><br>'
        + 'Go back and choose Debit card or Bank direct debit instead.'
        + '</div>';
}

async function onWalletConfirm(event) {
    let paymentIntentId = null;

    try {
        const { error: submitError } = await walletElements.submit();

        if (submitError) {
            throw new Error(submitError.message);
        }

        const intent = await createRepaymentIntent();
        paymentIntentId = intent.paymentIntentId;

        showStep('stepProcessing');

        const { error, paymentIntent } = await stripe.confirmPayment({
            elements: walletElements,
            clientSecret: intent.clientSecret,
            confirmParams: { return_url: `${window.location.origin}/ECA/Login.html` },
            redirect: 'if_required'
        });

        if (error) {
            console.error('Wallet confirmation failed', error);
            notifyWalletSheetOfFailure(event);
            await handlePaymentFailure(paymentIntentId, error);
            return;
        }

        await showSuccess(paymentIntent.id);
    } catch (error) {
        console.error('Wallet payment failed', error);
        notifyWalletSheetOfFailure(event);
        await handlePaymentFailure(paymentIntentId, error);
    }
}

function notifyWalletSheetOfFailure(event) {
    // Dismisses the native sheet with a failure state so the customer is returned to the
    // page rather than being left inside the wallet UI.
    try {
        event.paymentFailed({ reason: 'fail' });
    } catch (error) {
        console.debug('Wallet sheet already dismissed', error);
    }
}

/* --------------------------------------------------------- debit card */

function mountCardElement() {
    const host = document.getElementById('card-payment-element');
    host.innerHTML = '';
    document.getElementById('saveCardCheckbox').checked = false;

    cardElements = stripe.elements(buildElementsOptions(state.quote.totalInCents));
    cardPaymentElement = cardElements.create('payment', {
        layout: 'tabs',
        // Apple Pay and Google Pay are their own entries in the method picker and are
        // confirmed through the Express Checkout Element, so suppress them here rather
        // than offering a second, inconsistent route to the same wallets.
        wallets: { applePay: 'never', googlePay: 'never' }
    });
    cardPaymentElement.mount(host);
}

async function payWithCard() {
    const errorText = document.getElementById('cardError');
    errorText.textContent = '';

    let paymentIntentId = null;
    const isSavedCardUsed = Boolean(selectedSavedCardId);

    try {
        if (!isSavedCardUsed) {
            const { error: submitError } = await cardElements.submit();

            if (submitError) {
                errorText.textContent = submitError.message;
                return;
            }
        }

        const intent = await createRepaymentIntent();
        paymentIntentId = intent.paymentIntentId;

        showStep('stepProcessing');

        // A saved card already lives on the customer, so it is confirmed by id - there is
        // no Elements group and nothing for the customer to retype.
        const confirmation = isSavedCardUsed
            ? await stripe.confirmCardPayment(intent.clientSecret, { payment_method: selectedSavedCardId })
            : await stripe.confirmPayment({
                elements: cardElements,
                clientSecret: intent.clientSecret,
                confirmParams: { return_url: `${window.location.origin}/ECA/Welcome.html` },
                redirect: 'if_required'
            });

        if (confirmation.error) {
            console.error('Card confirmation failed', confirmation.error);
            await handlePaymentFailure(paymentIntentId, confirmation.error);
            return;
        }

        await showSuccess(confirmation.paymentIntent.id);
    } catch (error) {
        console.error('Card payment failed', error);
        await handlePaymentFailure(paymentIntentId, error);
    }
}

/* ------------------------------------------------- bank direct debit */

function scheduleDirectDebit() {
    // Direct debit does not create a PaymentIntent at this point in the flow, so the
    // customer sees a "scheduled" confirmation rather than "You paid $X".
    document.getElementById('scheduledMeta').innerHTML =
        `${formatMoney(state.quote.totalInCents, state.quote.currency)} will be debited from your bank account on file.<br>`
        + 'It can take 2 to 3 business days to clear. You will see it in your repayment history once it does.';
    showStep('stepScheduled');
}

/* ------------------------------------------------------------ outcomes */

async function createRepaymentIntent() {
    return postJson('/api/repayment/create-intent', {
        customerId: repaymentCustomerId,
        amountInCents: state.amountInCents,
        method: state.method,
        currency: account.currency,
        paymentMethodId: selectedSavedCardId,
        isCardSaved: !selectedSavedCardId && document.getElementById('saveCardCheckbox').checked,
        isFullBalancePayment: state.isFullBalancePayment
    });
}

async function showSuccess(paymentIntentId) {
    let receipt = null;

    try {
        receipt = await getJson(`/api/repayment/receipt/${paymentIntentId}`);
    } catch (error) {
        console.error('Could not load receipt', error);
    }

    const paidInCents = receipt ? receipt.amountInCents : state.quote.totalInCents;
    const currency = receipt ? receipt.currency : state.quote.currency;

    document.getElementById('successAmount').textContent = `You paid ${formatMoney(paidInCents, currency)}`;
    document.getElementById('successMeta').innerHTML =
        `${formatDateTime(new Date())}<br>A receipt is on its way to your email.`;

    const rows = [['Payment to', receipt ? receipt.paymentTo : account.productName]];

    if (receipt) {
        rows.push(['Payment from', receipt.paymentFrom]);
        rows.push(['Method', receipt.methodLabel]);
        rows.push(['Status', receipt.statusLabel]);
        rows.push(['Reference', receipt.reference]);

        if (receipt.walletType) {
            rows.push(['Wallet reported by Stripe', receipt.walletType]);
        }
    }

    renderDetailList(document.getElementById('successDetails'), rows);
    showStep('stepSuccess');
    await loadSavedCards();
    await loadHistory();
}

/**
 * Turns a decline into the in-page error modal. The server is asked for the copy so the
 * mapping from decline_code to customer-facing wording lives in one place.
 */
async function handlePaymentFailure(paymentIntentId, clientError) {
    let failure = null;

    if (paymentIntentId) {
        try {
            failure = await getJson(`/api/repayment/failure/${paymentIntentId}`);
        } catch (error) {
            console.error('Could not resolve failure copy', error);
        }
    }

    if (!failure) {
        failure = {
            title: 'Payment could not be processed',
            description: clientError?.message || 'Something went wrong. No money has left your account.',
            primaryAction: 'Try again',
            secondaryAction: 'Choose another method',
            retryStep: 'same'
        };
    }

    showStep('stepReview');
    showErrorModal(
        failure,
        () => {
            document.getElementById('errorModal').hidden = true;
            // The server decides where "try again" lands so the button label and the
            // screen the customer ends up on always agree.
            goToRetryStep(failure.retryStep);
        },
        () => {
            document.getElementById('errorModal').hidden = true;
            showStep('stepMethod');
        });
}

function goToRetryStep(retryStep) {
    if (retryStep === 'amount') {
        showStep('stepAmount');
        return;
    }

    if (retryStep === 'method') {
        renderMethodStep();
        showStep('stepMethod');
        return;
    }

    showStep('stepReview');
}

function showErrorModal(failure, onPrimary, onSecondary) {
    document.getElementById('errorTitle').textContent = failure.title;
    document.getElementById('errorDescription').textContent = failure.description;

    const primaryButton = document.getElementById('errorPrimaryButton');
    const secondaryButton = document.getElementById('errorSecondaryButton');

    primaryButton.textContent = failure.primaryAction || 'Try again';
    secondaryButton.textContent = failure.secondaryAction || 'Choose another method';
    primaryButton.onclick = onPrimary;
    secondaryButton.onclick = onSecondary;

    const codeText = [failure.errorCode, failure.declineCode].filter(Boolean).join(' / ');
    document.getElementById('errorDeclineCode').textContent = codeText ? `Stripe code: ${codeText}` : '';

    document.getElementById('errorModal').hidden = false;
}

/* ------------------------------------------------- history and receipt */

async function loadHistory() {
    const list = document.getElementById('historyList');

    try {
        const history = await getJson(`/api/repayment/history/${repaymentCustomerId}?limit=${historyLimit}`);

        if (!history.length) {
            list.innerHTML = '<div class="empty-state">No repayments yet.<br>Your first repayment will appear here.</div>';
            return;
        }

        list.innerHTML = '';
        history.forEach(item => list.appendChild(buildHistoryRow(item)));
    } catch (error) {
        console.error('Could not load repayment history', error);
        list.innerHTML = '<div class="empty-state">Repayment history is unavailable right now.</div>';
    }
}

function buildHistoryRow(item) {
    const row = document.createElement('button');
    row.type = 'button';
    row.className = 'history-item';

    row.innerHTML = `
        <span class="method-glyph">${METHOD_GLYPHS[item.methodKey] || METHOD_GLYPHS.DebitCard}</span>
        <span class="detail">
            <span class="method">${escapeHtml(item.methodLabel)}</span>
            <span class="date">${formatDate(new Date(item.createdUtc))}</span>
        </span>
        <span class="value">
            <span class="amount">${formatMoney(item.amountInCents, item.currency)}</span><br>
            <span class="status-pill ${statusClass(item.status)}">${escapeHtml(item.statusLabel)}</span>
        </span>
    `;

    row.addEventListener('click', () => showReceipt(item));
    return row;
}

function showReceipt(item) {
    const rows = [
        ['Payment to', item.paymentTo],
        ['Payment from', item.paymentFrom],
        ['Method', item.methodLabel],
        ['Amount', formatMoney(item.amountInCents, item.currency)]
    ];

    if (item.surchargeInCents > 0) {
        rows.push(['Includes card surcharge', formatMoney(item.surchargeInCents, item.currency)]);
    }

    rows.push(['Status', item.statusLabel]);
    rows.push(['Date', formatDateTime(new Date(item.createdUtc))]);
    rows.push(['Reference', item.reference]);

    if (item.walletType) {
        rows.push(['Wallet reported by Stripe', item.walletType]);
    }

    renderDetailList(document.getElementById('receiptDetails'), rows);

    const receiptLink = document.getElementById('stripeReceiptLink');

    if (item.receiptUrl) {
        receiptLink.href = item.receiptUrl;
        receiptLink.hidden = false;
    } else {
        receiptLink.hidden = true;
    }

    document.getElementById('receiptModal').hidden = false;
}

function statusClass(status) {
    if (status === 'succeeded') {
        return 'cleared';
    }

    if (status === 'requires_payment_method' || status === 'canceled') {
        return 'failed';
    }

    return 'pending';
}

/* ------------------------------------------------------ bank transfer */

function showBankTransferModal() {
    const transfer = account.bankTransfer;

    renderDetailList(document.getElementById('bankTransferDetails'), [
        ['Account name', transfer.accountName],
        ['BSB', transfer.bsb],
        ['Account number', transfer.accountNumber],
        ['Reference', transfer.reference]
    ]);

    document.getElementById('bankTransferModal').hidden = false;
}

async function copyBankDetails() {
    const transfer = account.bankTransfer;
    const text = `Account name: ${transfer.accountName}\nBSB: ${transfer.bsb}\nAccount number: ${transfer.accountNumber}\nReference: ${transfer.reference}`;

    try {
        await navigator.clipboard.writeText(text);
        const toast = document.getElementById('copyToast');
        toast.hidden = false;
        setTimeout(() => { toast.hidden = true; }, 2500);
    } catch (error) {
        console.error('Clipboard write failed', error);
    }
}

/* ----------------------------------------------------------- plumbing */

function showStep(stepId) {
    STEP_IDS.forEach(id => {
        document.getElementById(id).hidden = id !== stepId;
    });

    state.currentStep = stepId;
}

function bindModalDismissals() {
    document.querySelectorAll('[data-close]').forEach(button => {
        button.addEventListener('click', () => {
            document.getElementById(button.dataset.close).hidden = true;
        });
    });

    document.querySelectorAll('.modal-backdrop').forEach(backdrop => {
        backdrop.addEventListener('click', event => {
            if (event.target === backdrop) {
                backdrop.hidden = true;
            }
        });
    });
}

async function getJson(url) {
    const response = await fetch(url);

    if (!response.ok) {
        throw new Error(`${response.status}: ${await response.text()}`);
    }

    return response.json();
}

async function postJson(url, body) {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });

    if (!response.ok) {
        let message = `Request failed (${response.status})`;

        try {
            const payload = await response.json();
            message = payload.error || message;
        } catch {
            // Response was not JSON - keep the status-based message.
        }

        throw new Error(message);
    }

    return response.json();
}

function formatMoney(amountInCents, currency) {
    return new Intl.NumberFormat('en-AU', {
        style: 'currency',
        currency: (currency || 'AUD').toUpperCase()
    }).format((amountInCents || 0) / 100);
}

function formatDate(date) {
    return date.toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' });
}

function formatDateTime(date) {
    return date.toLocaleString('en-AU', {
        day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit'
    });
}

function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value;
    return div.innerHTML;
}

function renderDetailList(container, rows) {
    container.innerHTML = rows
        .map(([key, value]) =>
            `<li><span class="key">${escapeHtml(key)}</span><span class="val">${escapeHtml(String(value))}</span></li>`)
        .join('');
}
