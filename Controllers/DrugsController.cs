using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/drugs")]
[Authorize]
public class DrugsController : ControllerBase
{
    private readonly AppDbContext _context;

    public DrugsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/drugs
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
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
        var drug = new Drug
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Dosage = request.Dosage,
            CreatedAt = DateTime.UtcNow
        };

        _context.Drugs.Add(drug);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { id = drug.Id }, drug);
    }
}

public record CreateDrugRequest(string Name, string? Dosage);
