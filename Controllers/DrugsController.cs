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

    // GET /api/drugs/search
    [HttpGet("search")]
    public async Task<IActionResult> Search(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new List<object>());

        var drugs = await _context.Drugs
            .Where(d => d.Name.ToLower().Contains(q.ToLower()))
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
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, CreateDrugRequest request)
    {
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
        var drug = await _context.Drugs.FindAsync(id);
        if (drug == null)
            return NotFound();
        _context.Drugs.Remove(drug);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateDrugRequest(string Name, string? Dosage);
