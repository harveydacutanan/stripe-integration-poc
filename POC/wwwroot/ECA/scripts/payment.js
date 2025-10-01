// Payment processing for existing customers
let stripe;
let cardElement;
let savedPaymentMethods = [];
let selectedPaymentMethodId = null;
let useNewCard = false;

document.addEventListener('DOMContentLoaded', async function() {
    // Check authentication
    checkAuthentication();

    // Load customer information
    loadCustomerInfo();

    // Initialize Stripe
    await initializeStripe();

    // Load saved payment methods
    await loadSavedPaymentMethods();

    // Setup form handler
    const paymentForm = document.getElementById('paymentForm');
    paymentForm.addEventListener('submit', handlePaymentSubmit);

    // Setup toggle buttons
    document.getElementById('use-new-card-btn').addEventListener('click', showNewCardSection);
    document.getElementById('use-saved-card-btn').addEventListener('click', showSavedCardsSection);
});

function checkAuthentication() {
    const isAuthenticated = sessionStorage.getItem('eca_authenticated');

    if (!isAuthenticated || isAuthenticated !== 'true') {
        window.location.href = 'Login.html';
    }
}

function loadCustomerInfo() {
    const customerId = sessionStorage.getItem('eca_customerId');
    const email = sessionStorage.getItem('eca_email');

    if (customerId && email) {
        document.getElementById('customerIdDisplay').textContent = customerId;
        document.getElementById('customerEmailDisplay').textContent = email;
    }
}

async function initializeStripe() {
    try {
        // Get Stripe publishable key from configuration endpoint
        const response = await fetch('/api/configuration/stripe');

        if (!response.ok) {
            throw new Error('Failed to load Stripe configuration');
        }

        const data = await response.json();

        // Initialize Stripe
        stripe = Stripe(data.publishableKey);

        // Create card element
        const elements = stripe.elements();
        cardElement = elements.create('card', {
            hidePostalCode: true,
            style: {
                base: {
                    fontSize: '16px',
                    color: '#32325d',
                    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
                    '::placeholder': {
                        color: '#aab7c4'
                    }
                },
                invalid: {
                    color: '#fa755a',
                    iconColor: '#fa755a'
                }
            }
        });

        cardElement.mount('#card-element');

        // Handle real-time validation errors
        cardElement.on('change', function(event) {
            const displayError = document.getElementById('card-errors');
            if (event.error) {
                displayError.textContent = event.error.message;
            } else {
                displayError.textContent = '';
            }
        });

    } catch (error) {
        console.error('Error initializing Stripe:', error);
        showError('Failed to initialize payment system. Please try again later.');
    }
}

async function loadSavedPaymentMethods() {
    const customerId = sessionStorage.getItem('eca_customerId');

    try {
        console.log('Fetching payment methods for customer:', customerId);
        const url = `/api/payment/saved-payment-methods/${customerId}`;
        console.log('Full URL:', url);

        const response = await fetch(url);

        console.log('Response status:', response.status);
        console.log('Response headers:', response.headers.get('content-type'));

        if (!response.ok) {
            const errorText = await response.text();
            console.error('Error response:', errorText);
            throw new Error(`Failed to load saved payment methods: ${response.status}`);
        }

        const contentType = response.headers.get('content-type');
        if (!contentType || !contentType.includes('application/json')) {
            const text = await response.text();
            console.error('Received non-JSON response:', text.substring(0, 200));
            throw new Error('Server returned HTML instead of JSON. API endpoint may not exist.');
        }

        savedPaymentMethods = await response.json();
        console.log('Saved payment methods:', savedPaymentMethods);

        if (savedPaymentMethods.length > 0) {
            displaySavedCards();
            showSavedCardsSection();
        } else {
            console.log('No saved payment methods found, showing new card section');
            showNewCardSection();
        }

    } catch (error) {
        console.error('Error loading saved payment methods:', error);
        // If error, just show new card section
        showNewCardSection();
    }
}

