using Clienta.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class TechnicianNotesRequestValidationTests
{
    [TestMethod]
    public void TechnicianNotesRequest_UsesMvcValidationAndEnforcesMaximumLength()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var serviceProvider = services.BuildServiceProvider();

        var validator = serviceProvider.GetRequiredService<IObjectModelValidator>();
        var validContext = CreateActionContext();
        validator.Validate(
            validContext,
            validationState: null,
            prefix: "request",
            model: new UpdateImagingOrderTechnicianNotesRequest(new string('x', 4000)));

        Assert.IsTrue(validContext.ModelState.IsValid);

        var invalidContext = CreateActionContext();
        validator.Validate(
            invalidContext,
            validationState: null,
            prefix: "request",
            model: new UpdateImagingOrderTechnicianNotesRequest(new string('x', 4001)));

        Assert.IsFalse(invalidContext.ModelState.IsValid);
    }

    private static ActionContext CreateActionContext() =>
        new(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
}
