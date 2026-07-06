using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public sealed class DepartmentAccessContext
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public bool IsOwner { get; init; }
    public List<Guid> DepartmentIds { get; init; } = new();
    public bool HasDepartmentFilter => !IsOwner && DepartmentIds.Count > 0;
}

public interface IDepartmentAccessService
{
    Task<DepartmentAccessContext> GetCurrentUserAccessContextAsync();
    IQueryable<Appointment> ApplyAppointmentVisibility(IQueryable<Appointment> query, DepartmentAccessContext accessContext);
    IQueryable<VisitSummary> ApplyVisitSummaryVisibility(IQueryable<VisitSummary> query, DepartmentAccessContext accessContext);
}

public class DepartmentAccessService : IDepartmentAccessService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public DepartmentAccessService(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<DepartmentAccessContext> GetCurrentUserAccessContextAsync()
    {
        var tenantId = _tenantContext.TenantId;
        var userId = _tenantContext.UserId ?? Guid.Empty;

        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            return new DepartmentAccessContext
            {
                TenantId = tenantId,
                UserId = userId,
                IsOwner = false,
                DepartmentIds = new List<Guid>()
            };
        }

        var isOwner = await _db.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Id == tenantId && t.OwnerUserId == userId);

        if (isOwner)
        {
            return new DepartmentAccessContext
            {
                TenantId = tenantId,
                UserId = userId,
                IsOwner = true,
                DepartmentIds = new List<Guid>()
            };
        }

        var departmentIds = await _db.StaffDepartments
            .AsNoTracking()
            .Where(sd => sd.TenantId == tenantId && sd.Staff.UserId == userId)
            .Select(sd => sd.DepartmentId)
            .Distinct()
            .ToListAsync();

        return new DepartmentAccessContext
        {
            TenantId = tenantId,
            UserId = userId,
            IsOwner = false,
            DepartmentIds = departmentIds
        };
    }

    public IQueryable<Appointment> ApplyAppointmentVisibility(IQueryable<Appointment> query, DepartmentAccessContext accessContext)
    {
        if (!accessContext.HasDepartmentFilter)
            return query;

        return query.Where(a =>
            !a.DepartmentId.HasValue || accessContext.DepartmentIds.Contains(a.DepartmentId.Value));
    }

    public IQueryable<VisitSummary> ApplyVisitSummaryVisibility(IQueryable<VisitSummary> query, DepartmentAccessContext accessContext)
    {
        if (!accessContext.HasDepartmentFilter)
            return query;

        // Department ownership is derived from Appointment.DepartmentId.
        // Legacy summaries (no appointment or appointment with no department) remain visible.
        return query.Where(v =>
            !v.AppointmentId.HasValue ||
            !_db.Appointments.Any(a =>
                a.TenantId == accessContext.TenantId &&
                a.Id == v.AppointmentId.Value) ||
            _db.Appointments.Any(a =>
                a.TenantId == accessContext.TenantId &&
                a.Id == v.AppointmentId.Value &&
                (!a.DepartmentId.HasValue || accessContext.DepartmentIds.Contains(a.DepartmentId.Value)))
        );
    }
}