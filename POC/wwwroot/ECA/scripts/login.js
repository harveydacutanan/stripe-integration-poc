// Login functionality for ECA
document.addEventListener('DOMContentLoaded', function() {
    const loginForm = document.getElementById('loginForm');
    const errorMessage = document.getElementById('errorMessage');

    loginForm.addEventListener('submit', async function(e) {
        e.preventDefault();

        const customerId = document.getElementById('customerId').value.trim();
        const email = document.getElementById('email').value.trim();

        // Validate customer ID format
        if (!customerId.startsWith('cus_')) {
            showError('Customer ID must start with "cus_"');
            return;
        }

        // Basic email validation
        if (!isValidEmail(email)) {
            showError('Please enter a valid email address');
            return;
        }

        // Store credentials in sessionStorage (demo purposes only)
        // In production, implement proper authentication
        sessionStorage.setItem('eca_customerId', customerId);
        sessionStorage.setItem('eca_email', email);
        sessionStorage.setItem('eca_authenticated', 'true');

        // Redirect to welcome page
        window.location.href = 'Welcome.html';
    });

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
