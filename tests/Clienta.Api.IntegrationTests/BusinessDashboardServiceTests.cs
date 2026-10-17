using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

// PostgreSQL-backed (the model uses Postgres-only functions, so SQLite cannot host it). Set CLIENTA_TEST_PG to the LOCAL clienta_local connection string.
[TestClass]
public class BusinessDashboardServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Ctx : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid tenantId) { TenantId = tenantId; }
        public void SetUserId(Guid userId) { UserId = userId; }
    }

    private static DbContextOptions<AppDbContext> Options()
    {
        var cs = Environment.GetEnvironmentVariable("CLIENTA_TEST_PG");
        if (string.IsNullOrWhiteSpace(cs)) Assert.Inconclusive("CLIENTA_TEST_PG is not set.");
        if (!cs!.StartsWith("Host=localhost;Port=5433;Database=clienta_local;")) Assert.Inconclusive("CLIENTA_TEST_PG must target the local clienta_local database.");
        return new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
    }

    private sealed class Seeded
    {
        public Guid TenantId;
        public Guid CompletedProjectClientId;
        public Guid[] ProjectIds = Array.Empty<Guid>();
    }

    private static DateTime D(int m, int d) => new(2026, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<Guid> NewTenant(AppDbContext db, string currency = "ILS")
    {
        var t = new Tenant { Id = Guid.NewGuid(), Name = "P8A test " + Guid.NewGuid().ToString("N")[..6], Subdomain = "p8a-" + Guid.NewGuid().ToString("N"), Currency = currency, ProductType = TenantProductType.Business };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static Client NewClient(Guid tenantId, string name) => new() { Id = Guid.NewGuid(), TenantId = tenantId, FullName = name };

    private static Project NewProject(Guid tenantId, Guid clientId, string name, ProjectStatus status, decimal value) =>
        new() { TenantId = tenantId, ClientId = clientId, Name = name, Status = status, Value = value };

    private static Invoice NewInvoice(Guid tenantId, Client client, Guid? projectId, decimal final, InvoiceStatus status) => new()
    {
        TenantId = tenantId, ClientId = client.Id, ClientName = client.FullName, ProjectId = projectId, InvoiceNumber = "T-" + Guid.NewGuid().ToString("N")[..8],
        Amount = final, Subtotal = final, TotalAmount = final, FinalAmountToPay = final, Status = status, InvoiceDate = Today
    };

    private static Payment NewPayment(Invoice invoice, decimal amount, PaymentStatus status) => new()
    {
        TenantId = invoice.TenantId, ClientId = invoice.ClientId, ProjectId = invoice.ProjectId, InvoiceId = invoice.Id, Amount = amount, PaymentDate = Today, Status = status
    };

    private static BillingMilestone NewMilestone(Guid tenantId, Guid projectId, string title, decimal amount, DateTime? due, BillingMilestoneStatus status, Guid? invoiceId = null, int sort = 0) =>
        new() { TenantId = tenantId, ProjectId = projectId, Title = title, Amount = amount, DueDate = due, Status = status, InvoiceId = invoiceId, SortOrder = sort };

    private static async Task<Seeded> SeedMainAsync(AppDbContext db)
    {
        var tenantId = await NewTenant(db, "USD");
        var c1 = NewClient(tenantId, "C1"); var c2 = NewClient(tenantId, "C2"); var c3 = NewClient(tenantId, "C3 completed");
        db.Clients.AddRange(c1, c2, c3);
        var p1 = NewProject(tenantId, c1.Id, "P1 active", ProjectStatus.Active, 10000m);
        var p2 = NewProject(tenantId, c2.Id, "P2 on hold", ProjectStatus.OnHold, 4000m);
        var p3 = NewProject(tenantId, c3.Id, "P3 completed", ProjectStatus.Completed, 2000m);
        var p4 = NewProject(tenantId, c1.Id, "P4 draft", ProjectStatus.Draft, 9000m);
        var p5 = NewProject(tenantId, c1.Id, "P5 cancelled", ProjectStatus.Cancelled, 7000m);
        db.Projects.AddRange(p1, p2, p3, p4, p5);
        await db.SaveChangesAsync();

        var a1 = NewInvoice(tenantId, c1, p1.Id, 1180m, InvoiceStatus.Pending);
        var a2 = NewInvoice(tenantId, c1, p1.Id, 1180m, InvoiceStatus.PartiallyPaid);
        var a3 = NewInvoice(tenantId, c1, p1.Id, 590m, InvoiceStatus.Paid);
        var a4 = NewInvoice(tenantId, c1, p1.Id, 2360m, InvoiceStatus.Cancelled);
        var b1 = NewInvoice(tenantId, c2, p2.Id, 1000m, InvoiceStatus.Pending);
        var cc1 = NewInvoice(tenantId, c3, p3.Id, 2000m, InvoiceStatus.Paid);
        var d1 = NewInvoice(tenantId, c1, p4.Id, 5000m, InvoiceStatus.Pending);
        var e1 = NewInvoice(tenantId, c1, p5.Id, 1000m, InvoiceStatus.Pending);
        var noProject = NewInvoice(tenantId, c1, null, 777m, InvoiceStatus.Pending);
        db.Invoices.AddRange(a1, a2, a3, a4, b1, cc1, d1, e1, noProject);
        await db.SaveChangesAsync();

        db.Payments.AddRange(
            NewPayment(a2, 500m, PaymentStatus.Verified), NewPayment(a2, 300m, PaymentStatus.PendingVerification), NewPayment(a2, 200m, PaymentStatus.Rejected),
            NewPayment(a3, 590m, PaymentStatus.Verified), NewPayment(cc1, 2000m, PaymentStatus.Verified),
            NewPayment(d1, 100m, PaymentStatus.Verified));
        db.BillingMilestones.AddRange(
            NewMilestone(tenantId, p1.Id, "M1 invoiced", 1000m, D(9, 1), BillingMilestoneStatus.Invoiced, a3.Id),
            NewMilestone(tenantId, p1.Id, "M2 planned", 3000m, D(11, 15), BillingMilestoneStatus.Planned),
            NewMilestone(tenantId, p1.Id, "M3 ready", 2000m, D(10, 20), BillingMilestoneStatus.ReadyToInvoice),
            NewMilestone(tenantId, p1.Id, "M4 cancelled", 500m, D(10, 10), BillingMilestoneStatus.Cancelled),
            NewMilestone(tenantId, p1.Id, "M5 undated", 1500m, null, BillingMilestoneStatus.Planned),
            NewMilestone(tenantId, p2.Id, "M6 planned", 800m, D(12, 1), BillingMilestoneStatus.Planned),
            NewMilestone(tenantId, p4.Id, "M7 draft project", 999m, D(10, 1), BillingMilestoneStatus.Planned),
            NewMilestone(tenantId, p3.Id, "M8 overdue", 400m, D(9, 20), BillingMilestoneStatus.Planned));
        db.ClientContacts.AddRange(
            new ClientContact { TenantId = tenantId, ClientId = c3.Id, Name = "Primary non-billing", IsPrimary = true },
            new ClientContact { TenantId = tenantId, ClientId = c3.Id, Name = "Billing person", Email = "bill@example.com", IsBillingContact = true });
        await db.SaveChangesAsync();
        return new Seeded { TenantId = tenantId, CompletedProjectClientId = c3.Id, ProjectIds = new[] { p1.Id, p2.Id, p3.Id, p4.Id, p5.Id } };
    }

    private static async Task CleanupAsync(AppDbContext db, params Guid[] tenantIds)
    {
        foreach (var id in tenantIds)
        {
            db.Payments.RemoveRange(await db.Payments.IgnoreQueryFilters().Where(x => x.TenantId == id).ToListAsync());
            db.BillingMilestones.RemoveRange(await db.BillingMilestones.IgnoreQueryFilters().Where(x => x.TenantId == id).ToListAsync());
            await db.SaveChangesAsync();
            db.Invoices.RemoveRange(await db.Invoices.IgnoreQueryFilters().Where(x => x.TenantId == id).ToListAsync());
            db.ClientContacts.RemoveRange(await db.ClientContacts.IgnoreQueryFilters().Where(x => x.TenantId == id).ToListAsync());
            await db.SaveChangesAsync();
            db.Projects.RemoveRange(await db.Projects.IgnoreQueryFilters().Where(x => x.TenantId == id).ToListAsync());
            await db.SaveChangesAsync();
            db.Clients.RemoveRange(await db.Clients.IgnoreQueryFilters().Where(x => x.TenantId == id).ToListAsync());
            db.TenantFeatures.RemoveRange(await db.TenantFeatures.IgnoreQueryFilters().Where(x => x.TenantId == id).ToListAsync());
            await db.SaveChangesAsync();
            db.Tenants.RemoveRange(await db.Tenants.Where(x => x.Id == id).ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    [TestMethod]
    public async Task Dashboard_CalculatesFinancialsIsolatesTenantsAndHandlesEmptyTenant()
    {
        var options = Options();
        await using var db = new AppDbContext(options, new Ctx());
        Seeded? main = null; Guid otherId = Guid.Empty, emptyId = Guid.Empty;
        try
        {
            main = await SeedMainAsync(db);

            // Tenant B: its numbers must never appear in tenant A's dashboard.
            otherId = await NewTenant(db);
            var ob = NewClient(otherId, "Other client"); db.Clients.Add(ob);
            var op = NewProject(otherId, ob.Id, "Other project", ProjectStatus.Active, 777m); db.Projects.Add(op);
            await db.SaveChangesAsync();
            var oi = NewInvoice(otherId, ob, op.Id, 123m, InvoiceStatus.PartiallyPaid); db.Invoices.Add(oi);
            await db.SaveChangesAsync();
            db.Payments.Add(NewPayment(oi, 23m, PaymentStatus.Verified));
            db.BillingMilestones.Add(NewMilestone(otherId, op.Id, "Other milestone", 55m, D(10, 6), BillingMilestoneStatus.Planned));
            await db.SaveChangesAsync();

            emptyId = await NewTenant(db);

            var service = new BusinessDashboardService(db);

            var a = await service.GetAsync(main.TenantId, true, Today);
            Assert.AreEqual("USD", a.Currency);
            Assert.AreEqual(3, a.ProjectCount);                 // Active + OnHold + Completed only
            Assert.AreEqual(16000m, a.ProjectValue);            // draft/cancelled excluded
            Assert.AreEqual(5950m, a.Invoiced);                 // cancelled, draft/cancelled-project and project-less invoices excluded
            Assert.AreEqual(3090m, a.Paid);                     // 500 + 590 + 2000; pending/rejected and cancelled-invoice payments excluded
            Assert.AreEqual(2860m, a.Outstanding);              // partial 680 + unpaid 1180 + unpaid 1000
            Assert.AreEqual(7700m, a.NotYetInvoiced);           // 3000 + 2000 + 1500 + 800 + 400; invoiced/cancelled/draft-project excluded
            Assert.IsNotNull(a.NextBilling);
            Assert.AreEqual("M8 overdue", a.NextBilling!.MilestoneTitle);   // earliest dated open milestone, overdue included
            Assert.IsTrue(a.NextBilling.IsOverdue);
            Assert.AreEqual(400m, a.NextBilling.Amount);
            Assert.AreEqual(main.CompletedProjectClientId, a.NextBilling.ClientId);
            Assert.AreEqual("Billing person", a.NextBilling.BillingContact!.Name);

            // Full payment: the paid-in-full invoices leave no outstanding balance.
            var onlyPaid = a.Invoiced - a.Outstanding;
            Assert.AreEqual(a.Paid, onlyPaid);

            // Without invoice access the invoice-derived totals are not computed.
            var restricted = await service.GetAsync(main.TenantId, false, Today);
            Assert.IsFalse(restricted.InvoiceFinancialsAvailable);
            Assert.IsNull(restricted.Invoiced); Assert.IsNull(restricted.Paid); Assert.IsNull(restricted.Outstanding);
            Assert.AreEqual(16000m, restricted.ProjectValue);

            // Tenant isolation: B sees only its own data, A saw none of it.
            var b = await service.GetAsync(otherId, true, Today);
            Assert.AreEqual(1, b.ProjectCount);
            Assert.AreEqual(777m, b.ProjectValue);
            Assert.AreEqual(123m, b.Invoiced); Assert.AreEqual(23m, b.Paid); Assert.AreEqual(100m, b.Outstanding);
            Assert.AreEqual(55m, b.NotYetInvoiced);
            Assert.AreEqual("Other milestone", b.NextBilling!.MilestoneTitle);
            Assert.IsFalse(b.NextBilling.IsOverdue);
            Assert.IsNull(b.NextBilling.BillingContact);

            // Empty tenant: zeros, no next billing, no exception.
            var e = await service.GetAsync(emptyId, true, Today);
            Assert.AreEqual(0, e.ProjectCount);
            Assert.AreEqual(0m, e.ProjectValue); Assert.AreEqual(0m, e.Invoiced); Assert.AreEqual(0m, e.Paid);
            Assert.AreEqual(0m, e.Outstanding); Assert.AreEqual(0m, e.NotYetInvoiced);
            Assert.IsNull(e.NextBilling);

            // Unknown tenant id behaves like an empty tenant.
            var unknown = await service.GetAsync(Guid.NewGuid(), true, Today);
            Assert.AreEqual(0m, unknown.ProjectValue); Assert.IsNull(unknown.NextBilling);
        }
        finally
        {
            await using var cleanup = new AppDbContext(options, new Ctx());
            await CleanupAsync(cleanup, new[] { main?.TenantId ?? Guid.Empty, otherId, emptyId }.Where(x => x != Guid.Empty).ToArray());
        }
    }
}
