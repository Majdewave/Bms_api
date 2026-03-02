using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public class TenantSeedService : ITenantSeedService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly Random _random = new Random();

    public TenantSeedService(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task SeedAdvancedAsync(Guid tenantId, Guid adminUserId)
    {
        // Temporarily set TenantContext for this operation
        _tenantContext.SetTenant(tenantId);
        _tenantContext.SetUserId(adminUserId);

        // Check if demo data already exists for this tenant
        if (await _db.Clients.AnyAsync())
            return; // Skip seeding if data already exists

        var now = DateTime.UtcNow;
        var clientIds = new List<Guid>();

        // ===== CREATE 10 DEMO CLIENTS =====
        var demoClients = new List<Client>
        {
            CreateClient(tenantId, "דוד כהן", "david.cohen@example.com", "+972-50-1234567", now.AddDays(-25)),
            CreateClient(tenantId, "רחל לוי", "rachel.levi@example.com", "+972-52-2345678", now.AddDays(-22)),
            CreateClient(tenantId, "אברהם גולדמן", "abraham.goldman@example.com", "+972-54-3456789", now.AddDays(-20)),
            CreateClient(tenantId, "שרה מנדלסון", "sarah.mendelson@example.com", "+972-50-4567890", now.AddDays(-18)),
            CreateClient(tenantId, "יוסף ברקוביץ", "joseph.bar@example.com", "+972-52-5678901", now.AddDays(-15)),
            CreateClient(tenantId, "מרים כספי", "miriam.kaspi@example.com", "+972-54-6789012", now.AddDays(-12)),
            CreateClient(tenantId, "משה רסקין", "moshe.raskin@example.com", "+972-50-7890123", now.AddDays(-10)),
            CreateClient(tenantId, "נעמי אלוני", "naomi.aloni@example.com", "+972-52-8901234", now.AddDays(-7)),
            CreateClient(tenantId, "בנימין דרור", "benjamin.dror@example.com", "+972-54-9012345", now.AddDays(-5)),
            CreateClient(tenantId, "אסתר שחור", "esther.shahor@example.com", "+972-50-0123456", now.AddDays(-2))
        };

        _db.Clients.AddRange(demoClients);
        await _db.SaveChangesAsync();

        clientIds = demoClients.Select(c => c.Id).ToList();

        // ===== CREATE 15 APPOINTMENTS =====
        var demoAppointments = new List<Appointment>();

        // 5 Past Appointments (Completed)
        for (int i = 0; i < 5; i++)
        {
            var startTime = now.AddDays(-(20 - i * 4)).Date.AddHours(9 + (i % 4));
            demoAppointments.Add(new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ClientId = clientIds[i],
                CreatedByUserId = adminUserId,
                StartTime = startTime,
                EndTime = startTime.AddHours(1),
                Status = "Completed",
                Notes = GetRandomAppointmentNotes()[0],
                CreatedAt = startTime.AddHours(-1)
            });
        }

        // 5 This Week/Today (Scheduled)
        for (int i = 0; i < 5; i++)
        {
            var startTime = now.AddDays(i).Date.AddHours(10 + (i % 3));
            demoAppointments.Add(new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ClientId = clientIds[5 + i],
                CreatedByUserId = adminUserId,
                StartTime = startTime,
                EndTime = startTime.AddHours(1),
                Status = "Scheduled",
                Notes = GetRandomAppointmentNotes()[1],
                CreatedAt = now.AddHours(-2)
            });
        }

        // 5 Future (Scheduled)
        for (int i = 0; i < 5; i++)
        {
            var startTime = now.AddDays(7 + (i * 2)).Date.AddHours(14 + (i % 2));
            demoAppointments.Add(new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ClientId = clientIds[i % 10],
                CreatedByUserId = adminUserId,
                StartTime = startTime,
                EndTime = startTime.AddHours(1),
                Status = "Scheduled",
                Notes = GetRandomAppointmentNotes()[2],
                CreatedAt = now.AddHours(-6)
            });
        }

        _db.Appointments.AddRange(demoAppointments);
        await _db.SaveChangesAsync();

        // ===== CREATE NOTES (1-2 PER CLIENT) =====
        var demoNotes = new List<Note>();

        for (int i = 0; i < clientIds.Count; i++)
        {
            // First note for all clients
            demoNotes.Add(new Note
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ClientId = clientIds[i],
                CreatedByUserId = adminUserId,
                Content = GetRandomClientNotes()[0],
                CreatedAt = now.AddDays(-(25 - i * 2))
            });

            // Second note for 50% of clients
            if (i % 2 == 0)
            {
                demoNotes.Add(new Note
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ClientId = clientIds[i],
                    CreatedByUserId = adminUserId,
                    Content = GetRandomClientNotes()[1],
                    CreatedAt = now.AddDays(-(20 - i * 2))
                });
            }
        }

        _db.Notes.AddRange(demoNotes);
        await _db.SaveChangesAsync();
    }

    private Client CreateClient(Guid tenantId, string fullName, string email, string phone, DateTime createdAt)
    {
        return new Client
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = fullName,
            Email = email,
            Phone = phone,
            IsActive = true,
            CreatedAt = createdAt
        };
    }

    private string[] GetRandomAppointmentNotes()
    {
        return new[]
        {
            "ייעוץ ראשוני",
            "פגישת משך",
            "דיון על דרישות",
            "תחקור ועדכון",
            "סיכום פרויקט"
        };
    }

    private string[] GetRandomClientNotes()
    {
        return new[]
        {
            "לקוח חוזר, מעדיף שעות בוקר",
            "ביקש הצעת מחיר",
            "נדרש מעקב במהלך השבוע",
            "לקוח VIP, עדיפות גבוהה",
            "פתרון בהצלחה בעבר",
            "דורש עדכון על התקדמות",
            "אישור חוזה בעתיד הקרוב",
            "הערות מיוחדות: הזמנות ממוקדות",
            "נדרש תמיכה טכנית",
            "לקוח משוקלל, השקעה בטוחה"
        };
    }
}
