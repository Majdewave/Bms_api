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
        var client = await _db.Clients.FindAsync(request.ClientId);

        if (client == null)
            return BadRequest("Client not found");

        var invoice = new Invoice
        {
            InvoiceNumber = request.InvoiceNumber,
            ClientId = client.Id,
            ClientName = client.FullName,
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
                page.Margin(30);

                page.Content().Column(col =>
                {
                    col.Item().Text("INVOICE")
                        .FontSize(28)
                        .Bold();

                    col.Item().PaddingTop(10);

                    col.Item().Text($"Invoice #: {invoice.InvoiceNumber}");
                    col.Item().Text($"Client: {invoice.ClientName}");
                    col.Item().Text($"Date: {invoice.CreatedAt:yyyy-MM-dd}");

                    col.Item().PaddingVertical(10).LineHorizontal(1);

                    col.Item().Text($"Total Amount: {invoice.Amount}")
                        .FontSize(18)
                        .Bold();
                });
            });
        }).GeneratePdf();

        return File(pdf, "application/pdf", $"invoice-{invoice.Id}.pdf");
    }
}