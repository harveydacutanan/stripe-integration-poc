// Global variables
let stripe = null;
let currentCustomer = null;
let selectedPaymentMethodId = null;

// Initialize application
document.addEventListener('DOMContentLoaded', async function() {
    await initializeStripe();
    setupCardElements();
});

// Initialize Stripe dynamically
async function initializeStripe() {
    try {
        const response = await fetch('/api/configuration/stripe');
        const config = await response.json();

        if (!response.ok) {
            throw new Error(config.error || 'Failed to load Stripe configuration');
        }

        // Initialize Stripe with the publishable key from backend
        stripe = Stripe(config.publishableKey);

        // Show environment indicator
        const envIndicator = config.environment === 'live' ? '🔴 LIVE' : '🧪 TEST';
        document.title = `Stripe Payment POC (${envIndicator})`;

        console.log(`Stripe initialized in ${config.environment} mode`);
    } catch (error) {
        console.error('Failed to initialize Stripe:', error);
        alert('Failed to initialize payment system. Please refresh the page.');
    }
}

// Setup card elements after Stripe is initialized
function setupCardElements() {
    if (!stripe) {
        console.error('Stripe not initialized');
        return;
    }

    // Create elements for different forms
    const elements1 = stripe.elements();
    const elements2 = stripe.elements();
    const elements3 = stripe.elements();
    const elements4 = stripe.elements();

    // Configure card elements without postal code to match existing eWAY setup
    const cardStyle = {
        hidePostalCode: true
    };

    window.guestCardElement = elements1.create('card', cardStyle);
    window.customerCardElement = elements2.create('card', cardStyle);
    window.returningCardElement = elements3.create('card', cardStyle);
    window.saveCardElement = elements4.create('card', cardStyle);

    window.guestCardElement.mount('#guest-card-element');
    window.customerCardElement.mount('#customer-card-element');
    window.returningCardElement.mount('#returning-card-element');
    window.saveCardElement.mount('#save-card-element');

    // Setup card element event handlers
    window.guestCardElement.on('change', handleCardChange.bind(null, 'guest-card-error'));
    window.customerCardElement.on('change', handleCardChange.bind(null, 'customer-card-error'));
    window.returningCardElement.on('change', handleCardChange.bind(null, 'returning-card-error'));
    window.saveCardElement.on('change', handleCardChange.bind(null, 'save-card-error'));
}

// Tab functionality
function showTab(tabName) {
    // Hide all tabs
    document.querySelectorAll('.tab-content').forEach(tab => {
        tab.classList.remove('active');
    });
    document.querySelectorAll('.tab').forEach(tab => {
        tab.classList.remove('active');
    });

    // Show selected tab
    document.getElementById(tabName).classList.add('active');
    event.target.classList.add('active');
}

// Error handling for card elements (already set up in setupCardElements)

function handleCardChange(errorElementId, event) {
    const displayError = document.getElementById(errorElementId);
    if (event.error) {
        displayError.textContent = event.error.message;
    } else {
        displayError.textContent = '';
    }
}

// Guest Payment Form
document.getElementById('guest-payment-form').addEventListener('submit', async function(event) {
    event.preventDefault();

    const submitButton = document.getElementById('guest-submit-button');
    const buttonText = document.getElementById('guest-button-text');
    const spinner = document.getElementById('guest-spinner');

    toggleButton(submitButton, buttonText, spinner, true);

    const amount = document.getElementById('guest-amount').value;

    try {
        // First, create payment method to check funding type
        const {paymentMethod, error: pmError} = await stripe.createPaymentMethod({
            type: 'card',
            card: window.guestCardElement
        });

        if (pmError) {
            displayError('guest-payment-result', pmError.message);
            toggleButton(submitButton, buttonText, spinner, false);
            return;
        }

        // Block credit cards
        if (paymentMethod.card.funding === 'credit') {
            displayError('guest-payment-result', '❌ Credit cards are not accepted. Please use a debit card.');
            document.getElementById('guest-card-error').textContent = '❌ Credit cards are not accepted. Please use a debit card.';
            document.getElementById('guest-card-error').style.color = '#dc3545';
            document.getElementById('guest-card-error').style.fontWeight = 'bold';
            toggleButton(submitButton, buttonText, spinner, false);
            return;
        }

        const response = await fetch('/api/payment/create-intent', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                amount: parseFloat(amount),
                currency: 'aud',
                savePaymentMethod: false
            })
        });

        const { clientSecret } = await response.json();

        const result = await stripe.confirmCardPayment(clientSecret, {
            payment_method: paymentMethod.id
        });

        displayResult('guest-payment-result', result);
    } catch (error) {
        displayError('guest-payment-result', error.message);
    }

    toggleButton(submitButton, buttonText, spinner, false);
});

