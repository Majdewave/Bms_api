using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;
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
                .Where(s => s.TenantId == _tenant.TenantId && s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.DefaultDurationMinutes
                })
                .ToListAsync();

            return Ok(services);
        }

        // POST: /api/services
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] Service request)
        {
            if (_tenant.TenantId == Guid.Empty)
                return BadRequest("Tenant not resolved.");

            var service = new Service
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                DefaultDurationMinutes = request.DefaultDurationMinutes,
                IsActive = true,
                TenantId = _tenant.TenantId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Services.Add(service);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                service.Id,
                service.Name,
                service.DefaultDurationMinutes
            });
        }

        // PUT: /api/services/{id}
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] Service request)
        {
            var service = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == _tenant.TenantId);

            if (service == null)
                return NotFound();

            service.Name = request.Name;
            service.DefaultDurationMinutes = request.DefaultDurationMinutes;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                service.Id,
                service.Name,
                service.DefaultDurationMinutes
            });
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