function displaySavedCards() {
    const savedCardsList = document.getElementById('saved-cards-list');
    savedCardsList.innerHTML = '';

    if (savedPaymentMethods.length === 0) {
        savedCardsList.innerHTML = '<p class="loading-cards">No saved cards found.</p>';
        return;
    }

    savedPaymentMethods.forEach(pm => {
        const cardItem = document.createElement('div');
        cardItem.className = 'saved-card-item';
        cardItem.dataset.paymentMethodId = pm.id;

        const brandIcon = getCardBrandIcon(pm.brand);

        cardItem.innerHTML = `
            <div class="saved-card-info">
                <span class="card-brand-icon">${brandIcon}</span>
                <div class="card-details">
                    <span class="card-number">${pm.brand.toUpperCase()} •••• ${pm.last4}</span>
                    <span class="card-expiry">Expires ${pm.expMonth}/${pm.expYear}</span>
                </div>
            </div>
            <div class="card-actions">
                <button type="button" class="btn-remove-card" onclick="removeCard('${pm.id}', event)">Remove</button>
            </div>
        `;

        cardItem.addEventListener('click', function(e) {
            if (!e.target.classList.contains('btn-remove-card')) {
                selectCard(pm.id);
            }
        });

        savedCardsList.appendChild(cardItem);
    });

    // Auto-select first card
    if (savedPaymentMethods.length > 0) {
        selectCard(savedPaymentMethods[0].id);
    }
}

function selectCard(paymentMethodId) {
    // Remove selection from all cards
    document.querySelectorAll('.saved-card-item').forEach(item => {
        item.classList.remove('selected');
    });

    // Add selection to clicked card
    const selectedCard = document.querySelector(`[data-payment-method-id="${paymentMethodId}"]`);
    if (selectedCard) {
        selectedCard.classList.add('selected');
        selectedPaymentMethodId = paymentMethodId;
    }
}

async function removeCard(paymentMethodId, event) {
    event.stopPropagation();

    if (!confirm('Are you sure you want to remove this card?')) {
        return;
    }

    try {
        const response = await fetch(`/api/payment/payment-method/${paymentMethodId}`, {
            method: 'DELETE'
        });

        if (!response.ok) {
            throw new Error('Failed to remove card');
        }

        // Reload saved payment methods
        await loadSavedPaymentMethods();

        // Show success message
        showSuccess('Card removed successfully');

    } catch (error) {
        console.error('Error removing card:', error);
        showError('Failed to remove card. Please try again.');
    }
}

function getCardBrandIcon(brand) {
    const icons = {
        'visa': '💳',
        'mastercard': '💳',
        'amex': '💳',
        'discover': '💳',
        'diners': '💳',
        'jcb': '💳',
        'unionpay': '💳'
    };
    return icons[brand.toLowerCase()] || '💳';
}

function showSavedCardsSection() {
    document.getElementById('saved-cards-section').style.display = 'block';
    document.getElementById('new-card-section').style.display = 'none';
    useNewCard = false;

    // Show "use saved card" button only if there are saved cards
    if (savedPaymentMethods.length > 0) {
        document.getElementById('use-saved-card-btn').style.display = 'block';
    }
}

function showNewCardSection() {
    document.getElementById('saved-cards-section').style.display = 'none';
    document.getElementById('new-card-section').style.display = 'block';
    useNewCard = true;
    selectedPaymentMethodId = null;
}

async function handlePaymentSubmit(e) {
    e.preventDefault();

    const submitBtn = document.getElementById('submitBtn');
    const processingMessage = document.getElementById('processingMessage');
    const errorMessage = document.getElementById('errorMessage');
    const successMessage = document.getElementById('successMessage');

    // Get form values
    const amount = parseFloat(document.getElementById('amount').value);
    const description = document.getElementById('description').value;
    const customerId = sessionStorage.getItem('eca_customerId');
    const saveCard = document.getElementById('save-card-checkbox').checked;

    // Validate amount
    if (isNaN(amount) || amount <= 0) {
        showError('Please enter a valid amount');
        return;
    }

    // Disable submit button and show processing
    submitBtn.disabled = true;
    processingMessage.style.display = 'flex';
    errorMessage.style.display = 'none';
    successMessage.style.display = 'none';

    try {
        if (useNewCard) {
            // Process with new card
            await processPaymentWithNewCard(amount, description, customerId, saveCard);
        } else {
            // Process with saved card
            await processPaymentWithSavedCard(amount, description, customerId);
        }

        // Payment successful
        processingMessage.style.display = 'none';
        successMessage.style.display = 'block';

        // Clear form
        document.getElementById('paymentForm').reset();
        if (useNewCard) {
            cardElement.clear();
        }

        // Reload saved cards if a new card was saved
        if (saveCard) {
            await loadSavedPaymentMethods();
        }

        // Redirect back to dashboard after 3 seconds
        setTimeout(() => {
            window.location.href = 'Welcome.html';
        }, 3000);

    } catch (error) {
        console.error('Payment error:', error);
        processingMessage.style.display = 'none';
        showError(error.message || 'Payment failed. Please try again.');
        submitBtn.disabled = false;
    }
}

