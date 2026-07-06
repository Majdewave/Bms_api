using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/departments")]
[Authorize(Policy = "manage_business_settings")]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IDepartmentFeatureResolver _departmentFeatureResolver;

    public DepartmentsController(AppDbContext context, ITenantContext tenant, IDepartmentFeatureResolver departmentFeatureResolver)
    {
        _context = context;
        _tenant = tenant;
        _departmentFeatureResolver = departmentFeatureResolver;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var departments = await _context.Departments
            .OrderBy(d => d.DisplayOrder)
            .ThenBy(d => d.Name)
            .ToListAsync();

        return Ok(departments.Select(MapDepartment));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);

        if (department == null)
            return NotFound();

        return Ok(MapDepartment(department));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var name = NormalizeName(request.Name);
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Department name is required.");

        var exists = await DepartmentNameExistsAsync(_tenant.TenantId, name);
        if (exists)
            return Conflict("Department name already exists.");

        var department = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Name = name,
            Description = NormalizeOptionalText(request.Description),
            Color = NormalizeOptionalText(request.Color),
            DisplayOrder = request.DisplayOrder,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        _context.Departments.Add(department);
        await _context.SaveChangesAsync();

        return Ok(MapDepartment(department));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest request)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);

        if (department == null)
            return NotFound();

        var name = NormalizeName(request.Name);
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Department name is required.");

        var exists = await DepartmentNameExistsAsync(_tenant.TenantId, name, department.Id);
        if (exists)
            return Conflict("Department name already exists.");

        department.Name = name;
        department.Description = NormalizeOptionalText(request.Description);
        department.Color = NormalizeOptionalText(request.Color);
        department.DisplayOrder = request.DisplayOrder;
        department.IsActive = request.IsActive;

        await _context.SaveChangesAsync();

        return Ok(MapDepartment(department));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);

        if (department == null)
            return NotFound();

        department.IsActive = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:guid}/features")]
    public async Task<IActionResult> GetFeatures(Guid id, CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var departmentExists = await _context.Departments
            .AnyAsync(d => d.Id == id && d.TenantId == _tenant.TenantId, cancellationToken);

        if (!departmentExists)
            return NotFound();

        var features = await _departmentFeatureResolver.GetDepartmentFeaturesAsync(_tenant.TenantId, id, cancellationToken);
        return Ok(features);
    }

    [HttpPut("{id:guid}/features")]
    public async Task<IActionResult> UpdateFeatures(Guid id, [FromBody] UpdateDepartmentFeaturesRequest request, CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var departmentExists = await _context.Departments
            .AnyAsync(d => d.Id == id && d.TenantId == _tenant.TenantId, cancellationToken);

        if (!departmentExists)
            return NotFound();

        var updates = request?.Features ?? new List<DepartmentFeatureUpdateItem>();
        await _departmentFeatureResolver.UpdateDepartmentFeaturesAsync(_tenant.TenantId, id, updates, cancellationToken);

        var features = await _departmentFeatureResolver.GetDepartmentFeaturesAsync(_tenant.TenantId, id, cancellationToken);
        return Ok(features);
    }

    private static DepartmentResponse MapDepartment(Department department) => new(
        department.Id,
        department.TenantId,
        department.Name,
        department.Description,
        department.Color,
        department.DisplayOrder,
        department.IsActive,
        department.CreatedAt
    );

    private static string NormalizeName(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private async Task<bool> DepartmentNameExistsAsync(Guid tenantId, string name, Guid? excludeId = null)
    {
        var query = _context.Departments
            .IgnoreQueryFilters()
            .Where(department => department.TenantId == tenantId && department.Name.ToLower() == name.ToLower());

        if (excludeId.HasValue)
        {
            query = query.Where(department => department.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }
}