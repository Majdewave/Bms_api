using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly AppDbContext _db;

    public InvoicesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var invoices = await _db.Invoices
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(invoices);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Invoice request)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = request.InvoiceNumber,
            ClientName = request.ClientName,
            Amount = request.Amount
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        return Ok(invoice);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id)
    {
        var invoice = await _db.Invoices.FindAsync(id);

        if (invoice == null)
            return NotFound();

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);

                page.Content().Column(col =>
                {
                    col.Item().Text("INVOICE").FontSize(24).Bold();

                    col.Item().Text($"Number: {invoice.InvoiceNumber}");
                    col.Item().Text($"Client: {invoice.ClientName}");
                    col.Item().Text($"Amount: {invoice.Amount}");
                    col.Item().Text($"Date: {invoice.CreatedAt}");
                });
            });
        }).GeneratePdf();

        return File(pdf, "application/pdf", $"invoice-{invoice.Id}.pdf");
    }
}