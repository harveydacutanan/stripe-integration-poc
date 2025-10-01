let createdStripeCustomerId = null;

// Populate test data button
document.getElementById('populate-test-data').addEventListener('click', function() {
    const timestamp = new Date().getTime().toString().slice(-6);
    const today = new Date();
    const birthDate = new Date(today.getFullYear() - 30, today.getMonth(), today.getDate());

    document.getElementById('first-name').value = `wagtest${timestamp}`;
    document.getElementById('last-name').value = 'User';
    document.getElementById('email').value = `wagtest${timestamp}@test.com`;
    document.getElementById('phone').value = '0469322594';
    document.getElementById('dob').value = birthDate.toISOString().split('T')[0];
    document.getElementById('address').value = '123 Test Street';
    document.getElementById('city').value = 'Sydney';
    document.getElementById('state').value = 'NSW';
    document.getElementById('postcode').value = '2000';

    // Trigger input event to show customer creation section
    document.getElementById('personal-info-form').dispatchEvent(new Event('input'));
});

// Form validation - show customer creation section when form is valid
document.getElementById('personal-info-form').addEventListener('input', function() {
    const form = document.getElementById('personal-info-form');
    const customerSection = document.getElementById('customer-creation-section');

    if (form.checkValidity()) {
        customerSection.style.display = 'block';
    }
});

// Handle Create Customer button
document.getElementById('create-customer-btn').addEventListener('click', async function() {
    const createBtn = document.getElementById('create-customer-btn');
    const createText = document.getElementById('create-customer-text');
    const createSpinner = document.getElementById('create-customer-spinner');
    const resultDiv = document.getElementById('customer-result');
    const nextButton = document.getElementById('next-button');

    // Disable button and show loading
    createBtn.disabled = true;
    createText.classList.add('hidden');
    createSpinner.classList.remove('hidden');
    resultDiv.innerHTML = '';

    try {
        // Collect form data
        const customerData = {
            name: `${document.getElementById('first-name').value} ${document.getElementById('last-name').value}`,
            email: document.getElementById('email').value,
            phone: document.getElementById('phone').value,
            address: {
                line1: document.getElementById('address').value,
                city: document.getElementById('city').value,
                state: document.getElementById('state').value,
                postal_code: document.getElementById('postcode').value,
                country: 'AU'
            },
            metadata: {
                dateOfBirth: document.getElementById('dob').value
            }
        };

        // Call API to create customer
        const response = await fetch('/api/customer/create', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(customerData)
        });

        const result = await response.json();

        if (!response.ok) {
            throw new Error(result.error || 'Failed to create customer');
        }

        // Store customer ID
        createdStripeCustomerId = result.customerId;

        // Display success message with customer ID
        resultDiv.innerHTML = `
            <div style="background: #d4edda; color: #155724; padding: 15px; border-radius: 8px; border-left: 4px solid #28a745;">
                <strong>✓ Customer Created Successfully!</strong><br>
                <strong>Stripe Customer ID:</strong> <code style="background: #fff; padding: 2px 6px; border-radius: 4px;">${result.customerId}</code><br>
                <strong>Name:</strong> ${result.name}<br>
                <strong>Email:</strong> ${result.email}
            </div>
        `;

        // Enable next button
        nextButton.disabled = false;

        // Hide create button
        createBtn.style.display = 'none';

    } catch (error) {
        console.error('Error creating customer:', error);
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

// Handle form submission
document.getElementById('personal-info-form').addEventListener('submit', function(e) {
    e.preventDefault();

    if (!createdStripeCustomerId) {
        alert('Please create a Stripe customer first by clicking "Create Stripe Customer" button.');
        return;
    }

    // Save form data to sessionStorage
    const formData = {
        firstName: document.getElementById('first-name').value,
        lastName: document.getElementById('last-name').value,
        email: document.getElementById('email').value,
        phone: document.getElementById('phone').value,
        dob: document.getElementById('dob').value,
        address: document.getElementById('address').value,
        city: document.getElementById('city').value,
        state: document.getElementById('state').value,
        postcode: document.getElementById('postcode').value,
        stripe_customerId: createdStripeCustomerId
    };

    sessionStorage.setItem('personalInfo', JSON.stringify(formData));

    // Navigate to next page
    window.location.href = 'CardDetails.html';
});