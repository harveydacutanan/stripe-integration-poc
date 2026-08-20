using POC.Models;
using Stripe;

namespace POC.Services;

/// <summary>
/// Turns a Stripe decline code into the customer-facing copy shown in the repayment
/// error modal. Raw Stripe error text is never surfaced to the customer.
/// Reference: https://docs.stripe.com/declines/codes
/// </summary>
public class DeclineMessageResolver
{
    private static readonly RepaymentFailure GenericFailure = new()
    {
        Title = "We couldn't process your payment",
        Description = "Something went wrong while processing your repayment. No money has left your account. Please try again or choose another payment method.",
        PrimaryAction = "Try again",
        SecondaryAction = "Choose another method",
        CanRetrySameMethod = true,
        RetryStep = RetrySteps.Same
    };

    private static readonly Dictionary<string, RepaymentFailure> DeclineCopyByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["insufficient_funds"] = new RepaymentFailure
        {
            Title = "Not enough funds",
            Description = "Your bank declined this payment because the account doesn't have enough available funds. Try a smaller amount, another card, or pay by bank transfer.",
            PrimaryAction = "Change amount",
            SecondaryAction = "Choose another method",
            CanRetrySameMethod = false,
            RetryStep = RetrySteps.Amount
        },
        ["card_declined"] = new RepaymentFailure
        {
            Title = "Card declined by your bank",
            Description = "Your bank declined this payment. Contact your bank for details, or use a different payment method.",
            PrimaryAction = "Try again",
            SecondaryAction = "Choose another method",
            CanRetrySameMethod = true,
            RetryStep = RetrySteps.Same
        },
        ["expired_card"] = new RepaymentFailure
        {
            Title = "Card has expired",
            Description = "The card in your wallet has expired. Update it in your device wallet, or choose another payment method.",
            PrimaryAction = "Choose another method",
            SecondaryAction = "Cancel",
            CanRetrySameMethod = false,
            RetryStep = RetrySteps.Method
        },
        ["incorrect_cvc"] = new RepaymentFailure
        {
            Title = "Security code doesn't match",
            Description = "The CVC entered doesn't match this card. Check the 3-digit code on the back of your card and try again.",
            PrimaryAction = "Try again",
            SecondaryAction = "Choose another method",
            CanRetrySameMethod = true,
            RetryStep = RetrySteps.Same
        },
        ["processing_error"] = new RepaymentFailure
        {
            Title = "Payment couldn't be completed",
            Description = "There was a problem processing your card. Please try again in a few moments.",
            PrimaryAction = "Try again",
            SecondaryAction = "Choose another method",
            CanRetrySameMethod = true,
            RetryStep = RetrySteps.Same
        },
        ["lost_card"] = new RepaymentFailure
        {
            Title = "Card declined by your bank",
            Description = "Your bank declined this payment. Contact your bank for details, or use a different payment method.",
            PrimaryAction = "Choose another method",
            SecondaryAction = "Cancel",
            CanRetrySameMethod = false,
            RetryStep = RetrySteps.Method
        },
        ["stolen_card"] = new RepaymentFailure
        {
            Title = "Card declined by your bank",
            Description = "Your bank declined this payment. Contact your bank for details, or use a different payment method.",
            PrimaryAction = "Choose another method",
            SecondaryAction = "Cancel",
            CanRetrySameMethod = false,
            RetryStep = RetrySteps.Method
        },
        ["authentication_required"] = new RepaymentFailure
        {
            Title = "Extra verification needed",
            Description = "Your bank needs to verify this payment before it can go through. Try again and complete the verification step.",
            PrimaryAction = "Try again",
            SecondaryAction = "Choose another method",
            CanRetrySameMethod = true,
            RetryStep = RetrySteps.Same
        }
    };

    /// <summary>
    /// Resolves copy for a failed PaymentIntent, preferring decline_code over the
    /// broader error code, and falling back to generic copy when neither is mapped.
    /// </summary>
    public RepaymentFailure Resolve(StripeError? lastPaymentError)
    {
        if (lastPaymentError == null)
        {
            return Clone(GenericFailure, null, null);
        }

        var declineCode = lastPaymentError.DeclineCode;
        var errorCode = lastPaymentError.Code;

        if (!string.IsNullOrEmpty(declineCode) && DeclineCopyByCode.TryGetValue(declineCode, out var byDeclineCode))
        {
            return Clone(byDeclineCode, declineCode, errorCode);
        }

        if (!string.IsNullOrEmpty(errorCode) && DeclineCopyByCode.TryGetValue(errorCode, out var byErrorCode))
        {
            return Clone(byErrorCode, declineCode, errorCode);
        }

        return Clone(GenericFailure, declineCode, errorCode);
    }

    private static RepaymentFailure Clone(RepaymentFailure source, string? declineCode, string? errorCode)
    {
        return new RepaymentFailure
        {
            Title = source.Title,
            Description = source.Description,
            PrimaryAction = source.PrimaryAction,
            SecondaryAction = source.SecondaryAction,
            CanRetrySameMethod = source.CanRetrySameMethod,
            RetryStep = source.RetryStep,
            DeclineCode = declineCode,
            ErrorCode = errorCode
        };
    }
}
