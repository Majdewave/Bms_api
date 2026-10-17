using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Authorize(Policy = "manage_projects")]
public class BillingMilestonesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPmAccessService _pmAccessService;

    public BillingMilestonesController(AppDbContext db, ITenantContext tenantContext, IPmAccessService pmAccessService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _pmAccessService = pmAccessService;
    }

    [HttpGet("/api/projects/{projectId:guid}/billing-milestones")]
    public async Task<IActionResult> GetByProject(Guid projectId)
    {
        var project = await GetProjectAsync(projectId);
        if (project == null) return NotFound();
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        var milestones = await _db.BillingMilestones
            .AsNoTracking()
            .Include(milestone => milestone.Invoice)
            .Where(milestone => milestone.ProjectId == projectId && milestone.TenantId == _tenantContext.TenantId)
            .OrderBy(milestone => milestone.SortOrder)
            .ThenBy(milestone => milestone.DueDate)
            .ThenBy(milestone => milestone.CreatedAt)
            .Select(ToResponseExpression)
            .ToListAsync();

        return Ok(milestones);
    }

    [HttpGet("/api/billing-milestones/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var milestone = await _db.BillingMilestones
            .AsNoTracking()
            .Include(item => item.Invoice)
            .FirstOrDefaultAsync(item => item.Id == id && item.TenantId == _tenantContext.TenantId);
        if (milestone == null) return NotFound();

        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;
        return Ok(ToResponse(milestone));
    }

    [HttpPost("/api/projects/{projectId:guid}/billing-milestones")]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] CreateBillingMilestoneRequest request)
    {
        var project = await GetProjectAsync(projectId);
        if (project == null) return NotFound();
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;
        if (!TryValidateRequest(request.Title, request.Amount, request.Status, out var error)) return BadRequest(error);

        var milestone = new BillingMilestone
        {
            TenantId = _tenantContext.TenantId,
            ProjectId = project.Id,
            Title = request.Title.Trim(),
            Description = Normalize(request.Description),
            Amount = request.Amount,
            DueDate = NormalizeToUtc(request.DueDate),
            Status = request.Status,
            SortOrder = request.SortOrder,
        };

        _db.BillingMilestones.Add(milestone);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = milestone.Id }, ToResponse(milestone));
    }

    [HttpPut("/api/billing-milestones/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBillingMilestoneRequest request)
    {
        var milestone = await _db.BillingMilestones
            .Include(item => item.Invoice)
            .FirstOrDefaultAsync(item => item.Id == id && item.TenantId == _tenantContext.TenantId);
        if (milestone == null) return NotFound();
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;
        if (!TryValidateRequest(request.Title, request.Amount, request.Status, out var error)) return BadRequest(error);

        if (milestone.InvoiceId.HasValue && request.Status != BillingMilestoneStatus.Invoiced)
            return BadRequest("A linked invoice must remain Invoiced. Unlink the invoice before changing this status.");
        if (request.Status == BillingMilestoneStatus.Invoiced && !milestone.InvoiceId.HasValue)
            return BadRequest("An Invoiced milestone must have a linked invoice.");

        milestone.Title = request.Title.Trim();
        milestone.Description = Normalize(request.Description);
        milestone.Amount = request.Amount;
        milestone.DueDate = NormalizeToUtc(request.DueDate);
        milestone.Status = request.Status;
        milestone.SortOrder = request.SortOrder;
        milestone.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToResponse(milestone));
    }

    [HttpPost("/api/billing-milestones/{id:guid}/link-invoice")]
    public async Task<IActionResult> LinkInvoice(Guid id, [FromBody] LinkBillingMilestoneInvoiceRequest request)
    {
        var milestone = await _db.BillingMilestones
            .Include(item => item.Project)
            .FirstOrDefaultAsync(item => item.Id == id && item.TenantId == _tenantContext.TenantId);
        if (milestone == null) return NotFound();
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;
        if (milestone.Status == BillingMilestoneStatus.Cancelled) return BadRequest("A cancelled milestone cannot be invoiced.");
        if (milestone.InvoiceId.HasValue) return Conflict("This milestone already has an invoice.");

        var invoice = await _db.Invoices.FirstOrDefaultAsync(item => item.Id == request.InvoiceId && item.TenantId == _tenantContext.TenantId);
        if (invoice == null) return BadRequest("Invoice not found for the current tenant.");
        if (invoice.ClientId != milestone.Project.ClientId || invoice.ProjectId != milestone.ProjectId)
            return BadRequest("Invoice must belong to the same project and client.");

        var alreadyLinked = await _db.BillingMilestones.AnyAsync(item =>
            item.Id != milestone.Id && item.InvoiceId == invoice.Id && item.Status != BillingMilestoneStatus.Cancelled);
        if (alreadyLinked) return Conflict("This invoice is already linked to another active milestone.");

        milestone.InvoiceId = invoice.Id;
        milestone.Status = BillingMilestoneStatus.Invoiced;
        milestone.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToResponse(milestone, invoice.InvoiceNumber));
    }

    [HttpDelete("/api/billing-milestones/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var milestone = await _db.BillingMilestones.FirstOrDefaultAsync(item => item.Id == id && item.TenantId == _tenantContext.TenantId);
        if (milestone == null) return NotFound();
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;
        if (milestone.InvoiceId.HasValue) return Conflict("An invoiced milestone cannot be deleted.");

        _db.BillingMilestones.Remove(milestone);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Project?> GetProjectAsync(Guid projectId) =>
        _tenantContext.TenantId == Guid.Empty
            ? null
            : await _db.Projects.FirstOrDefaultAsync(project => project.Id == projectId && project.TenantId == _tenantContext.TenantId);

    private async Task<IActionResult?> EnsurePmEnabledAsync() =>
        _tenantContext.TenantId == Guid.Empty
            ? Unauthorized("Tenant not resolved")
            : await _pmAccessService.IsPmEnabledAsync(_tenantContext.TenantId)
                ? null
                : StatusCode(StatusCodes.Status403Forbidden, "PM feature is not enabled for this tenant.");

    private static bool TryValidateRequest(string? title, decimal amount, BillingMilestoneStatus status, out string? error)
    {
        if (string.IsNullOrWhiteSpace(title)) { error = "Title is required."; return false; }
        if (amount < 0) { error = "Amount cannot be negative."; return false; }
        if (status == BillingMilestoneStatus.Invoiced) { error = "An Invoiced milestone must be linked to an invoice."; return false; }
        error = null;
        return true;
    }

    private static DateTime? NormalizeToUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BillingMilestoneResponse ToResponse(BillingMilestone milestone, string? invoiceNumber = null) => new(
        milestone.Id, milestone.ProjectId, milestone.Title, milestone.Description, milestone.Amount,
        milestone.DueDate, milestone.Status, milestone.InvoiceId, invoiceNumber ?? milestone.Invoice?.InvoiceNumber,
        milestone.CreatedAt, milestone.UpdatedAt, milestone.SortOrder);

    private static readonly System.Linq.Expressions.Expression<Func<BillingMilestone, BillingMilestoneResponse>> ToResponseExpression = milestone =>
        new BillingMilestoneResponse(milestone.Id, milestone.ProjectId, milestone.Title, milestone.Description, milestone.Amount,
            milestone.DueDate, milestone.Status, milestone.InvoiceId, milestone.Invoice == null ? null : milestone.Invoice.InvoiceNumber,
            milestone.CreatedAt, milestone.UpdatedAt, milestone.SortOrder);
}

public sealed record LinkBillingMilestoneInvoiceRequest(Guid InvoiceId);
