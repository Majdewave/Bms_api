using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Clienta.Api.Authorization;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Clienta.Api.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class RivhitIntegrationTests
{
    private const string Token = "rivhit-secret-token-123";

    private sealed class MetadataHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            object payload = request.RequestUri.AbsolutePath.EndsWith("Payment.TypeList", StringComparison.Ordinal)
                ? new { error_code = 0, data = new { payment_type_list = new[] { new { payment_type = 31, payment_name = " Cash\n ", type_code = 7 } } } }
                : new { error_code = 0, data = new { receipt_type_list = new[] { new { receipt_type = 42, receipt_name = "Standard", is_invoice_receipt = false, payment_type = (int?)null, start_number = 100, open_format = 1, clause46 = 0 } } } };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        }
    }

    private sealed class FakeProvider : IAccountingIntegrationProvider, IAccountingMetadataProvider
    {
        public ProviderConnectionResult Result = ProviderConnectionResult.Ok();
        public bool Throw;
        public string? LastCredential;
        public int Calls;
        public int PaymentTypeCalls;
        public int ReceiptTypeCalls;
        public IReadOnlyList<ProviderPaymentType> PaymentTypes = new[] { new ProviderPaymentType(12, "Cash", 1) };
        public IReadOnlyList<ProviderReceiptType> ReceiptTypes = new[] { new ProviderReceiptType(23, "Receipt", false, null, 1, 0, 0) };
        public IntegrationProvider Provider => IntegrationProvider.Rivhit;

        public Task<ProviderConnectionResult> TestConnectionAsync(string credential, CancellationToken cancellationToken)
        {
            Calls++;
            LastCredential = credential;
            if (Throw) throw new InvalidOperationException("boom " + credential);
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<ProviderPaymentType>> GetPaymentTypesAsync(string credential, CancellationToken cancellationToken)
        {
            PaymentTypeCalls++;
            LastCredential = credential;
            return Task.FromResult(PaymentTypes);
        }

        public Task<IReadOnlyList<ProviderReceiptType>> GetReceiptTypesAsync(string credential, CancellationToken cancellationToken)
        {
            ReceiptTypeCalls++;
            LastCredential = credential;
            return Task.FromResult(ReceiptTypes);
        }
    }

    private sealed class Env : IAsyncDisposable
    {
        public SqliteConnection Connection = null!;
        public AppDbContext Db = null!;
        public TestTenantContext Ctx = null!;
        public FakeProvider Provider = new();
        public IntegrationSecretProtector Protector = new("unit-test-key");
        public Guid Business, OtherBusiness, Clinic, Legacy;

        public TenantIntegrationService Service() => new(Db, Protector, new[] { Provider });

        public BusinessRivhitIntegrationController Controller(Guid tenantId)
        {
            Ctx.SetTenant(tenantId);
            return new BusinessRivhitIntegrationController(Ctx, new TenantProductService(Db), Service());
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private static async Task<Env> NewEnvAsync()
    {
        var env = new Env { Connection = new SqliteConnection("Data Source=:memory:"), Ctx = new TestTenantContext() };
        await env.Connection.OpenAsync();
        env.Connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        env.Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(env.Connection).Options, env.Ctx);
        await env.Db.Database.EnsureCreatedAsync();

        env.Business = Guid.NewGuid();
        env.OtherBusiness = Guid.NewGuid();
        env.Clinic = Guid.NewGuid();
        env.Legacy = Guid.NewGuid();
        env.Db.Tenants.AddRange(
            NewTenant(env.Business, TenantProductType.Business),
            NewTenant(env.OtherBusiness, TenantProductType.Business),
            NewTenant(env.Clinic, TenantProductType.Clinic),
            NewTenant(env.Legacy, null));
        await env.Db.SaveChangesAsync();
        return env;
    }

    private static Tenant NewTenant(Guid id, TenantProductType? product) =>
        new() { Id = id, Name = "T", Subdomain = "t-" + Guid.NewGuid().ToString("N"), ProductType = product, Plan = PlanType.Pro };

    private static T Value<T>(IActionResult result) where T : class
    {
        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok, "Expected 200 but got " + result.GetType().Name);
        return (T)ok.Value!;
    }

    [TestMethod]
    public async Task BusinessAdmin_CanReadDefaultState()
    {
        await using var env = await NewEnvAsync();
        var state = Value<IntegrationStateDto>(await env.Controller(env.Business).Get(default));

        Assert.AreEqual("Rivhit", state.Provider);
        Assert.AreEqual("notConfigured", state.ConnectionStatus);
        Assert.IsFalse(state.IsEnabled);
        Assert.IsFalse(state.IsConfigured);
        Assert.IsFalse(state.PaymentsAvailable);
    }

    [TestMethod]
    public async Task BusinessAdmin_CanReadSanitizedPaymentAndReceiptMetadata()
    {
        await using var env = await NewEnvAsync();
        var controller = env.Controller(env.Business);
        await controller.Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default);

        var result = await controller.GetMetadata(default);
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual(1, env.Provider.PaymentTypeCalls);
        Assert.AreEqual(1, env.Provider.ReceiptTypeCalls);
        Assert.AreEqual(Token, env.Provider.LastCredential);
        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value);
        StringAssert.Contains(json, "paymentTypes");
        StringAssert.Contains(json, "receiptTypes");
        StringAssert.Contains(json, "Cash");
        StringAssert.Contains(json, "IsInvoiceReceipt");
        Assert.IsFalse(json.Contains(Token));
        Assert.IsFalse(json.Contains("api_token", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("client_message", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task OfficialMetadataOperations_UseReadOnlyDocumentedPathsAndSanitizeResults()
    {
        var handler = new MetadataHandler();
        var provider = new RivhitAccountingProvider(
            new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) },
            NullLogger<RivhitAccountingProvider>.Instance);

        var payments = await provider.GetPaymentTypesAsync(Token, default);
        var receipts = await provider.GetReceiptTypesAsync(Token, default);

        CollectionAssert.AreEqual(new[]
        {
            "/online/RivhitOnlineAPI.svc/Payment.TypeList",
            "/online/RivhitOnlineAPI.svc/Receipt.TypeList"
        }, handler.Paths);
        Assert.AreEqual(31, payments.Single().PaymentType);
        Assert.AreEqual("Cash", payments.Single().PaymentName);
        Assert.AreEqual(7, payments.Single().TypeCode);
        Assert.AreEqual(42, receipts.Single().ReceiptType);
        Assert.AreEqual("Standard", receipts.Single().ReceiptName);
        Assert.IsFalse(receipts.Single().IsInvoiceReceipt);
        Assert.IsNull(receipts.Single().PaymentType);
        Assert.IsTrue(handler.Bodies.All(body => body.Contains(Token, StringComparison.Ordinal)));
        Assert.IsFalse(handler.Paths.Any(path => path.EndsWith("Receipt.New", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task MetadataRequiresEnabledBusinessIntegrationAndDoesNotMutate()
    {
        await using var env = await NewEnvAsync();
        var controller = env.Controller(env.Business);
        Assert.IsInstanceOfType(await controller.GetMetadata(default), typeof(ObjectResult));
        Assert.AreEqual(0, env.Provider.PaymentTypeCalls);

        await controller.Update(new UpdateIntegrationRequest { ApiToken = Token }, default);
        Assert.IsInstanceOfType(await controller.GetMetadata(default), typeof(ObjectResult));
        Assert.AreEqual(0, env.Provider.PaymentTypeCalls);

        await controller.Update(new UpdateIntegrationRequest { IsEnabled = true }, default);
        var before = await env.Db.ExternalAccountingDocuments.CountAsync();
        await controller.GetMetadata(default);
        Assert.AreEqual(before, await env.Db.ExternalAccountingDocuments.CountAsync());
        Assert.AreEqual(1, env.Provider.PaymentTypeCalls);
        Assert.AreEqual(1, env.Provider.ReceiptTypeCalls);
    }

    [TestMethod]
    public async Task BusinessAdmin_CanConfigure_AndCredentialIsStoredProtected()
    {
        await using var env = await NewEnvAsync();
        var state = Value<IntegrationStateDto>(await env.Controller(env.Business).Update(new UpdateIntegrationRequest { ApiToken = Token }, default));

        Assert.IsTrue(state.IsConfigured);
        Assert.AreEqual("disabled", state.ConnectionStatus);
        var stored = await env.Db.TenantIntegrations.AsNoTracking().SingleAsync();
        Assert.IsNotNull(stored.EncryptedCredential);
        Assert.IsFalse(stored.EncryptedCredential!.Contains(Token));
        Assert.AreEqual(Token, env.Protector.Unprotect(stored.EncryptedCredential, $"integration:Rivhit:{env.Business:N}"));
    }

    [TestMethod]
    public async Task BusinessAdmin_CanEnableAndDisable_WithoutLosingCredential()
    {
        await using var env = await NewEnvAsync();
        var c = env.Controller(env.Business);

        var noToken = await c.Update(new UpdateIntegrationRequest { IsEnabled = true }, default);
        Assert.IsInstanceOfType(noToken, typeof(BadRequestObjectResult));

        await c.Update(new UpdateIntegrationRequest { ApiToken = Token }, default);
        var on = Value<IntegrationStateDto>(await c.Update(new UpdateIntegrationRequest { IsEnabled = true }, default));
        Assert.IsTrue(on.IsEnabled);
        Assert.AreEqual("notTested", on.ConnectionStatus);

        var off = Value<IntegrationStateDto>(await c.Update(new UpdateIntegrationRequest { IsEnabled = false }, default));
        Assert.IsFalse(off.IsEnabled);
        Assert.IsTrue(off.IsConfigured);
        Assert.IsNotNull((await env.Db.TenantIntegrations.AsNoTracking().SingleAsync()).EncryptedCredential);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("********")]
    [DataRow("\u2022\u2022\u2022\u2022")]
    public async Task MaskedOrEmptyToken_DoesNotOverwriteStoredCredential(string masked)
    {
        await using var env = await NewEnvAsync();
        var c = env.Controller(env.Business);
        await c.Update(new UpdateIntegrationRequest { ApiToken = Token }, default);
        var before = (await env.Db.TenantIntegrations.AsNoTracking().SingleAsync()).EncryptedCredential;

        var result = await c.Update(new UpdateIntegrationRequest { ApiToken = masked, AccountingEnabled = true }, default);
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual(before, (await env.Db.TenantIntegrations.AsNoTracking().SingleAsync()).EncryptedCredential);
    }

    [TestMethod]
    public async Task PaymentsStayOff_EvenWhenRequested()
    {
        await using var env = await NewEnvAsync();
        await env.Controller(env.Business).Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default);
        var row = await env.Db.TenantIntegrations.AsNoTracking().SingleAsync();
        Assert.IsFalse(row.PaymentsEnabled);
    }

    [TestMethod]
    public async Task StaffCannotModify_AdminPolicyIsEnforcedOnController()
    {
        var attr = typeof(BusinessRivhitIntegrationController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.AreEqual(IntegrationAuthorization.ManagePolicy, attr.Policy);
        Assert.IsFalse(typeof(BusinessRivhitIntegrationController).GetMethods().Any(m => m.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any()));

        var services = new ServiceCollection().AddLogging().AddAuthorization(IntegrationAuthorization.AddPolicies).BuildServiceProvider();
        var authz = services.GetRequiredService<IAuthorizationService>();

        ClaimsPrincipal User(string role) => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));
        Assert.IsTrue((await authz.AuthorizeAsync(User("Admin"), null, IntegrationAuthorization.ManagePolicy)).Succeeded);
        Assert.IsFalse((await authz.AuthorizeAsync(User("Staff"), null, IntegrationAuthorization.ManagePolicy)).Succeeded);
        Assert.IsFalse((await authz.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, IntegrationAuthorization.ManagePolicy)).Succeeded);
    }

    [TestMethod]
    public async Task CrossTenant_CannotReadTestUpdateOrUseOtherTenantCredential()
    {
        await using var env = await NewEnvAsync();
        await env.Controller(env.Business).Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default);

        var other = env.Controller(env.OtherBusiness);
        var state = Value<IntegrationStateDto>(await other.Get(default));
        Assert.IsFalse(state.IsConfigured);

        var test = await other.Test(default);
        Assert.IsInstanceOfType(test, typeof(BadRequestObjectResult));
        Assert.AreEqual(0, env.Provider.Calls);

        Assert.IsNull(await env.Service().GetOperationalCredentialAsync(env.OtherBusiness, IntegrationProvider.Rivhit, default));

        await other.Update(new UpdateIntegrationRequest { ApiToken = "other-token", IsEnabled = true }, default);
        Assert.AreEqual(2, await env.Db.TenantIntegrations.IgnoreQueryFilters().CountAsync());
        env.Ctx.SetTenant(env.Business);
        Assert.AreEqual(Token, await env.Service().GetOperationalCredentialAsync(env.Business, IntegrationProvider.Rivhit, default));

        // Ciphertext is bound to its tenant: moving it to another row yields nothing.
        var a = await env.Db.TenantIntegrations.IgnoreQueryFilters().SingleAsync(i => i.TenantId == env.Business);
        Assert.IsNull(env.Protector.Unprotect(a.EncryptedCredential, $"integration:Rivhit:{env.OtherBusiness:N}"));
    }

    [TestMethod]
    public async Task ApiTokenIsNeverReturned()
    {
        await using var env = await NewEnvAsync();
        var c = env.Controller(env.Business);
        var update = Value<IntegrationStateDto>(await c.Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default));
        var get = Value<IntegrationStateDto>(await c.Get(default));
        var test = await c.Test(default);
        env.Provider.Throw = true;
        var failedTest = await c.Test(default);

        var stored = (await env.Db.TenantIntegrations.AsNoTracking().SingleAsync()).EncryptedCredential!;
        foreach (var payload in new object[] { update, get, ((OkObjectResult)test).Value!, ((OkObjectResult)failedTest).Value! })
        {
            var json = JsonSerializer.Serialize(payload);
            Assert.IsFalse(json.Contains(Token), json);
            Assert.IsFalse(json.Contains(stored), json);
            Assert.IsFalse(json.Contains("boom"), json);
            Assert.IsFalse(json.Contains("apiToken", StringComparison.OrdinalIgnoreCase), json);
        }
    }

    [TestMethod]
    public async Task DisabledIntegration_CannotBeUsed()
    {
        await using var env = await NewEnvAsync();
        var c = env.Controller(env.Business);
        await c.Update(new UpdateIntegrationRequest { ApiToken = Token }, default);
        Assert.IsNull(await env.Service().GetOperationalCredentialAsync(env.Business, IntegrationProvider.Rivhit, default));

        await c.Update(new UpdateIntegrationRequest { IsEnabled = true }, default);
        env.Ctx.SetTenant(env.Business);
        Assert.AreEqual(Token, await env.Service().GetOperationalCredentialAsync(env.Business, IntegrationProvider.Rivhit, default));

        await c.Update(new UpdateIntegrationRequest { IsEnabled = false }, default);
        Assert.IsNull(await env.Service().GetOperationalCredentialAsync(env.Business, IntegrationProvider.Rivhit, default));
    }

    [TestMethod]
    public async Task Disconnect_RemovesCredentialButKeepsRow()
    {
        await using var env = await NewEnvAsync();
        var c = env.Controller(env.Business);
        await c.Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default);

        var state = Value<IntegrationStateDto>(await c.Disconnect(default));
        Assert.IsFalse(state.IsConfigured);
        Assert.IsFalse(state.IsEnabled);
        var row = await env.Db.TenantIntegrations.AsNoTracking().SingleAsync();
        Assert.IsNull(row.EncryptedCredential);
    }

    [TestMethod]
    public async Task LegacyClinicAndUnknownTenants_AreDenied()
    {
        await using var env = await NewEnvAsync();
        foreach (var tenant in new[] { env.Legacy, env.Clinic, Guid.NewGuid() })
        {
            var c = env.Controller(tenant);
            foreach (var result in new[]
            {
                await c.Get(default),
                await c.Update(new UpdateIntegrationRequest { ApiToken = Token }, default),
                await c.Test(default),
                await c.Disconnect(default)
            })
            {
                Assert.AreEqual(403, (result as ObjectResult)?.StatusCode);
            }
        }

        Assert.AreEqual(0, await env.Db.TenantIntegrations.IgnoreQueryFilters().CountAsync());
        Assert.IsInstanceOfType(await env.Controller(Guid.Empty).Get(default), typeof(UnauthorizedObjectResult));
    }

    [TestMethod]
    public async Task InvalidCredentials_ProduceControlledFailure()
    {
        await using var env = await NewEnvAsync();
        var c = env.Controller(env.Business);
        await c.Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default);
        env.Provider.Result = ProviderConnectionResult.Fail(ProviderConnectionFailure.InvalidCredentials);

        var body = Value<object>(await c.Test(default));
        var json = JsonSerializer.Serialize(body);
        StringAssert.Contains(json, "InvalidCredentials");
        Assert.AreEqual(Token, env.Provider.LastCredential);
        Assert.AreEqual("failed", Value<IntegrationStateDto>(await c.Get(default)).ConnectionStatus);
    }

    [TestMethod]
    public async Task SuccessfulTest_MarksConnected_AndNewTokenResetsStatus()
    {
        await using var env = await NewEnvAsync();
        var c = env.Controller(env.Business);
        await c.Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default);
        await c.Test(default);

        var state = Value<IntegrationStateDto>(await c.Get(default));
        Assert.AreEqual("connected", state.ConnectionStatus);
        Assert.IsNotNull(state.LastConnectionTestAt);

        var reset = Value<IntegrationStateDto>(await c.Update(new UpdateIntegrationRequest { ApiToken = "another-token" }, default));
        Assert.AreEqual("notTested", reset.ConnectionStatus);
    }

    [TestMethod]
    public async Task ProviderException_IsContained()
    {
        await using var env = await NewEnvAsync();
        var c = env.Controller(env.Business);
        await c.Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default);
        env.Provider.Throw = true;

        var result = await c.Test(default);
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual("failed", Value<IntegrationStateDto>(await c.Get(default)).ConnectionStatus);
    }

    [TestMethod]
    public async Task UnreadableStoredSecret_FailsControlled()
    {
        await using var env = await NewEnvAsync();
        await env.Controller(env.Business).Update(new UpdateIntegrationRequest { ApiToken = Token, IsEnabled = true }, default);
        env.Protector = new IntegrationSecretProtector("rotated-key");

        var state = Value<IntegrationStateDto>(await env.Controller(env.Business).Get(default));
        Assert.IsTrue(state.IsConfigured);
        var test = await env.Controller(env.Business).Test(default);
        StringAssert.Contains(JsonSerializer.Serialize(Value<object>(test)), "CredentialUnreadable");
        Assert.AreEqual(0, env.Provider.Calls);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> Respond = null!;
        public HttpRequestMessage? Last;
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await Respond(request, LastBody);
        }
    }

    private static RivhitAccountingProvider NewProvider(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new RivhitOptions().ResolveBaseUri() }, NullLogger<RivhitAccountingProvider>.Instance);

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });

    [TestMethod]
    public async Task RivhitProvider_UsesReadOnlyOperation_AndAcceptsErrorCodeZero()
    {
        var handler = new StubHandler { Respond = (_, _) => Json(HttpStatusCode.OK, "{\"error_code\":0,\"data\":{}}") };
        var result = await NewProvider(handler).TestConnectionAsync("tok", default);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(HttpMethod.Post, handler.Last!.Method);
        Assert.AreEqual("https://api.rivhit.co.il/online/RivhitOnlineAPI.svc/Company.Details", handler.Last.RequestUri!.ToString());
        StringAssert.Contains(handler.LastBody, "\"api_token\":\"tok\"");
    }

    [TestMethod]
    public async Task RivhitProvider_MapsFailuresWithoutThrowing()
    {
        async Task<ProviderConnectionFailure> Run(Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> respond) =>
            (await NewProvider(new StubHandler { Respond = respond }).TestConnectionAsync("tok", default)).Failure;

        Assert.AreEqual(ProviderConnectionFailure.InvalidCredentials, await Run((_, _) => Json(HttpStatusCode.OK, "{\"error_code\":3,\"client_message\":\"bad token\"}")));
        Assert.AreEqual(ProviderConnectionFailure.InvalidCredentials, await Run((_, _) => Json(HttpStatusCode.Unauthorized, "")));
        Assert.AreEqual(ProviderConnectionFailure.ProviderUnavailable, await Run((_, _) => Json(HttpStatusCode.ServiceUnavailable, "down")));
        Assert.AreEqual(ProviderConnectionFailure.ProviderUnavailable, await Run((_, _) => throw new HttpRequestException("dns")));
        Assert.AreEqual(ProviderConnectionFailure.Timeout, await Run((_, _) => throw new TaskCanceledException("timeout")));
        Assert.AreEqual(ProviderConnectionFailure.InvalidResponse, await Run((_, _) => Json(HttpStatusCode.OK, "<html>not json</html>")));
        Assert.AreEqual(ProviderConnectionFailure.InvalidResponse, await Run((_, _) => Json(HttpStatusCode.OK, "{\"foo\":1}")));
        Assert.AreEqual(ProviderConnectionFailure.InvalidResponse, await Run((_, _) => Json(HttpStatusCode.OK, "[1,2]")));
    }

    [TestMethod]
    public void RivhitOptions_FallsBackToOfficialHttpsBaseUrl()
    {
        Assert.AreEqual(RivhitOptions.DefaultBaseUrl, new RivhitOptions().ResolveBaseUri().ToString());
        Assert.AreEqual(RivhitOptions.DefaultBaseUrl, new RivhitOptions { BaseUrl = "http://evil.example/" }.ResolveBaseUri().ToString());
        Assert.AreEqual("https://sandbox.example/api/", new RivhitOptions { BaseUrl = "https://sandbox.example/api" }.ResolveBaseUri().ToString());
    }

    [TestMethod]
    public void SecretProtector_RoundTripsAndBindsPurpose()
    {
        var p = new IntegrationSecretProtector("k");
        var cipher = p.Protect("secret", "a");
        Assert.IsNotNull(cipher);
        Assert.AreEqual("secret", p.Unprotect(cipher, "a"));
        Assert.IsNull(p.Unprotect(cipher, "b"));
        Assert.IsNull(p.Unprotect("garbage", "a"));
    }
}