// Customer Registration + Payment Form
document.getElementById('customer-payment-form').addEventListener('submit', async function(event) {
    event.preventDefault();

    const submitButton = document.getElementById('customer-submit-button');
    const buttonText = document.getElementById('customer-button-text');
    const spinner = document.getElementById('customer-spinner');

    toggleButton(submitButton, buttonText, spinner, true);

    const customerData = {
        name: document.getElementById('customer-name').value,
        email: document.getElementById('customer-email').value,
        phone: document.getElementById('customer-phone').value
    };

    const amount = document.getElementById('customer-amount').value;
    const savePaymentMethod = document.getElementById('save-payment-method').checked;

    try {
        // First, create payment method to check funding type
        const {paymentMethod, error: pmError} = await stripe.createPaymentMethod({
            type: 'card',
            card: window.customerCardElement
        });

        if (pmError) {
            displayError('customer-payment-result', pmError.message);
            toggleButton(submitButton, buttonText, spinner, false);
            return;
        }

        // Block credit cards
        if (paymentMethod.card.funding === 'credit') {
            displayError('customer-payment-result', '❌ Credit cards are not accepted. Please use a debit card.');
            document.getElementById('customer-card-error').textContent = '❌ Credit cards are not accepted. Please use a debit card.';
            document.getElementById('customer-card-error').style.color = '#dc3545';
            document.getElementById('customer-card-error').style.fontWeight = 'bold';
            toggleButton(submitButton, buttonText, spinner, false);
            return;
        }

        const response = await fetch('/api/payment/create-intent', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                amount: parseFloat(amount),
                currency: 'usd',
                savePaymentMethod: savePaymentMethod,
                customer: customerData
            })
        });

        const { clientSecret, customerId } = await response.json();

        const result = await stripe.confirmCardPayment(clientSecret, {
            payment_method: paymentMethod.id
        });

        displayResult('customer-payment-result', result, `Customer created with ID: ${customerId}`);
    } catch (error) {
        displayError('customer-payment-result', error.message);
    }

    toggleButton(submitButton, buttonText, spinner, false);
});

// Customer Lookup
async function lookupCustomer() {
    const customerId = document.getElementById('lookup-customer-id').value;
    if (!customerId) {
        alert('Please enter a Stripe Customer ID');
        return;
    }

    try {
        const response = await fetch(`/api/customer/${encodeURIComponent(customerId)}`);

        if (response.ok) {
            const customer = await response.json();
            displayCustomerInfo(customer);
        } else {
            alert('Customer not found. Please check the Customer ID or create a new account.');
        }
    } catch (error) {
        alert('Error finding customer: ' + error.message);
    }
}

function displayCustomerInfo(customer) {
    currentCustomer = customer;

    document.getElementById('found-customer-name').textContent = customer.name;
    document.getElementById('found-customer-email').textContent = customer.email;
    document.getElementById('found-customer-id').textContent = customer.customerId;

    displaySavedPaymentMethods(customer.savedPaymentMethods);

    document.getElementById('customer-found').classList.remove('hidden');
}

