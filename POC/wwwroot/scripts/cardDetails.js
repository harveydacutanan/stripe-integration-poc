// Global variables
let stripe = null;
let cardElement = null;
let setupIntentClientSecret = null;

// Initialize on page load
document.addEventListener('DOMContentLoaded', async function() {
    await initializeStripe();

    // Show SetupIntent section when Stripe is ready
    if (personalInfo && personalInfo.stripe_customerId) {
        document.getElementById('setup-intent-section').style.display = 'block';
    }
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

        // Create card element with Link disabled
        const elements = stripe.elements({
            mode: 'setup',
            currency: 'aud'
        });
        cardElement = elements.create('card', {
            hidePostalCode: true,
            disableLink: true,
            style: {
                base: {
                    fontSize: '16px',
                    color: '#313131',
                    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen, Ubuntu, Cantarell, sans-serif',
                    '::placeholder': {
                        color: '#999'
                    }
                }
            }
        });

        cardElement.mount('#card-element');

        cardElement.on('change', function(event) {
            const displayError = document.getElementById('card-error');
            const validationInfo = document.getElementById('card-validation-info');
            const brandDisplay = document.getElementById('card-brand-display');
            const fundingDisplay = document.getElementById('card-funding-display');

            // Handle errors
            if (event.error) {
                displayError.textContent = event.error.message;
                validationInfo.style.display = 'none';
            } else {
                displayError.textContent = '';
            }

            // Display card brand and status
            if (event.complete) {
                validationInfo.style.display = 'block';
                validationInfo.style.background = '#d4edda';
                validationInfo.style.borderLeftColor = '#28a745';

                // Display brand
                const brand = event.brand ? event.brand.toUpperCase() : 'UNKNOWN';
                brandDisplay.textContent = brand;

                // We can't get funding type until after submission, so show "Validating..."
                fundingDisplay.textContent = 'Will be validated on submit';
            } else if (event.brand) {
                // Show brand as user types
                validationInfo.style.display = 'block';
                validationInfo.style.background = '#f0f9e8';
                validationInfo.style.borderLeftColor = '#BBEE00';
                brandDisplay.textContent = event.brand.toUpperCase();
                fundingDisplay.textContent = 'Incomplete';
            } else {
                validationInfo.style.display = 'none';
            }
        });

        console.log(`Stripe initialized in ${config.environment} mode`);
    } catch (error) {
        console.error('Failed to initialize Stripe:', error);
        document.getElementById('card-error').textContent = 'Failed to initialize payment system. Please refresh the page.';
    }
}

// Load personal info from sessionStorage
const personalInfo = JSON.parse(sessionStorage.getItem('personalInfo'));

if (!personalInfo) {
    alert('Please complete personal information first.');
    window.location.href = 'PersonalInfo.html';
} else {
    // Display customer summary
    document.getElementById('summary-name').textContent = `${personalInfo.firstName} ${personalInfo.lastName}`;
    document.getElementById('summary-email').textContent = personalInfo.email;
    document.getElementById('summary-phone').textContent = personalInfo.phone;

    // Display Stripe Customer ID
    if (personalInfo.stripe_customerId) {
        document.getElementById('stripe-customer-id').textContent = personalInfo.stripe_customerId;
    } else {
        document.getElementById('stripe-customer-id').textContent = 'Not available';
    }
}

