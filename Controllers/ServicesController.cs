using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.DTOs;
using Clienta.Api.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Clienta.Api.Controllers
{
    [ApiController]
    [Route("api/services")]
    [Authorize] // כל ה-Controller דורש משתמש מחובר
    public class ServicesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITenantContext _tenant;

        public ServicesController(AppDbContext context, ITenantContext tenant)
        {
            _context = context;
            _tenant = tenant;
        }

        // GET: /api/services
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            if (_tenant.TenantId == Guid.Empty)
                return BadRequest("Tenant not resolved.");

            var services = await _context.Services
                .Include(s => s.Department)
                .Where(s => s.TenantId == _tenant.TenantId && s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new ServiceResponse(
                    s.Id,
                    s.Name,
                    s.DefaultDurationMinutes,
                    s.DepartmentId,
                    s.Department != null ? s.Department.Name : null,
                    s.Department != null ? s.Department.Color : null
                ))
                .ToListAsync();

            return Ok(services);
        }

        // POST: /api/services
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateServiceRequest request)
        {
            if (_tenant.TenantId == Guid.Empty)
                return BadRequest("Tenant not resolved.");

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Service name is required.");

            if (request.DepartmentId == null || request.DepartmentId == Guid.Empty)
                return BadRequest("Department is required.");

            var department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value && d.TenantId == _tenant.TenantId);

            if (department == null)
                return BadRequest("Selected department is invalid.");

            var service = new Service
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                DefaultDurationMinutes = request.DefaultDurationMinutes,
                DepartmentId = request.DepartmentId,
                IsActive = true,
                TenantId = _tenant.TenantId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Services.Add(service);
            await _context.SaveChangesAsync();

            return Ok(new ServiceResponse(
                service.Id,
                service.Name,
                service.DefaultDurationMinutes,
                service.DepartmentId,
                department.Name,
                department.Color
            ));
        }

        // PUT: /api/services/{id}
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Service name is required.");

            if (request.DepartmentId == null || request.DepartmentId == Guid.Empty)
                return BadRequest("Department is required.");

            var department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value && d.TenantId == _tenant.TenantId);

            if (department == null)
                return BadRequest("Selected department is invalid.");

            var service = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == _tenant.TenantId);

            if (service == null)
                return NotFound();

            service.Name = request.Name.Trim();
            service.DefaultDurationMinutes = request.DefaultDurationMinutes;
            service.DepartmentId = request.DepartmentId;

            await _context.SaveChangesAsync();

            return Ok(new ServiceResponse(
                service.Id,
                service.Name,
                service.DefaultDurationMinutes,
                service.DepartmentId,
                department.Name,
                department.Color
            ));
        }

        // DELETE: /api/services/{id}
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var service = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == _tenant.TenantId);

            if (service == null)
                return NotFound();

            _context.Services.Remove(service);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}