function displaySavedPaymentMethods(paymentMethods) {
    const container = document.getElementById('saved-payment-methods');
    container.innerHTML = '';

    if (paymentMethods.length === 0) {
        container.innerHTML = '<p>No saved payment methods found.</p>';
        return;
    }

    paymentMethods.forEach(pm => {
        const div = document.createElement('div');
        div.className = 'payment-method';
        div.innerHTML = `
            <div>
                <strong>${pm.brand.toUpperCase()} •••• ${pm.last4}</strong><br>
                <small>Expires ${pm.expMonth}/${pm.expYear}</small>
            </div>
            <div>
                <button type="button" onclick="selectPaymentMethod('${pm.id}', '${pm.brand} •••• ${pm.last4}')">Select</button>
                <button type="button" onclick="removePaymentMethod('${pm.id}')" class="danger">Remove</button>
            </div>
        `;
        container.appendChild(div);
    });
}

function selectPaymentMethod(paymentMethodId, displayText) {
    selectedPaymentMethodId = paymentMethodId;
    document.getElementById('selected-method-info').textContent = displayText;
    document.getElementById('selected-payment-method').classList.remove('hidden');
    document.getElementById('returning-submit-button').disabled = false;
}

// Returning Customer Payment with Saved Method
document.getElementById('returning-payment-form').addEventListener('submit', async function(event) {
    event.preventDefault();

    if (!selectedPaymentMethodId || !currentCustomer) {
        alert('Please select a payment method');
        return;
    }

    const submitButton = document.getElementById('returning-submit-button');
    const buttonText = document.getElementById('returning-button-text');
    const spinner = document.getElementById('returning-spinner');

    toggleButton(submitButton, buttonText, spinner, true);

    const amount = document.getElementById('returning-amount').value;

    try {
        const response = await fetch('/api/payment/create-intent-with-saved-method', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                customerId: currentCustomer.customerId,
                paymentMethodId: selectedPaymentMethodId,
                amount: parseFloat(amount),
                currency: 'usd'
            })
        });

        const { clientSecret } = await response.json();

        const result = await stripe.confirmCardPayment(clientSecret);
        displayResult('returning-payment-result', result);
    } catch (error) {
        displayError('returning-payment-result', error.message);
    }

    toggleButton(submitButton, buttonText, spinner, false);
});

// Add new card functionality
function showAddNewCard() {
    document.getElementById('add-new-card-form').classList.remove('hidden');
}

function hideAddNewCard() {
    document.getElementById('add-new-card-form').classList.add('hidden');
}

async function payWithNewCard() {
    if (!currentCustomer) {
        alert('No customer selected');
        return;
    }

    const amount = document.getElementById('returning-amount').value;

    try {
        // First, create payment method to check funding type
        const {paymentMethod, error: pmError} = await stripe.createPaymentMethod({
            type: 'card',
            card: window.returningCardElement
        });

        if (pmError) {
            displayError('returning-payment-result', pmError.message);
            return;
        }

        // Block credit cards
        if (paymentMethod.card.funding === 'credit') {
            displayError('returning-payment-result', '❌ Credit cards are not accepted. Please use a debit card.');
            document.getElementById('returning-card-error').textContent = '❌ Credit cards are not accepted. Please use a debit card.';
            document.getElementById('returning-card-error').style.color = '#dc3545';
            document.getElementById('returning-card-error').style.fontWeight = 'bold';
            return;
        }

        const response = await fetch('/api/payment/create-intent', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                amount: parseFloat(amount),
                currency: 'usd',
                savePaymentMethod: true,
                existingCustomerId: currentCustomer.customerId
            })
        });

        const { clientSecret } = await response.json();

        const result = await stripe.confirmCardPayment(clientSecret, {
            payment_method: paymentMethod.id
        });

        displayResult('returning-payment-result', result);

        if (result.paymentIntent && result.paymentIntent.status === 'succeeded') {
            // Refresh customer info to show new saved card
            setTimeout(() => lookupCustomer(), 1000);
        }
    } catch (error) {
        displayError('returning-payment-result', error.message);
    }
}

