using System.Text.Json;
using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class InvoiceCalendarDateTests
{
    [DataTestMethod]
    [DataRow("19/10/2026", "2026-10-19")]
    [DataRow("01/01/2027", "2027-01-01")]
    [DataRow("31/12/2026", "2026-12-31")]
    [DataRow("28/03/2027", "2027-03-28")]
    public void RequestAndResponse_RetainCalendarDate(string selected, string expectedIso)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = JsonSerializer.Deserialize<CreateInvoiceRequest>(
            JsonSerializer.Serialize(new { invoiceDate = expectedIso, dueDate = expectedIso }, options), options)!;

        Assert.AreEqual(DateOnly.ParseExact(expectedIso, "yyyy-MM-dd"), request.InvoiceDate);
        Assert.AreEqual(DateOnly.ParseExact(expectedIso, "yyyy-MM-dd"), request.DueDate);

        var storage = InvoiceCalendarDate.ToStorageDateTime(request.InvoiceDate);
        var response = new InvoiceResponse
        {
            InvoiceDate = InvoiceCalendarDate.FromStorageDateTime(storage),
            DueDate = InvoiceCalendarDate.FromStorageDateTime(InvoiceCalendarDate.ToStorageDateTime(request.DueDate!.Value)),
        };
        var serializedResponse = JsonSerializer.Serialize(response, options);
        using var json = JsonDocument.Parse(serializedResponse);

        Assert.AreEqual(expectedIso, json.RootElement.GetProperty("invoiceDate").GetString());
        Assert.AreEqual(expectedIso, json.RootElement.GetProperty("dueDate").GetString());
        Assert.AreEqual(selected, request.InvoiceDate.ToString("dd/MM/yyyy"));
    }
}
