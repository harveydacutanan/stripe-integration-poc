// Login functionality for ECA
document.addEventListener('DOMContentLoaded', function() {
    const loginForm = document.getElementById('loginForm');
    const errorMessage = document.getElementById('errorMessage');

    loginForm.addEventListener('submit', function(e) {
        e.preventDefault();

        if (readCredentials()) {
            window.location.href = 'Welcome.html';
        }
    });

    // Prefill if a customer is already in the session.
    if (sessionStorage.getItem('eca_authenticated') === 'true' && sessionStorage.getItem('eca_customerId')) {
        document.getElementById('customerId').value = sessionStorage.getItem('eca_customerId');
        document.getElementById('email').value = sessionStorage.getItem('eca_email') || '';
    }

    /**
     * Validates the form and stores the demo session.
     * Returns the customer ID, or null when validation failed.
     */
    function readCredentials() {
        const customerId = document.getElementById('customerId').value.trim();
        const email = document.getElementById('email').value.trim();

        // Validate customer ID format
        if (!customerId.startsWith('cus_')) {
            showError('Customer ID must start with "cus_"');
            return null;
        }

        // Basic email validation
        if (!isValidEmail(email)) {
            showError('Please enter a valid email address');
            return null;
        }

        // Store credentials in sessionStorage (demo purposes only)
        // In production, implement proper authentication
        sessionStorage.setItem('eca_customerId', customerId);
        sessionStorage.setItem('eca_email', email);
        sessionStorage.setItem('eca_authenticated', 'true');

        return customerId;
    }

    // POC convenience: spin up a fresh Stripe test customer so the demo can be run
    // without hunting for an existing customer ID in the Stripe Dashboard.
    document.getElementById('createCustomerButton').addEventListener('click', async function() {
        const button = this;
        const result = document.getElementById('createCustomerResult');

        button.disabled = true;
        button.textContent = 'Creating customer...';
        result.style.display = 'none';

        try {
            const customer = await createTestCustomer();

            document.getElementById('customerId').value = customer.customerId;
            document.getElementById('email').value = customer.email;

            result.innerHTML = 'Created <code>' + customer.customerId + '</code><br>'
                + 'Email: <code>' + customer.email + '</code><br>'
                + 'The form is filled in - press Sign in to continue.';
            result.style.display = 'block';
        } catch (error) {
            showError(error.message || 'Could not create a test customer.');
        } finally {
            button.disabled = false;
            button.textContent = 'Create a new test customer';
        }
    });

    async function createTestCustomer() {
        // The API deduplicates on email, so the timestamp keeps every click distinct.
        const email = `poc.customer.${Date.now()}@example.com`;

        const response = await fetch('/api/customer/create', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                name: 'POC Test Customer',
                email: email,
                phone: '+61400000000'
            })
        });

        if (!response.ok) {
            let message = `Could not create a test customer (${response.status})`;

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

    function showError(message) {
        errorMessage.textContent = message;
        errorMessage.style.display = 'block';

        setTimeout(() => {
            errorMessage.style.display = 'none';
        }, 5000);
    }

    function isValidEmail(email) {
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        return emailRegex.test(email);
    }
});