// Save Card Only functionality
async function findCustomerForSaving() {
    const customerId = document.getElementById('save-customer-id').value;
    if (!customerId) {
        alert('Please enter a Stripe Customer ID');
        return;
    }

    try {
        const response = await fetch(`/api/customer/${encodeURIComponent(customerId)}`);

        if (response.ok) {
            const customer = await response.json();
            document.getElementById('save-found-customer-name').textContent = customer.name;
            document.getElementById('save-found-customer-email').textContent = customer.email;
            document.getElementById('save-customer-found').classList.remove('hidden');
            currentCustomer = customer;
        } else {
            alert('Customer not found. Please create an account first.');
        }
    } catch (error) {
        alert('Error finding customer: ' + error.message);
    }
}

document.getElementById('save-card-form').addEventListener('submit', async function(event) {
    event.preventDefault();

    if (!currentCustomer) {
        alert('No customer selected');
        return;
    }

    const submitButton = document.getElementById('save-card-button');
    const buttonText = document.getElementById('save-card-button-text');
    const spinner = document.getElementById('save-card-spinner');

    toggleButton(submitButton, buttonText, spinner, true);

    try {
        // First, create payment method to check funding type
        const {paymentMethod, error: pmError} = await stripe.createPaymentMethod({
            type: 'card',
            card: window.saveCardElement
        });

        if (pmError) {
            displayError('save-card-result', pmError.message);
            toggleButton(submitButton, buttonText, spinner, false);
            return;
        }

        // Block credit cards
        if (paymentMethod.card.funding === 'credit') {
            displayError('save-card-result', '❌ Credit cards are not accepted. Please use a debit card.');
            document.getElementById('save-card-error').textContent = '❌ Credit cards are not accepted. Please use a debit card.';
            document.getElementById('save-card-error').style.color = '#dc3545';
            document.getElementById('save-card-error').style.fontWeight = 'bold';
            toggleButton(submitButton, buttonText, spinner, false);
            return;
        }

        // Create setup intent
        const response = await fetch(`/api/customer/${currentCustomer.customerId}/setup-intent`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });

        const { clientSecret } = await response.json();

        // Confirm setup intent with existing payment method
        const result = await stripe.confirmCardSetup(clientSecret, {
            payment_method: paymentMethod.id
        });

        if (result.error) {
            displayError('save-card-result', result.error.message);
        } else {
            displaySuccess('save-card-result', 'Payment method saved successfully!');
        }
    } catch (error) {
        displayError('save-card-result', error.message);
    }

    toggleButton(submitButton, buttonText, spinner, false);
});

// Utility functions
function toggleButton(button, textSpan, spinner, isLoading) {
    button.disabled = isLoading;
    textSpan.classList.toggle('hidden', isLoading);
    spinner.classList.toggle('hidden', !isLoading);
}

function displayResult(containerId, result, extraInfo = '') {
    const container = document.getElementById(containerId);
    if (result.error) {
        container.innerHTML = `<div class="error">Payment failed: ${result.error.message}</div>`;
    } else {
        container.innerHTML = `
            <div class="success">
                Payment succeeded!
                <br><strong>Payment ID:</strong> ${result.paymentIntent.id}
                ${extraInfo ? '<br>' + extraInfo : ''}
            </div>
        `;
    }
}

function displayError(containerId, message) {
    document.getElementById(containerId).innerHTML = `<div class="error">Error: ${message}</div>`;
}

function displaySuccess(containerId, message) {
    document.getElementById(containerId).innerHTML = `<div class="success">${message}</div>`;
}

async function removePaymentMethod(paymentMethodId) {
    if (!confirm('Are you sure you want to remove this payment method?')) {
        return;
    }

    try {
        const response = await fetch(`/api/customer/payment-method/${paymentMethodId}`, {
            method: 'DELETE'
        });

        if (response.ok) {
            alert('Payment method removed successfully');
            // Refresh the customer info
            if (currentCustomer) {
                lookupCustomer();
            }
        } else {
            alert('Error removing payment method');
        }
    } catch (error) {
        alert('Error: ' + error.message);
    }
}