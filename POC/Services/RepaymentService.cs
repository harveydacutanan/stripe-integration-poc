using POC.Models;
using Stripe;

namespace POC.Services;

/// <summary>
/// Server-side half of the Apple Pay / Google Pay repayment flow.
/// Wallets are card-based, so the same PaymentIntent / PaymentMethod objects used by
/// Debit card repayments are used here; the wallet only changes how the card is presented.
/// </summary>
public class RepaymentService
{
    private const string MetadataFlowKey = "repayment_flow";
    private const string MetadataMethodKey = "repayment_method";
    private const string MetadataSurchargeKey = "surcharge_in_cents";
    private const string MetadataBaseAmountKey = "base_amount_in_cents";
    private const string MetadataFullBalanceKey = "is_full_balance_payment";
    private const string FlowValue = "repayments_wallet_poc";

    private readonly PaymentIntentService _paymentIntentService;
    private readonly ChargeService _chargeService;
    private readonly DeclineMessageResolver _declineMessageResolver;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RepaymentService> _logger;

    public RepaymentService(
        DeclineMessageResolver declineMessageResolver,
        IConfiguration configuration,
        ILogger<RepaymentService> logger)
    {
        _paymentIntentService = new PaymentIntentService();
        _chargeService = new ChargeService();
        _declineMessageResolver = declineMessageResolver;
        _configuration = configuration;
        _logger = logger;
    }

    public decimal CardSurchargeRate => _configuration.GetValue<decimal>("Repayments:CardSurchargeRate", 0.0072m);

    public string MerchantName => _configuration["Repayments:MerchantName"] ?? "MoneyMe";

    public string MerchantCountryCode => _configuration["Repayments:MerchantCountryCode"] ?? "AU";

    public string Currency => _configuration["Repayments:Currency"] ?? "aud";

    public string ProductName => _configuration["Repayments:ProductName"] ?? "Personal Loan";

    /// <summary>
    /// Builds the repayments root page summary. Balance and instalment values are
    /// stubbed for the POC - in production these come from the loan servicing system.
    /// </summary>
    public RepaymentAccountSummary BuildAccountSummary(string customerId, string customerName)
    {
        return new RepaymentAccountSummary
        {
            CustomerId = customerId,
            CustomerName = customerName,
            ProductName = ProductName,
            Currency = Currency,
            OutstandingBalanceInCents = _configuration.GetValue<long>("Repayments:OutstandingBalanceInCents", 2_000_000),
            NextInstalmentAmountInCents = _configuration.GetValue<long>("Repayments:NextInstalmentAmountInCents", 24_550),
            NextInstalmentDueDate = DateTime.UtcNow.Date.AddDays(7),
            CardSurchargeRate = CardSurchargeRate,
            QuickAmountsInCents = new List<long> { 5_000, 10_000, 24_550, 50_000 },
            BankTransfer = new BankTransferDetails
            {
                AccountName = _configuration["Repayments:BankTransfer:AccountName"] ?? "MoneyMe Financial Group",
                Bsb = _configuration["Repayments:BankTransfer:Bsb"] ?? "082-001",
                AccountNumber = _configuration["Repayments:BankTransfer:AccountNumber"] ?? "1234 5678",
                Reference = BuildCustomerReference(customerId)
            }
        };
    }

    /// <summary>
    /// Calculates the surcharge and total for the Review and finalise step.
    /// The 0.72% surcharge applies to every card-based method, which includes both wallets.
    /// </summary>
    public RepaymentQuoteResponse BuildQuote(long amountInCents, RepaymentMethod method, string currency)
    {
        var isCardBased = IsCardBasedMethod(method);
        var surchargeInCents = isCardBased
            ? (long)Math.Round(amountInCents * CardSurchargeRate, MidpointRounding.AwayFromZero)
            : 0;

        return new RepaymentQuoteResponse
        {
            AmountInCents = amountInCents,
            SurchargeInCents = surchargeInCents,
            TotalInCents = amountInCents + surchargeInCents,
            SurchargeRate = CardSurchargeRate,
            IsSurchargeApplied = isCardBased,
            IsProcessedInstantly = isCardBased,
            Currency = currency,
            SettlementNote = isCardBased
                ? "Processed instantly. Credited to your account by the next business day."
                : "Up to 2 business days to clear."
        };
    }

