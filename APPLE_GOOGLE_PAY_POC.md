# Apple Pay & Google Pay repayments — POC

Proof of concept for the proposal in
[Apple Pay & Google Pay implementation using Stripe](https://moneyme1.atlassian.net/wiki/spaces/UD/pages/3058040988/Apple+Pay+Google+Pay+implementation+using+Stripe)
(Confluence space UD, page 3058040988).

The goal is to prove the **integration mechanics** end to end — device wallet availability,
the wallet sheet, the PaymentIntent lifecycle, surcharge handling, wallet-aware receipts and
decline copy — so the real build starts from verified ground rather than from the doc alone.

---

## Important scope note: web wallets, not native SDKs

The proposal targets the **native mobile apps** (Stripe iOS `StripeApplePay`, Stripe Android
Google Pay module). This repo is an ASP.NET Core web POC with no iOS/Android project, so the
POC uses Stripe's **web** wallet integration (Express Checkout Element) instead.

That distinction only affects the client. Everything the backend does is identical:

| Concern | Web POC | Native app |
|---|---|---|
| PaymentIntent creation, amount, surcharge, metadata | ✅ same | same |
| Wallet token → PaymentMethod → confirm | ✅ same | same |
| `status` / `last_payment_error` handling | ✅ same | same |
| Receipt + history labelling from `wallet.type` | ✅ same | same |
| Availability check | Express Checkout Element `availablePaymentMethods` | `canMakePayment` / `PaymentSheet` |
| Sheet presentation | Browser wallet sheet | `PKPaymentAuthorizationController` / Google Pay sheet |

So the **server contract in this POC is directly reusable** by the mobile apps. The client
half is a stand-in that proves the same sequence.

---

## What is built

### Screens

Repayments live **inside** the Existing Customer Area rather than as a separate destination.
There is one customer area: sign in, and the wallet flow is a section of that dashboard,
alongside the existing Make a Payment and Payment History actions.

| File | Covers |
|---|---|
| `POC/wwwroot/ECA/Login.html` | Sign in, or create a throwaway Stripe test customer |
| `POC/wwwroot/ECA/Welcome.html` | The Customer Area. Holds the repayments section: account status card, Payment amount → Payment method → Review & finalise → wallet sheet → Success, repayment history, receipt screen, and the error / full-balance / bank-transfer modals |
| `POC/wwwroot/ECA/scripts/repayments.js` | The whole client flow, entered via `startRepaymentDemo(customerId)` |
| `POC/wwwroot/ECA/styles/repayments.css` | Styling for the repayments section |
| `POC/wwwroot/ECA/styles/login.css` | MoneyMe-branded login page (dark green ground, white card, lime CTAs) |

The whole ECA uses the MoneyMe palette from the proposal's app designs — dark green
`#0B2828`, lime `#BBEE00` CTAs, white cards.

### Proposal flows mapped

| # | Flow in the proposal | Status in POC |
|---|---|---|
| 1 | Apple Pay | ✅ Built — availability gate, sheet, confirm, success, decline |
| 2 | Google Pay | ✅ Built — same path |
| 3 | Bank direct debit | ✅ UI only — no PaymentIntent, shows the "Payment scheduled" confirmation |
| 4 | Debit card, no saved card | ✅ Built via Stripe Payment Element (shares the same PaymentIntent path as the wallets) |
| 5 | Debit card, saved card | ✅ Built — tick "save this card", then reuse it next time without retyping the number. The CVC re-entry modal is not built |
| 6 | Bank transfer | ✅ Built — BSB / account / reference modal with copy-to-clipboard |
| 7 | PayTo | ❌ Out of scope, as per the proposal |
| 8 | Full balance payment | ✅ Built — "Pay full balance" preset plus the confirmation modal |
| 9 | Error | ✅ Built — server-mapped decline copy, wallet sheet dismisses into the in-app modal |
| 10 | Apple Pay / Google Pay not set up | ✅ Built — wallets are hidden entirely, never greyed out |

### Backend

| File | Purpose |
|---|---|
| `POC/Models/RepaymentModels.cs` | Request/response contracts for the whole flow |
| `POC/Services/RepaymentService.cs` | Surcharge calculation, PaymentIntent creation, receipt/history mapping |
| `POC/Services/DeclineMessageResolver.cs` | `decline_code` → customer-facing copy (raw Stripe text is never shown) |
| `POC/Controllers/RepaymentController.cs` | The API surface below |

### API

| Endpoint | Purpose |
|---|---|
| `GET /api/repayment/config` | Publishable key, merchant name/country, surcharge rate, Apple Pay domain-file status |
| `GET /api/repayment/account/{customerId}` | Account status card + bank transfer details |
| `POST /api/repayment/quote` | Surcharge breakdown for Review & finalise |
| `POST /api/repayment/create-intent` | Creates the PaymentIntent the wallet sheet confirms against |
| `GET /api/repayment/receipt/{paymentIntentId}` | Receipt/success data, including the wallet Stripe reported |
| `GET /api/repayment/history/{customerId}` | Repayment history list |
| `GET /api/repayment/failure/{paymentIntentId}` | Customer-facing decline copy for the error modal |

---

## Key design decisions

**Wallets are card payments.** `create-intent` builds a plain
`PaymentMethodTypes = ["card"]` intent. Apple Pay and Google Pay do not need a different
intent type — the wallet only changes how the card reaches Stripe. This is why the proposal's
"extension of an existing integration" framing holds up.

**The server owns the surcharge.** The 0.72% is calculated in `RepaymentService.BuildQuote`
and baked into `PaymentIntent.Amount`, with the base amount and surcharge stored in metadata.
The client never computes it, so the sheet total can't disagree with the intent.

**The method label comes from Stripe, not from the picker.** `RepaymentService` reads
`charge.payment_method_details.card.wallet.type` and maps `apple_pay` / `google_pay` to the
history and receipt labels, falling back to the customer's selection. History therefore
reflects how the payment actually settled.

**Receipt "Payment from" shows the underlying card** (`Debit card ending in 4242`) for all
three card-based methods, matching the receipt table in the proposal.

**The receipt Reference is the Stripe PaymentIntent ID** (`pi_...`), so the value the customer
sees is the same one support and finance paste straight into the Stripe Dashboard.

**Saving a card needs the Elements group and the PaymentIntent to agree.** With deferred
Elements, `setup_future_usage` must be declared on both — ticking "save this card" calls
`elements.update({ setupFutureUsage: 'off_session' })` before the server creates the intent
with the same value. They will not confirm if they disagree.

**Decline copy is resolved server-side** so the `decline_code` → wording mapping lives in one
place and can be reused by the iOS and Android apps.

---

## Running the POC

```bash
cd POC
dotnet run
```

Stripe test keys go in `POC/appsettings.Development.json` under `Stripe:PublishableKey` /
`Stripe:SecretKey`. Repayment settings (merchant name, surcharge rate, stub balance, bank
details) live under `Repayments` in `appsettings.json`.

Then open `/home.html` → **Existing Customer Area — Apple Pay / Google Pay**, or go straight
to `/ECA/Login.html`.

Press **Create a new test customer** (it fills the form for you), then **Sign in**. You land
on the Customer Area dashboard with the repayments section ready to go.

In Development, static files are served with `Cache-Control: no-cache` so edits to CSS/JS
show up on a normal reload — no hard-refresh needed while iterating.

### Seeing the wallets

Apple Pay and Google Pay only appear when the browser reports them as available:

- **Google Pay** — Chrome, signed into a Google account with a saved card. Works on `localhost`.
- **Apple Pay** — Safari on a signed-in Apple device with a card in Wallet, served over HTTPS,
  **and** the domain registered with Stripe (see below). It will not appear on `localhost` in Chrome.

If neither is available the method list falls back to Debit card + Bank direct debit only —
which is flow 10 in the proposal, working as designed.

### Apple Pay domain registration

1. Stripe Dashboard → Settings → Payments → Payment methods → Apple Pay → **Add a new domain**.
2. Download `apple-developer-merchantid-domain-association` and drop it into
   `POC/wwwroot/.well-known/` (no extension, no edits).
3. Redeploy over HTTPS and confirm
   `https://<domain>/.well-known/apple-developer-merchantid-domain-association` returns the file.
   `Program.cs` already serves this path with `ServeUnknownFileTypes`.
4. Click **Verify** in the Dashboard.

`GET /api/repayment/config` returns `isApplePayDomainFileConfigured` so you can check step 2
without shelling into the server.

Google Pay needs no equivalent file for a Stripe-hosted web integration.

---

## Verification performed

Against live Stripe **test mode**:

| Check | Result |
|---|---|
| Surcharge on a $245.50 wallet repayment | $1.77 surcharge, $247.27 total (0.72%) |
| Surcharge on Bank direct debit | $0.00, "Up to 2 business days to clear" |
| PaymentIntent creation + confirm | `succeeded`, receipt shows "Apple Pay", "Debit card ending in 4242", reference `MM260818FAF336` |
| `insufficient_funds` decline | "Not enough funds", retry disabled, primary action "Change amount" |
| `expired_card` decline | "Card has expired", directs to update the wallet card |
| `generic_decline` | Falls back through `decline_code` → `code` to "Card declined by your bank" |
| Decline with no `last_payment_error` | Generic copy, no crash |
| History list | Apple Pay / Google Pay entries with Cleared / Failed pills |
| Both pages in headless Chrome | No JS errors; amount presets, balance and history all render |

Wallet sheets themselves cannot be automated — they need a real device with a real wallet card,
so those need a manual pass on Safari (Apple Pay) and Chrome (Google Pay).

---

## Open questions for the real build

1. **Native SDK work is not proven here.** The iOS `StripeApplePay` / Android Google Pay
   integration and the `PaymentSheet`-vs-custom decision still need a spike in the mobile repos.
   The server contract in this POC is what they should build against.
2. **Recording the repayment.** This POC reads history straight from Stripe. Production needs
   the repayment written to the loan servicing system on `payment_intent.succeeded` — the
   existing `WebhookService` is the natural place, and it will need the wallet type persisted
   so history stays correct without a Stripe round-trip.
3. **Surcharge as a Stripe object.** The POC folds the surcharge into the intent amount. If
   finance needs it broken out for reporting, consider a separate line item or a dedicated
   metadata-driven reconciliation.
4. **Saved-card CVC step (flow 5)** is not built.
5. **The full-balance confirmation modal** fires when leaving the Payment method step rather
   than from Review, because the wallet button *is* the finalise action — there is no separate
   "Finalise payment" tap to intercept for Apple Pay / Google Pay. Worth a UX call.
6. **Merchant registration** — Apple Pay domain/certificate and the Google Pay business console
   setup are still outstanding for the real domains.
