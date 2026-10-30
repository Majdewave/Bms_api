using System.Text.Json;
using Clienta.Api.DTOs;
using Clienta.Api.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class BusinessInvoiceProjectMetadataTests
{
    [TestMethod]
    public void InvoiceResponseSerializesLinkedProjectMetadataWithoutChangingFinancialFields()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var response = new InvoiceResponse
        {
            ProjectId = Guid.NewGuid(),
            ProjectName = "Design project",
            ProjectEndDate = new DateOnly(2026, 12, 31),
            ProjectStatus = ProjectStatus.Active,
            ProjectPaymentType = ProjectPaymentType.Milestone,
            TotalAmount = 1250m,
            FinalAmountToPay = 1250m,
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, options));
        var root = json.RootElement;

        Assert.AreEqual("Design project", root.GetProperty("projectName").GetString());
        Assert.AreEqual("2026-12-31", root.GetProperty("projectEndDate").GetString());
        Assert.AreEqual("Active", root.GetProperty("projectStatus").GetString());
        Assert.AreEqual("Milestone", root.GetProperty("projectPaymentType").GetString());
        Assert.AreEqual(1250m, root.GetProperty("totalAmount").GetDecimal());
        Assert.AreEqual(1250m, root.GetProperty("finalAmountToPay").GetDecimal());
    }
}