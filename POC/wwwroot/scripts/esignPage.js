// Load data from sessionStorage
const personalInfo = JSON.parse(sessionStorage.getItem('personalInfo'));
const paymentMethodId = sessionStorage.getItem('paymentMethodId');
const cardBrand = sessionStorage.getItem('cardBrand');
const cardLast4 = sessionStorage.getItem('cardLast4');
const setupIntentId = sessionStorage.getItem('setupIntentId');
const setupIntentStatus = sessionStorage.getItem('setupIntentStatus');

if (!personalInfo || !paymentMethodId) {
    alert('Please complete all previous steps first.');
    window.location.href = 'PersonalInfo.html';
} else {
    // Display application summary
    document.getElementById('summary-name').textContent = `${personalInfo.firstName} ${personalInfo.lastName}`;
    document.getElementById('summary-email').textContent = personalInfo.email;
    document.getElementById('summary-phone').textContent = personalInfo.phone;
    document.getElementById('summary-address').textContent = `${personalInfo.address}, ${personalInfo.city}, ${personalInfo.state} ${personalInfo.postcode}`;

    // Display Stripe Customer Details
    if (personalInfo.stripe_customerId) {
        document.getElementById('stripe-customer-id').textContent = personalInfo.stripe_customerId;
        document.getElementById('customer-status').textContent = 'Active';
        document.getElementById('customer-status').style.color = '#28a745';
        document.getElementById('customer-status').style.fontWeight = 'bold';
    } else {
        document.getElementById('stripe-customer-id').textContent = 'Not available';
        document.getElementById('customer-status').textContent = 'Unknown';
    }

    // Display Payment Method Details
    if (setupIntentId) {
        document.getElementById('setupintent-id').textContent = setupIntentId;
    } else {
        document.getElementById('setupintent-id').textContent = 'Not available';
    }

    if (setupIntentStatus) {
        document.getElementById('setupintent-status').textContent = setupIntentStatus.toUpperCase();
        if (setupIntentStatus === 'succeeded') {
            document.getElementById('setupintent-status').style.color = '#28a745';
            document.getElementById('setupintent-status').style.fontWeight = 'bold';
        }
    } else {
        document.getElementById('setupintent-status').textContent = 'Unknown';
    }

    if (paymentMethodId) {
        document.getElementById('payment-method-id').textContent = paymentMethodId;

        // Fetch payment method details from Stripe API
        fetchPaymentMethodDetails(paymentMethodId);
    } else {
        document.getElementById('payment-method-id').textContent = 'Not available';
    }

    if (cardBrand && cardLast4) {
        document.getElementById('summary-card').textContent = `${cardBrand.toUpperCase()} ending in ${cardLast4}`;
    } else {
        document.getElementById('summary-card').textContent = 'Not available';
    }
}

// Fetch payment method details from Stripe API
async function fetchPaymentMethodDetails(paymentMethodId) {
    const indicator = document.getElementById('stripe-data-indicator');

    try {
        console.log('Fetching payment method:', paymentMethodId);
        const response = await fetch(`/api/payment/payment-method/${paymentMethodId}`);

        console.log('Response status:', response.status);
        console.log('Response content-type:', response.headers.get('content-type'));

        if (!response.ok) {
            const errorData = await response.text();
            console.error('API Error Response:', errorData);
            throw new Error(`Failed to fetch payment method details: ${response.status}`);
        }

        const contentType = response.headers.get('content-type');
        if (!contentType || !contentType.includes('application/json')) {
            const textResponse = await response.text();
            console.error('Received non-JSON response:', textResponse.substring(0, 200));
            throw new Error('API returned HTML instead of JSON - endpoint may not exist');
        }

        const paymentMethod = await response.json();

        // Update indicator to show success
        indicator.textContent = '✓ LIVE DATA FROM STRIPE';
        indicator.style.background = '#28a745';
        indicator.style.color = '#fff';

        // Populate all payment method fields
        document.getElementById('pm-type').textContent = paymentMethod.type.toUpperCase();

        // Format created date
        const createdDate = new Date(paymentMethod.created * 1000);
        document.getElementById('pm-created').textContent = createdDate.toLocaleString();

        // Customer ID
        if (paymentMethod.customerId) {
            document.getElementById('pm-customer-id').textContent = paymentMethod.customerId;
        } else {
            document.getElementById('pm-customer-id').textContent = 'Not attached';
        }

        // Card information
        if (paymentMethod.card) {
            const cardDetails = `${paymentMethod.card.brand.toUpperCase()} •••• ${paymentMethod.card.last4} (Exp: ${paymentMethod.card.expMonth}/${paymentMethod.card.expYear})`;
            document.getElementById('summary-card').textContent = cardDetails;

            document.getElementById('pm-card-funding').textContent = paymentMethod.card.funding.toUpperCase();
            document.getElementById('pm-card-country').textContent = paymentMethod.card.country || 'N/A';
            document.getElementById('pm-card-fingerprint').textContent = paymentMethod.card.fingerprint || 'N/A';
        }

        // Billing details
        if (paymentMethod.billingDetails) {
            document.getElementById('pm-billing-name').textContent = paymentMethod.billingDetails.name || 'N/A';
            document.getElementById('pm-billing-email').textContent = paymentMethod.billingDetails.email || 'N/A';
            document.getElementById('pm-billing-phone').textContent = paymentMethod.billingDetails.phone || 'N/A';

            // Format address
            if (paymentMethod.billingDetails.address) {
                const addr = paymentMethod.billingDetails.address;
                const addressParts = [
                    addr.line1,
                    addr.line2,
                    addr.city,
                    addr.state,
                    addr.postalCode,
                    addr.country
                ].filter(part => part && part.trim() !== '');

                document.getElementById('pm-billing-address').textContent = addressParts.join(', ') || 'N/A';
            } else {
                document.getElementById('pm-billing-address').textContent = 'N/A';
            }
        }

        // Log full details to console for POC demonstration
        console.log('===== STRIPE PAYMENT METHOD DETAILS (POC) =====');
        console.log('Full Payment Method Object:', paymentMethod);
        console.log('==============================================');

    } catch (error) {
        console.error('Error fetching payment method:', error);

        // Update indicator to show error
        indicator.textContent = '✗ FAILED TO LOAD';
        indicator.style.background = '#dc3545';
        indicator.style.color = '#fff';

        // Keep the existing card details from sessionStorage if API call fails
    }
}

