using Clienta.Api.Models;

namespace Clienta.Api.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string Subdomain { get; set; } = default!;
    public string? LogoUrl { get; set; }
    public string? BusinessStampUrl { get; set; }

    public PlanType Plan { get; set; } = PlanType.Trial;
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trialing;

    public DateTime? TrialEndsAt { get; set; }
    public bool IsTrial { get; set; }

    // מניעת כפילויות
    public bool TrialReminderSent { get; set; }
    public bool TrialExpiredSent { get; set; }
    public bool IsSuspended { get; set; }

    public int UserLimit { get; set; } = 10;
    public int MessageLimit { get; set; } = 100;
    public int AutoDeleteNotDocumentedAfterDays { get; set; } = 1;
    public bool EnableAutoDeleteNotDocumented { get; set; } = true;
    public decimal DefaultVatRate { get; set; } = 18m;
    public string? LegalBusinessName { get; set; }
    public string? BusinessRegistrationNumber { get; set; }
    public decimal DefaultWithholdingTaxRate { get; set; } = 0m;
    public PaymentMethod DefaultPaymentMethod { get; set; } = PaymentMethod.Cash;
    public int? DefaultInstallments { get; set; }
    public InvoiceStatus DefaultInvoiceStatus { get; set; } = InvoiceStatus.Pending;
    public string Currency { get; set; } = "ILS";
    public string InvoicePrefix { get; set; } = "INV-";
    public int NextInvoiceNumber { get; set; } = 1;
    public string QuotePrefix { get; set; } = "QT-";
    public int NextQuoteNumber { get; set; } = 1;


    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? StripePriceId { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }

    // Grace Period - don't suspend immediately on payment failure
    public DateTime? PaymentGracePeriodEndsAt { get; set; }

    // Scheduled Plan Change - applies at end of billing period (for downgrades)
    public PlanType? ScheduledPlan { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public DateTime? ScheduledPlanChangeAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public bool IsTrialExpiredEmailSent { get; set; }

    public Guid? OwnerUserId { get; set; }
    public User OwnerUser { get; set; }
}