async function processPaymentWithNewCard(amount, description, customerId, saveCard) {
    // Create payment method first to check funding type
    const { error: pmError, paymentMethod } = await stripe.createPaymentMethod({
        type: 'card',
        card: cardElement,
        billing_details: {
            email: sessionStorage.getItem('eca_email')
        }
    });

    if (pmError) {
        throw new Error(pmError.message);
    }

    // Check if card is a credit card
    if (paymentMethod.card && paymentMethod.card.funding === 'credit') {
        throw new Error('Credit cards are not accepted. Please use a debit or prepaid card.');
    }

    // Create payment intent
    const paymentIntentResponse = await fetch('/api/payment/create-payment-intent', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            amount: Math.round(amount * 100), // Convert to cents
            currency: 'aud',
            customerId: customerId,
            description: description,
            savePaymentMethod: saveCard,
            paymentMethodId: paymentMethod.id
        })
    });

    if (!paymentIntentResponse.ok) {
        const errorData = await paymentIntentResponse.json();
        throw new Error(errorData.error || 'Failed to create payment intent');
    }

    const { clientSecret, paymentIntentId } = await paymentIntentResponse.json();

    // Confirm payment with the created payment method
    const { error, paymentIntent } = await stripe.confirmCardPayment(clientSecret, {
        payment_method: paymentMethod.id
    });

    if (error) {
        throw new Error(error.message);
    }

    document.getElementById('paymentIntentId').textContent = paymentIntent.id;
}

async function processPaymentWithSavedCard(amount, description, customerId) {
    if (!selectedPaymentMethodId) {
        throw new Error('Please select a payment method');
    }

    // Create payment intent with saved payment method
    const paymentIntentResponse = await fetch('/api/payment/create-payment-intent', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            amount: Math.round(amount * 100), // Convert to cents
            currency: 'aud',
            customerId: customerId,
            paymentMethodId: selectedPaymentMethodId,
            description: description,
            savePaymentMethod: false
        })
    });

    if (!paymentIntentResponse.ok) {
        const errorData = await paymentIntentResponse.json();
        throw new Error(errorData.error || 'Failed to create payment intent');
    }

    const { clientSecret, paymentIntentId } = await paymentIntentResponse.json();

    // Confirm payment with saved payment method
    const { error, paymentIntent } = await stripe.confirmCardPayment(clientSecret, {
        payment_method: selectedPaymentMethodId
    });

    if (error) {
        throw new Error(error.message);
    }

    document.getElementById('paymentIntentId').textContent = paymentIntent.id;
}

function showError(message) {
    const errorMessage = document.getElementById('errorMessage');
    errorMessage.textContent = message;
    errorMessage.style.display = 'block';

    setTimeout(() => {
        errorMessage.style.display = 'none';
    }, 5000);
}

function showSuccess(message) {
    // Create temporary success notification
    const notification = document.createElement('div');
    notification.className = 'success-message';
    notification.style.marginBottom = '20px';
    notification.innerHTML = `<p>${message}</p>`;

    const container = document.querySelector('.payment-form-card');
    container.insertBefore(notification, container.firstChild);

    setTimeout(() => {
        notification.remove();
    }, 3000);
}

function goBack() {
    window.location.href = 'Welcome.html';
}