// Signature canvas setup
const canvas = document.getElementById('signature-canvas');
const ctx = canvas.getContext('2d');
let isDrawing = false;
let hasSignature = false;

// Set canvas size
canvas.width = canvas.offsetWidth;
canvas.height = 200;

// Drawing functions
function startDrawing(e) {
    isDrawing = true;
    hasSignature = true;
    const rect = canvas.getBoundingClientRect();
    const x = (e.clientX || e.touches[0].clientX) - rect.left;
    const y = (e.clientY || e.touches[0].clientY) - rect.top;
    ctx.beginPath();
    ctx.moveTo(x, y);
}

function draw(e) {
    if (!isDrawing) return;
    e.preventDefault();
    const rect = canvas.getBoundingClientRect();
    const x = (e.clientX || e.touches[0].clientX) - rect.left;
    const y = (e.clientY || e.touches[0].clientY) - rect.top;
    ctx.lineTo(x, y);
    ctx.strokeStyle = '#0B2828';
    ctx.lineWidth = 2;
    ctx.lineCap = 'round';
    ctx.stroke();
}

function stopDrawing() {
    isDrawing = false;
}

// Mouse events
canvas.addEventListener('mousedown', startDrawing);
canvas.addEventListener('mousemove', draw);
canvas.addEventListener('mouseup', stopDrawing);
canvas.addEventListener('mouseout', stopDrawing);

// Touch events
canvas.addEventListener('touchstart', startDrawing);
canvas.addEventListener('touchmove', draw);
canvas.addEventListener('touchend', stopDrawing);

function clearSignature() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    hasSignature = false;
    document.getElementById('signature-error').textContent = '';
}

// Form submission
document.getElementById('esign-form').addEventListener('submit', async function(e) {
    e.preventDefault();

    const termsAccepted = document.getElementById('accept-terms').checked;
    const signatureError = document.getElementById('signature-error');

    if (!termsAccepted) {
        alert('Please accept the terms and conditions.');
        return;
    }

    if (!hasSignature) {
        signatureError.textContent = 'Please provide your signature before submitting.';
        return;
    }

    signatureError.textContent = '';

    const submitButton = document.getElementById('submit-button');
    const buttonText = document.getElementById('button-text');
    const spinner = document.getElementById('spinner');

    submitButton.disabled = true;
    buttonText.classList.add('hidden');
    spinner.classList.remove('hidden');

    // Simulate API call
    setTimeout(() => {
        // Get signature as base64
        const signatureData = canvas.toDataURL();

        // Create application object
        const application = {
            personalInfo: personalInfo,
            paymentMethodId: paymentMethodId,
            cardBrand: cardBrand,
            cardLast4: cardLast4,
            signature: signatureData,
            timestamp: new Date().toISOString(),
            applicationId: 'APP-' + Math.random().toString(36).substr(2, 9).toUpperCase()
        };

        // Save to localStorage (in real app, send to backend)
        localStorage.setItem('completedApplication', JSON.stringify(application));

        // Show success message
        document.getElementById('application-id').textContent = application.applicationId;
        document.getElementById('esign-form').style.display = 'none';
        document.getElementById('success-message').style.display = 'block';

        // Clear sessionStorage
        sessionStorage.clear();

        submitButton.disabled = false;
        buttonText.classList.remove('hidden');
        spinner.classList.add('hidden');
    }, 2000);
});