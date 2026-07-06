using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/drugs")]
[Authorize]
public class DrugsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IUserDepartmentFeatureAccessService _userDepartmentFeatureAccessService;

    public DrugsController(
        AppDbContext context,
        ITenantContext tenant,
        IUserDepartmentFeatureAccessService userDepartmentFeatureAccessService)
    {
        _context = context;
        _tenant = tenant;
        _userDepartmentFeatureAccessService = userDepartmentFeatureAccessService;
    }

    // GET /api/drugs/search
    [HttpGet("search")]
    public async Task<IActionResult> Search(string q)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("drugsEnabled"))
            return Forbid();

        if (string.IsNullOrWhiteSpace(q))
            return Ok(new List<object>());

        var drugs = await _context.Drugs
            .Where(d => EF.Functions.ILike(d.Name, $"%{q}%"))
            .OrderBy(d => d.Name)
            .Take(20)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Dosage
            })
            .ToListAsync();

        return Ok(drugs);
    }

    // GET /api/drugs
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("drugsEnabled"))
            return Forbid();

        var drugs = await _context.Drugs
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Dosage,
                d.CreatedAt
            })
            .ToListAsync();

        return Ok(drugs);
    }

    // POST /api/drugs
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDrugRequest request)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("drugsEnabled"))
            return Forbid();

        // Set TenantId from context
        var tenantId = _tenant.TenantId;
        var drug = new Drug
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Dosage = request.Dosage,
            CreatedAt = DateTime.UtcNow,
            TenantId = tenantId
        };

        _context.Drugs.Add(drug);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { id = drug.Id }, drug);
    }
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, CreateDrugRequest request)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("drugsEnabled"))
            return Forbid();

        var drug = await _context.Drugs.FindAsync(id);
        if (drug == null)
            return NotFound();
        drug.Name = request.Name;
        drug.Dosage = request.Dosage;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("drugsEnabled"))
            return Forbid();

        var drug = await _context.Drugs.FindAsync(id);
        if (drug == null)
            return NotFound();
        _context.Drugs.Remove(drug);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateDrugRequest(string Name, string? Dosage);
