namespace POC.Models;

/// <summary>
/// Payment methods available on the repayment "Payment method" step.
/// Mirrors the method list in the Apple Pay / Google Pay proposal (UD-3058040988).
/// </summary>
public enum RepaymentMethod
{
    DebitCard,
    ApplePay,
    GooglePay,
    BankDirectDebit,
    BankTransfer
}

/// <summary>
/// Account summary shown on the repayments root page.
/// </summary>
public class RepaymentAccountSummary
{
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ProductName { get; set; } = "Personal Loan";
    public string Currency { get; set; } = "aud";
    public long OutstandingBalanceInCents { get; set; }
    public long NextInstalmentAmountInCents { get; set; }
    public DateTime NextInstalmentDueDate { get; set; }
    public decimal CardSurchargeRate { get; set; }
    public List<long> QuickAmountsInCents { get; set; } = new();
    public BankTransferDetails BankTransfer { get; set; } = new();
}

/// <summary>
/// Static MoneyMe bank details plus the customer's unique payment reference,
/// shown in the "How to pay via bank transfer" modal.
/// </summary>
public class BankTransferDetails
{
    public string AccountName { get; set; } = string.Empty;
    public string Bsb { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

/// <summary>
/// Request for a surcharge quote on the "Review and finalise" step.
/// </summary>
public class RepaymentQuoteRequest
{
    public long AmountInCents { get; set; }
    public string Method { get; set; } = nameof(RepaymentMethod.DebitCard);
    public string Currency { get; set; } = "aud";
}

/// <summary>
/// Amount breakdown shown on "Review and finalise".
/// </summary>
public class RepaymentQuoteResponse
{
    public long AmountInCents { get; set; }
    public long SurchargeInCents { get; set; }
    public long TotalInCents { get; set; }
    public decimal SurchargeRate { get; set; }
    public bool IsSurchargeApplied { get; set; }
    public bool IsProcessedInstantly { get; set; }
    public string Currency { get; set; } = "aud";
    public string SettlementNote { get; set; } = string.Empty;
}

/// <summary>
/// Request to create the PaymentIntent behind a wallet or card repayment.
/// </summary>
public class RepaymentIntentRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public long AmountInCents { get; set; }
    public string Method { get; set; } = nameof(RepaymentMethod.ApplePay);
    public string Currency { get; set; } = "aud";

    /// <summary>
    /// Set when paying with a card the customer has already saved, so the card number
    /// does not have to be entered again.
    /// </summary>
    public string? PaymentMethodId { get; set; }

    /// <summary>
    /// Set when the customer ticked "save this card", which attaches the card to the
    /// Stripe customer so it can be reused for future repayments.
    /// </summary>
    public bool IsCardSaved { get; set; }

    public bool IsFullBalancePayment { get; set; }
}

/// <summary>
/// Client secret plus the confirmed amount breakdown the sheet will display.
/// </summary>
public class RepaymentIntentResponse
{
    public string ClientSecret { get; set; } = string.Empty;
    public string PaymentIntentId { get; set; } = string.Empty;
    public string RepaymentReference { get; set; } = string.Empty;
    public RepaymentQuoteResponse Quote { get; set; } = new();
}

/// <summary>
/// A single row in the "Repayment history" list, and the source of the receipt screen.
/// </summary>
public class RepaymentHistoryItem
{
    public string PaymentIntentId { get; set; } = string.Empty;
    public string MethodLabel { get; set; } = string.Empty;
    public string MethodKey { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long AmountInCents { get; set; }
    public long SurchargeInCents { get; set; }
    public string Currency { get; set; } = "aud";
    public DateTime CreatedUtc { get; set; }
    public string PaymentTo { get; set; } = string.Empty;
    public string PaymentFrom { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? WalletType { get; set; }
    public string? ReceiptUrl { get; set; }
    public RepaymentFailure? Failure { get; set; }
}

/// <summary>
/// Customer-facing decline copy derived from PaymentIntent.last_payment_error.
/// </summary>
public class RepaymentFailure
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PrimaryAction { get; set; } = "Try again";
    public string SecondaryAction { get; set; } = "Choose another method";
    public bool CanRetrySameMethod { get; set; } = true;

    /// <summary>
    /// Where the primary action should take the customer: "same" to retry on the review
    /// screen, "method" to pick a different payment method, or "amount" to change how much
    /// they are paying. Keeps the button label and what it actually does in step.
    /// </summary>
    public string RetryStep { get; set; } = RetrySteps.Same;

    public string? DeclineCode { get; set; }
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Allowed values for <see cref="RepaymentFailure.RetryStep"/>.
/// </summary>
public static class RetrySteps
{
    public const string Same = "same";
    public const string Method = "method";
    public const string Amount = "amount";
}

/// <summary>
/// Front-end bootstrap values for the repayments flow.
/// </summary>
public class RepaymentConfigResponse
{
    public string PublishableKey { get; set; } = string.Empty;
    public string Environment { get; set; } = "test";
    public string MerchantName { get; set; } = string.Empty;
    public string MerchantCountryCode { get; set; } = "AU";
    public string Currency { get; set; } = "aud";
    public decimal CardSurchargeRate { get; set; }
    public bool IsApplePayDomainFileConfigured { get; set; }
}
