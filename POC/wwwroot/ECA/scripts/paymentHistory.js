// Payment History for existing customers
document.addEventListener('DOMContentLoaded', async function() {
    // Check authentication
    checkAuthentication();

    // Load customer information
    loadCustomerInfo();

    // Load payment history
    await loadPaymentHistory();
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

async function loadPaymentHistory() {
    const customerId = sessionStorage.getItem('eca_customerId');
    const loadingSection = document.getElementById('loading');
    const errorMessage = document.getElementById('error-message');
    const paymentList = document.getElementById('payment-list');

    try {
        const response = await fetch(`/api/payment/payment-history/${customerId}?limit=50`);

        if (!response.ok) {
            throw new Error('Failed to load payment history');
        }

        const payments = await response.json();

        // Hide loading
        loadingSection.style.display = 'none';

        // Show payment list
        paymentList.style.display = 'block';

        if (payments.length === 0) {
            document.getElementById('no-payments').style.display = 'block';
        } else {
            displayPayments(payments);
        }

    } catch (error) {
        console.error('Error loading payment history:', error);
        loadingSection.style.display = 'none';
        errorMessage.textContent = 'Failed to load payment history. Please try again later.';
        errorMessage.style.display = 'block';
    }
}

function displayPayments(payments) {
    const container = document.getElementById('payments-container');
    container.innerHTML = '';

    payments.forEach(payment => {
        const paymentItem = createPaymentItem(payment);
        container.appendChild(paymentItem);
    });
}

function createPaymentItem(payment) {
    const item = document.createElement('div');
    item.className = 'payment-item';

    const amount = (payment.amount / 100).toFixed(2);
    const date = new Date(payment.created * 1000).toLocaleString('en-AU', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });

    const statusClass = `status-${payment.status}`;
    const statusText = payment.status.replace('_', ' ');

    const cardInfo = payment.brand && payment.last4
        ? `${payment.brand.toUpperCase()} •••• ${payment.last4}`
        : 'Card on file';

    item.innerHTML = `
        <div class="payment-main">
            <div class="payment-header">
                <span class="payment-amount">$${amount} ${payment.currency}</span>
                <span class="payment-status ${statusClass}">${statusText}</span>
            </div>
            <div class="payment-details">
                <div class="payment-detail">
                    <strong>Date:</strong> ${date}
                </div>
                ${payment.description ? `
                    <div class="payment-detail">
                        <strong>Description:</strong> ${payment.description}
                    </div>
                ` : ''}
                <div class="payment-detail">
                    <strong>Payment Method:</strong> ${cardInfo}
                </div>
                <div class="payment-id">${payment.id}</div>
            </div>
        </div>
        <div class="payment-actions">
            ${payment.receiptUrl ? `
                <button class="btn-receipt" onclick="viewReceipt('${payment.receiptUrl}')">
                    View Receipt
                </button>
            ` : `
                <button class="btn-receipt" disabled>
                    No Receipt
                </button>
            `}
        </div>
    `;

    return item;
}

function viewReceipt(url) {
    window.open(url, '_blank');
}

function makePayment() {
    window.location.href = 'Payment.html';
}

function goBack() {
    window.location.href = 'Welcome.html';
}
