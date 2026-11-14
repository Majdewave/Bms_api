using Clienta.Api.Controllers;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class InterpretationReportTemplateTests
{
    [TestMethod]
    public async Task Templates_AreInterpreterAndTenantScoped_FilterByModality_AndCanBeUpdatedOrDeactivated()
    {
        await using var fixture = await MarkerFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var ownerController = fixture.CreateController(context, fixture.Interpreter.Id);

        var createdGeneral = await ownerController.CreateReportTemplate(
            new SaveInterpretationReportTemplateRequest("General", null, "General body"), CancellationToken.None);
        var createdMatching = await ownerController.CreateReportTemplate(
            new SaveInterpretationReportTemplateRequest("XR template", "xr", "XR body"), CancellationToken.None);
        var createdDx = await ownerController.CreateReportTemplate(
            new SaveInterpretationReportTemplateRequest("DX template", "DX", "DX body"), CancellationToken.None);
        var createdDr = await ownerController.CreateReportTemplate(
            new SaveInterpretationReportTemplateRequest("DR template", "DR", "DR body"), CancellationToken.None);
        var createdCr = await ownerController.CreateReportTemplate(
            new SaveInterpretationReportTemplateRequest("CR template", "CR", "CR body"), CancellationToken.None);
        var createdUs = await ownerController.CreateReportTemplate(
            new SaveInterpretationReportTemplateRequest("US template", "US", "US body"), CancellationToken.None);
        var createdOther = await ownerController.CreateReportTemplate(
            new SaveInterpretationReportTemplateRequest("CT template", "CT", "CT body"), CancellationToken.None);

        Assert.IsInstanceOfType<CreatedAtActionResult>(createdGeneral);
        Assert.IsInstanceOfType<CreatedAtActionResult>(createdMatching);
        Assert.IsInstanceOfType<CreatedAtActionResult>(createdDx);
        Assert.IsInstanceOfType<CreatedAtActionResult>(createdDr);
        Assert.IsInstanceOfType<CreatedAtActionResult>(createdCr);
        Assert.IsInstanceOfType<CreatedAtActionResult>(createdUs);
        Assert.IsInstanceOfType<CreatedAtActionResult>(createdOther);
        var matchingDto = (InterpretationReportTemplateDto)((CreatedAtActionResult)createdMatching).Value!;
        var templateId = matchingDto.Id;
        var stored = await context.InterpretationReportTemplates.SingleAsync(item => item.Id == templateId);
        Assert.AreEqual(fixture.Tenant.Id, stored.TenantId);
        Assert.AreEqual(fixture.Interpreter.Id, stored.InterpreterUserId);

        var filtered = await ownerController.ListReportTemplates("XR", CancellationToken.None);
        var filteredTemplates = ((IEnumerable<InterpretationReportTemplateDto>)((OkObjectResult)filtered).Value!).ToArray();
        CollectionAssert.AreEquivalent(new[] { "General", "XR template" }, filteredTemplates.Select(item => item.Name).ToArray());
        Assert.IsInstanceOfType<OkObjectResult>(await ownerController.GetReportTemplate(templateId, CancellationToken.None));

        foreach (var xrayModality in new[] { "DX", "DR", "CR" })
        {
            var xrayResult = await ownerController.ListReportTemplates(xrayModality, CancellationToken.None);
            var xrayTemplates = ((IEnumerable<InterpretationReportTemplateDto>)((OkObjectResult)xrayResult).Value!).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "General", "DX template", "DR template", "CR template" },
                xrayTemplates.Select(item => item.Name).ToArray());
        }

        var ultrasoundResult = await ownerController.ListReportTemplates("US", CancellationToken.None);
        var ultrasoundTemplates = ((IEnumerable<InterpretationReportTemplateDto>)((OkObjectResult)ultrasoundResult).Value!).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "General", "US template" },
            ultrasoundTemplates.Select(item => item.Name).ToArray());

        Assert.AreEqual("DR", await context.InterpretationReportTemplates.Where(item => item.Name == "DR template").Select(item => item.Modality).SingleAsync());
        Assert.AreEqual("CR", await context.InterpretationReportTemplates.Where(item => item.Name == "CR template").Select(item => item.Modality).SingleAsync());

        var secondInterpreter = new User
        {
            Id = Guid.NewGuid(), TenantId = fixture.Tenant.Id, Email = "second-interpreter@test.local",
            PasswordHash = "hash", Role = "Interpreter",
        };
        context.Users.Add(secondInterpreter);
        await context.SaveChangesAsync();
        var otherInterpreterController = fixture.CreateController(context, secondInterpreter.Id);
        var otherInterpreterList = await otherInterpreterController.ListReportTemplates(null, CancellationToken.None);
        Assert.AreEqual(0, ((IEnumerable<InterpretationReportTemplateDto>)((OkObjectResult)otherInterpreterList).Value!).Count());
        Assert.IsInstanceOfType<NotFoundResult>(await otherInterpreterController.GetReportTemplate(templateId, CancellationToken.None));
        Assert.IsInstanceOfType<NotFoundResult>(await otherInterpreterController.UpdateReportTemplate(
            templateId, new SaveInterpretationReportTemplateRequest("Hijack", null, "Hijack"), CancellationToken.None));
        Assert.IsInstanceOfType<NotFoundResult>(await otherInterpreterController.DeactivateReportTemplate(templateId, CancellationToken.None));

        fixture.TenantContext.SetUserId(fixture.Interpreter.Id);
        fixture.TenantContext.SetTenant(Guid.NewGuid());
        var otherTenantList = await ownerController.ListReportTemplates(null, CancellationToken.None);
        Assert.AreEqual(0, ((IEnumerable<InterpretationReportTemplateDto>)((OkObjectResult)otherTenantList).Value!).Count());
        Assert.IsInstanceOfType<NotFoundResult>(await ownerController.GetReportTemplate(templateId, CancellationToken.None));
        fixture.TenantContext.SetTenant(fixture.Tenant.Id);

        var updated = await ownerController.UpdateReportTemplate(
            templateId,
            new SaveInterpretationReportTemplateRequest("Updated XR", "XR", "Updated body"),
            CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(updated);
        Assert.AreEqual("Updated body", ((InterpretationReportTemplateDto)((OkObjectResult)updated).Value!).Content);

        Assert.IsInstanceOfType<NoContentResult>(await ownerController.DeactivateReportTemplate(templateId, CancellationToken.None));
        Assert.IsFalse(await context.InterpretationReportTemplates.Where(item => item.Id == templateId).Select(item => item.IsActive).SingleAsync());
        var afterDeactivate = await ownerController.ListReportTemplates("XR", CancellationToken.None);
        Assert.IsFalse(((IEnumerable<InterpretationReportTemplateDto>)((OkObjectResult)afterDeactivate).Value!).Any(item => item.Id == templateId));
    }

    [TestMethod]
    public async Task TemplateOperationsRequireInterpreterRole_AndTemplateEditsDoNotChangeSavedReportWorkflow()
    {
        await using var fixture = await MarkerFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var ownerController = fixture.CreateController(context, fixture.Interpreter.Id);
        var deniedController = fixture.CreateController(context, fixture.Interpreter.Id, "Owner");
        var request = new SaveInterpretationReportTemplateRequest("Template", null, "Template body");

        Assert.IsInstanceOfType<ForbidResult>(await deniedController.CreateReportTemplate(request, CancellationToken.None));
        Assert.AreEqual(0, await context.InterpretationReportTemplates.CountAsync());

        var created = (CreatedAtActionResult)await ownerController.CreateReportTemplate(request, CancellationToken.None);
        var template = (InterpretationReportTemplateDto)created.Value!;
        var reportResult = await ownerController.SaveReport(
            fixture.Request.Id,
            new SaveInterpreterReportRequest("Independent report text"),
            CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(reportResult);
        Assert.AreEqual(InterpretationRequestStatuses.InProgress, (await context.InterpretationRequests.SingleAsync()).Status);

        var updateResult = await ownerController.UpdateReportTemplate(
            template.Id,
            new SaveInterpretationReportTemplateRequest("Template", null, "Changed template body"),
            CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(updateResult);
        Assert.AreEqual("Independent report text", (await context.InterpretationReports.SingleAsync()).Content);

        var secondSave = await ownerController.SaveReport(
            fixture.Request.Id,
            new SaveInterpreterReportRequest("Edited report text"),
            CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(secondSave);
        Assert.AreEqual("Edited report text", (await context.InterpretationReports.SingleAsync()).Content);
        Assert.AreEqual(InterpretationRequestStatuses.InProgress, (await context.InterpretationRequests.SingleAsync()).Status);
    }
}