// Handle Create SetupIntent button
document.getElementById('create-setupintent-btn').addEventListener('click', async function() {
    const createBtn = document.getElementById('create-setupintent-btn');
    const createText = document.getElementById('create-setupintent-text');
    const createSpinner = document.getElementById('create-setupintent-spinner');
    const resultDiv = document.getElementById('setupintent-result');

    if (!personalInfo || !personalInfo.stripe_customerId) {
        resultDiv.innerHTML = `
            <div style="background: #f8d7da; color: #721c24; padding: 15px; border-radius: 8px; border-left: 4px solid #dc3545;">
                <strong>✗ Error:</strong> Stripe Customer ID not found. Please go back and create a customer first.
            </div>
        `;
        return;
    }

    // Disable button and show loading
    createBtn.disabled = true;
    createText.classList.add('hidden');
    createSpinner.classList.remove('hidden');
    resultDiv.innerHTML = '';

    try {
        // Call API to create SetupIntent
        const response = await fetch(`/api/customer/${personalInfo.stripe_customerId}/setup-intent`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        });

        const result = await response.json();

        if (!response.ok) {
            throw new Error(result.error || 'Failed to create SetupIntent');
        }

        // Store client secret
        setupIntentClientSecret = result.clientSecret;

        // Display success message with SetupIntent ID
        resultDiv.innerHTML = `
            <div style="background: #d4edda; color: #155724; padding: 15px; border-radius: 8px; border-left: 4px solid #28a745;">
                <strong>✓ SetupIntent Created Successfully!</strong><br>
                <strong>SetupIntent ID:</strong> <code style="background: #fff; padding: 2px 6px; border-radius: 4px;">${result.setupIntentId}</code><br>
                <p style="margin-top: 10px;">Now you can enter your card details below.</p>
            </div>
        `;

        // Hide create button and show card form
        createBtn.style.display = 'none';
        document.getElementById('card-details-form').style.display = 'block';

    } catch (error) {
        console.error('Error creating SetupIntent:', error);
        resultDiv.innerHTML = `
            <div style="background: #f8d7da; color: #721c24; padding: 15px; border-radius: 8px; border-left: 4px solid #dc3545;">
                <strong>✗ Error:</strong> ${error.message}
            </div>
        `;

        // Re-enable button
        createBtn.disabled = false;
        createText.classList.remove('hidden');
        createSpinner.classList.add('hidden');
    }
});

// Form submission - confirm card setup with SetupIntent
document.getElementById('card-details-form').addEventListener('submit', async function(e) {
    e.preventDefault();

    if (!setupIntentClientSecret) {
        document.getElementById('card-error').textContent = 'Please create a SetupIntent first.';
        return;
    }

    const submitButton = document.getElementById('submit-button');
    const buttonText = document.getElementById('button-text');
    const spinner = document.getElementById('spinner');

    submitButton.disabled = true;
    buttonText.classList.add('hidden');
    spinner.classList.remove('hidden');

    try {
        // First, create the payment method to check funding type
        const {paymentMethod, error: pmError} = await stripe.createPaymentMethod({
            type: 'card',
            card: cardElement,
            billing_details: {
                name: `${personalInfo.firstName} ${personalInfo.lastName}`,
                email: personalInfo.email,
                phone: personalInfo.phone,
                address: {
                    line1: personalInfo.address,
                    city: personalInfo.city,
                    state: personalInfo.state,
                    country: 'AU'
                }
            }
        });

        if (pmError) {
            document.getElementById('card-error').textContent = pmError.message;
            submitButton.disabled = false;
            buttonText.classList.remove('hidden');
            spinner.classList.add('hidden');
            return;
        }

        // Check if card is credit card and block it
        const fundingType = paymentMethod.card.funding;
        if (fundingType === 'credit') {
            document.getElementById('card-error').textContent = '❌ Credit cards are not accepted. Please use a debit card.';
            document.getElementById('card-error').style.color = '#dc3545';
            document.getElementById('card-error').style.fontWeight = 'bold';
            submitButton.disabled = false;
            buttonText.classList.remove('hidden');
            spinner.classList.add('hidden');
            return;
        }

        // If not credit card, proceed with confirming the setup intent
        const {setupIntent, error} = await stripe.confirmCardSetup(setupIntentClientSecret, {
            payment_method: paymentMethod.id
        });

        if (error) {
            document.getElementById('card-error').textContent = error.message;
            submitButton.disabled = false;
            buttonText.classList.remove('hidden');
            spinner.classList.add('hidden');
        } else {
            // Save setup intent and payment method info
            sessionStorage.setItem('setupIntentId', setupIntent.id);

            // Extract payment method ID (could be string or object)
            const paymentMethodId = typeof setupIntent.payment_method === 'string'
                ? setupIntent.payment_method
                : setupIntent.payment_method.id;
            sessionStorage.setItem('paymentMethodId', paymentMethodId);
            sessionStorage.setItem('setupIntentStatus', setupIntent.status);

            // Get payment method details for display
            const paymentMethod = setupIntent.payment_method;
            if (typeof paymentMethod === 'object' && paymentMethod.card) {
                sessionStorage.setItem('cardBrand', paymentMethod.card.brand);
                sessionStorage.setItem('cardLast4', paymentMethod.card.last4);
            }

            // Navigate to E-Sign page
            window.location.href = 'EsignPage.html';
        }
    } catch (err) {
        console.error('Error:', err);
        document.getElementById('card-error').textContent = 'An error occurred. Please try again.';
        submitButton.disabled = false;
        buttonText.classList.remove('hidden');
        spinner.classList.add('hidden');
    }
});