    /// <summary>
    /// Creates the PaymentIntent the wallet sheet confirms against.
    /// The intent is created before the sheet opens so the client always has a client secret,
    /// matching step 1 of the high-level payment sequence in the proposal.
    /// </summary>
    public async Task<RepaymentIntentResponse> CreateRepaymentIntentAsync(RepaymentIntentRequest request)
    {
        var method = ParseMethod(request.Method);

        if (!IsCardBasedMethod(method))
        {
            throw new InvalidOperationException(
                $"{method} does not create a PaymentIntent in this flow. Only card and wallet repayments do.");
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? Currency : request.Currency.ToLowerInvariant();
        var quote = BuildQuote(request.AmountInCents, method, currency);

        var options = new PaymentIntentCreateOptions
        {
            // Wallets settle as card payments, so this is a plain card intent.
            Amount = quote.TotalInCents,
            Currency = currency,
            Description = $"{ProductName} repayment",
            PaymentMethodTypes = new List<string> { "card" },
            Metadata = new Dictionary<string, string>
            {
                [MetadataFlowKey] = FlowValue,
                [MetadataMethodKey] = method.ToString(),
                [MetadataBaseAmountKey] = quote.AmountInCents.ToString(),
                [MetadataSurchargeKey] = quote.SurchargeInCents.ToString(),
                [MetadataFullBalanceKey] = request.IsFullBalancePayment.ToString(),
                ["created_at"] = DateTime.UtcNow.ToString("O")
            },
            PaymentMethodOptions = new PaymentIntentPaymentMethodOptionsOptions
            {
                Card = new PaymentIntentPaymentMethodOptionsCardOptions
                {
                    // Wallet payments carry network tokens and are usually exempt from 3DS,
                    // but it must stay available for the requires_action path.
                    RequestThreeDSecure = "automatic"
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(request.CustomerId))
        {
            options.Customer = request.CustomerId;
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentMethodId))
        {
            options.PaymentMethod = request.PaymentMethodId;
        }

        // Saving requires a customer to attach the card to; without one there is nowhere
        // for the card to live, so the request is honoured as a one-off payment instead.
        if (request.IsCardSaved && !string.IsNullOrWhiteSpace(request.CustomerId))
        {
            options.SetupFutureUsage = "off_session";
        }

        var paymentIntent = await _paymentIntentService.CreateAsync(options);

        _logger.LogInformation(
            "Created repayment PaymentIntent {PaymentIntentId} for {Method}, total {TotalInCents} {Currency}",
            paymentIntent.Id, method, quote.TotalInCents, currency.ToUpperInvariant());

        return new RepaymentIntentResponse
        {
            ClientSecret = paymentIntent.ClientSecret,
            PaymentIntentId = paymentIntent.Id,
            // The PaymentIntent ID is the reference shown on receipts, so it is the same
            // value support and finance can paste straight into the Stripe Dashboard.
            RepaymentReference = paymentIntent.Id,
            Quote = quote
        };
    }

    /// <summary>
    /// Builds the receipt / success screen data for a single repayment, including the
    /// wallet type reported by Stripe so Apple Pay and Google Pay are labelled correctly.
    /// </summary>
    public async Task<RepaymentHistoryItem> GetReceiptAsync(string paymentIntentId)
    {
        var paymentIntent = await _paymentIntentService.GetAsync(paymentIntentId);
        return await MapToHistoryItemAsync(paymentIntent);
    }

    /// <summary>
    /// Returns the repayment history, newest first, with Apple Pay and Google Pay
    /// appearing as their own entries alongside Debit card and Direct debit.
    /// Failed attempts are excluded by default: the history list only carries Cleared and
    /// Pending states, and a decline is surfaced through the error modal at the time it
    /// happens rather than as a permanent history row.
    /// </summary>
    public async Task<List<RepaymentHistoryItem>> GetHistoryAsync(
        string customerId,
        int limit,
        bool isFailedIncluded = false)
    {
        var listOptions = new PaymentIntentListOptions
        {
            Customer = customerId,
            // Fetch extra so filtering out declines still fills the requested page.
            Limit = isFailedIncluded ? limit : Math.Min(limit * 3, 100)
        };

        var paymentIntents = await _paymentIntentService.ListAsync(listOptions);
        var history = new List<RepaymentHistoryItem>();

        foreach (var paymentIntent in paymentIntents.Data)
        {
            if (!isFailedIncluded && IsFailedStatus(paymentIntent.Status))
            {
                continue;
            }

            history.Add(await MapToHistoryItemAsync(paymentIntent));

            if (history.Count == limit)
            {
                break;
            }
        }

        return history;
    }

    /// <summary>
    /// Maps a Stripe decline into the copy shown in the repayment error modal.
    /// </summary>
    public RepaymentFailure ResolveFailure(StripeError? lastPaymentError)
    {
        return _declineMessageResolver.Resolve(lastPaymentError);
    }

    public static bool IsCardBasedMethod(RepaymentMethod method)
    {
        return method == RepaymentMethod.DebitCard
            || method == RepaymentMethod.ApplePay
            || method == RepaymentMethod.GooglePay;
    }

    public static RepaymentMethod ParseMethod(string method)
    {
        if (Enum.TryParse<RepaymentMethod>(method, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Unknown repayment method '{method}'.", nameof(method));
    }

    private async Task<RepaymentHistoryItem> MapToHistoryItemAsync(PaymentIntent paymentIntent)
    {
        var item = new RepaymentHistoryItem
        {
            PaymentIntentId = paymentIntent.Id,
            AmountInCents = paymentIntent.Amount,
            Currency = paymentIntent.Currency.ToUpperInvariant(),
            Status = paymentIntent.Status,
            StatusLabel = BuildStatusLabel(paymentIntent.Status),
            CreatedUtc = paymentIntent.Created,
            PaymentTo = ProductName,
            Reference = paymentIntent.Id,
            MethodKey = nameof(RepaymentMethod.DebitCard)
        };

        if (paymentIntent.Metadata != null)
        {
            if (paymentIntent.Metadata.TryGetValue(MetadataSurchargeKey, out var surchargeText)
                && long.TryParse(surchargeText, out var surchargeInCents))
            {
                item.SurchargeInCents = surchargeInCents;
            }

            if (paymentIntent.Metadata.TryGetValue(MetadataMethodKey, out var requestedMethod)
                && !string.IsNullOrEmpty(requestedMethod))
            {
                item.MethodKey = requestedMethod;
            }
        }

        if (!string.IsNullOrEmpty(paymentIntent.LatestChargeId))
        {
            await ApplyChargeDetailsAsync(item, paymentIntent.LatestChargeId);
        }

        // The wallet Stripe actually saw wins over the method the customer picked,
        // so the history label always reflects how the payment really settled.
        item.MethodKey = ResolveMethodKey(item.WalletType, item.MethodKey);
        item.MethodLabel = BuildMethodLabel(item.MethodKey);
        item.PaymentFrom = BuildPaymentFrom(item);

        if (IsFailedStatus(paymentIntent.Status))
        {
            item.Failure = _declineMessageResolver.Resolve(paymentIntent.LastPaymentError);
        }

        return item;
    }

    private async Task ApplyChargeDetailsAsync(RepaymentHistoryItem item, string chargeId)
    {
        try
        {
            var charge = await _chargeService.GetAsync(chargeId);
            item.ReceiptUrl = charge.ReceiptUrl;

            var card = charge.PaymentMethodDetails?.Card;

            if (card == null)
            {
                return;
            }

            item.CardBrand = card.Brand;
            item.CardLast4 = card.Last4;
            item.WalletType = card.Wallet?.Type;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Could not load charge {ChargeId} for repayment receipt", chargeId);
        }
    }

    private static string ResolveMethodKey(string? walletType, string requestedMethodKey)
    {
        return walletType switch
        {
            "apple_pay" => nameof(RepaymentMethod.ApplePay),
            "google_pay" => nameof(RepaymentMethod.GooglePay),
            _ => requestedMethodKey
        };
    }

    private static string BuildMethodLabel(string methodKey)
    {
        return methodKey switch
        {
            nameof(RepaymentMethod.ApplePay) => "Apple Pay",
            nameof(RepaymentMethod.GooglePay) => "Google Pay",
            nameof(RepaymentMethod.BankDirectDebit) => "Direct debit",
            nameof(RepaymentMethod.BankTransfer) => "Bank transfer",
            _ => "Debit card"
        };
    }

    private static string BuildPaymentFrom(RepaymentHistoryItem item)
    {
        if (!string.IsNullOrEmpty(item.CardLast4))
        {
            // Apple Pay and Google Pay both show the underlying card, per the receipt spec.
            return $"Debit card ending in {item.CardLast4}";
        }

        return item.MethodKey == nameof(RepaymentMethod.BankDirectDebit)
            ? "Bank account on file"
            : "Card on file";
    }

    private static string BuildStatusLabel(string status)
    {
        return status switch
        {
            "succeeded" => "Cleared",
            "processing" => "Pending",
            "requires_action" => "Action required",
            "requires_confirmation" => "Pending",
            "requires_payment_method" => "Failed",
            "canceled" => "Cancelled",
            _ => status
        };
    }

    private static bool IsFailedStatus(string status)
    {
        return status == "requires_payment_method" || status == "canceled";
    }


    private static string BuildCustomerReference(string customerId)
    {
        var suffix = customerId.Length > 8 ? customerId[^8..] : customerId;
        return $"MM{suffix.ToUpperInvariant()}";
    }
}
