// Welcome dashboard functionality
document.addEventListener('DOMContentLoaded', function() {
    // Check authentication
    checkAuthentication();

    // Load customer information
    loadCustomerInfo();

    // Setup event listeners
    document.getElementById('logoutBtn').addEventListener('click', logout);
});

function checkAuthentication() {
    const isAuthenticated = sessionStorage.getItem('eca_authenticated');

    if (!isAuthenticated || isAuthenticated !== 'true') {
        // Redirect to login if not authenticated
        window.location.href = 'Login.html';
    }
}

function loadCustomerInfo() {
    const customerId = sessionStorage.getItem('eca_customerId');
    const email = sessionStorage.getItem('eca_email');

    if (customerId && email) {
        document.getElementById('userEmail').textContent = email;
        document.getElementById('displayCustomerId').textContent = customerId;
        document.getElementById('displayEmail').textContent = email;
    }
}

function navigateToPayment() {
    window.location.href = 'Payment.html';
}

function logout() {
    // Clear session storage
    sessionStorage.removeItem('eca_customerId');
    sessionStorage.removeItem('eca_email');
    sessionStorage.removeItem('eca_authenticated');

    // Redirect to login
    window.location.href = 'Login.html';
}
