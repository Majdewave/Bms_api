using Clienta.Api.Authorization;
using Clienta.Api.Authentication;
using Clienta.Api.Data;
using Clienta.Api.Hubs;
using Clienta.Api.Infrastructure.TeamChat.Interfaces;
using Clienta.Api.Infrastructure.TeamChat.Stores;
using Clienta.Api.Middleware;
using Clienta.Api.Services;
using Clienta.Api.Services.Platform;
using Clienta.Api.Services.WhatsApp;
using Clienta.Api.Services.WhatsApp.Meta;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;
using System.Text;
using Amazon.S3;

using Stripe;

var builder = WebApplication.CreateBuilder(args);
// Stripe configuration
var stripeSecretKey = builder.Configuration["Stripe:SecretKey"];
StripeConfiguration.ApiKey = stripeSecretKey;
// Register DrugSeedService
builder.Services.AddScoped<DrugSeedService>();

// QuestPDF License
QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddScoped<IQuoteConversionService, QuoteConversionService>();
builder.Services.AddScoped<TenantResolver>();
builder.Services.AddScoped<IFeatureService, FeatureService>();
builder.Services.AddScoped<IDepartmentFeatureResolver, DepartmentFeatureResolver>();
builder.Services.AddScoped<IUserDepartmentFeatureAccessService, UserDepartmentFeatureAccessService>();
builder.Services.AddScoped<IDepartmentAccessService, DepartmentAccessService>();
builder.Services.AddScoped<TrialService>();
builder.Services.AddHostedService<TrialBackgroundService>();
builder.Services.AddAWSService<IAmazonS3>(); 

FontManager.RegisterFont(
    System.IO.File.OpenRead(Path.Combine("wwwroot", "fonts", "NotoSansHebrew-Regular.ttf"))
);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.EnableRetryOnFailure()
    ));

builder.Services.AddDbContext<MasterDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.EnableRetryOnFailure()
    ));


builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<PlatformJwtService>();
builder.Services.AddScoped<AuthSeedService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<Clienta.Api.Services.TokenService>();
builder.Services.AddScoped<IEmailService, SendGridEmailService>();
builder.Services.AddScoped<IOnboardingLocalizationService, OnboardingLocalizationService>();
builder.Services.AddScoped<ITenantApprovalService, TenantApprovalService>();
builder.Services.AddScoped<ITenantAccessValidator, TenantAccessValidator>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOnboardingService, OnboardingService>();
builder.Services.AddScoped<ITenantSeedService, TenantSeedService>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<IImagingAccessionNumberGenerator, ImagingAccessionNumberGenerator>();
builder.Services.AddScoped<IPlanEnforcementService, PlanEnforcementService>();
builder.Services.AddSingleton<IPlanProvider, PlanProvider>();
builder.Services.AddHostedService<CleanupService>();
builder.Services.AddScoped<IPlatformTenantManagementService, PlatformTenantManagementService>();
builder.Services.AddScoped<IPlatformDashboardService, PlatformDashboardService>();
builder.Services.AddScoped<IPlatformUserManagementService, PlatformUserManagementService>();
builder.Services.AddScoped<IPlatformSettingsService, PlatformSettingsService>();
builder.Services.AddScoped<IQueueDisplayService, QueueDisplayService>();

builder.Services.AddScoped<DashboardService>();
builder.Services.Configure<MetaWhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient("MetaGraph", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddDataProtection();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IMetaOAuthService, MetaOAuthService>();
builder.Services.AddScoped<IMetaGraphApiService, MetaGraphApiService>();
builder.Services.AddScoped<IMetaStateService, MetaStateService>();
builder.Services.AddScoped<WhatsAppWebhookService>();
builder.Services.AddScoped<WhatsAppTemplateService>();
builder.Services.AddSingleton<WhatsAppMessageQueue>();
builder.Services.AddScoped<Clienta.Api.Repositories.AppointmentsRepository>();
builder.Services.AddScoped<Clienta.Api.Repositories.ClientsRepository>();
builder.Services.AddScoped<Clienta.Api.Repositories.BusinessUsersRepository>();

builder.Services.AddMemoryCache();
builder.Services.AddScoped<ResetRateLimiter>();

// JWT
var tenantJwtSettings = builder.Configuration.GetSection("JwtSettings");
var tenantJwtKey = Encoding.UTF8.GetBytes(tenantJwtSettings["Key"]!);

var platformJwtSettings = builder.Configuration.GetSection("PlatformJwtSettings");
var platformJwtKey = Encoding.UTF8.GetBytes(platformJwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = PlatformAuthConstants.TenantScheme;
    options.DefaultChallengeScheme = PlatformAuthConstants.TenantScheme;
})
.AddJwtBearer(PlatformAuthConstants.TenantScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = tenantJwtSettings["Issuer"],
        ValidAudience = tenantJwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(tenantJwtKey)
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) &&
                (path.StartsWithSegments("/hubs/appointments") || path.StartsWithSegments("/hubs/team-chat")))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var hasTenantId = context.Principal?.HasClaim(c => c.Type == "tenant_id") == true;
            if (!hasTenantId)
            {
                context.Fail("Tenant JWT must include tenant_id claim.");
                return Task.CompletedTask;
            }

            var tenantIdClaim = context.Principal?.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            {
                context.Fail("Tenant JWT must include a valid tenant_id claim.");
                return Task.CompletedTask;
            }

            using var scope = context.HttpContext.RequestServices.CreateScope();
            var validator = scope.ServiceProvider.GetRequiredService<ITenantAccessValidator>();
            var isAllowed = validator.IsTenantAllowedAsync(tenantId, context.HttpContext.RequestAborted).GetAwaiter().GetResult();
            if (!isAllowed)
            {
                context.Fail("Tenant is suspended or unavailable.");
            }

            return Task.CompletedTask;
        }
    };
})
.AddJwtBearer(PlatformAuthConstants.PlatformScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = platformJwtSettings["Issuer"],
        ValidAudience = platformJwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(platformJwtKey)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var scope = context.Principal?.Claims.FirstOrDefault(c => c.Type == "token_scope")?.Value;
            if (!string.Equals(scope, "platform", StringComparison.Ordinal))
            {
                context.Fail("Platform JWT requires token_scope=platform.");
            }

            return Task.CompletedTask;
        }
    };
})
.AddScheme<AuthenticationSchemeOptions, ImagingGatewayAuthenticationHandler>(PlatformAuthConstants.ImagingGatewayScheme, _ =>
{
});

// Authorization handler
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

// Authorization policies (DB-based permissions)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("manage_clients",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_clients")));

    options.AddPolicy("manage_appointments",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_appointments")));

    options.AddPolicy("manage_notes",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_notes")));

    options.AddPolicy("manage_files",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_files")));

    options.AddPolicy("view_clients",
    policy => policy.Requirements.Add(new PermissionRequirement("view_clients")));
    
    options.AddPolicy("manage_staff",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_staff")));

    options.AddPolicy("manage_business_settings",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_business_settings")));

    options.AddPolicy("manage_invoices",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_invoices")));

    options.AddPolicy("manage_quotes",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_quotes")));

    options.AddPolicy("manage_whatsapp",
        policy => policy.Requirements.Add(new PermissionRequirement("manage_whatsapp")));

    options.AddPolicy(PlatformAuthConstants.PolicyOwner, policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.PlatformScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Owner");
    });

    options.AddPolicy(PlatformAuthConstants.PolicyAdminOrOwner, policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.PlatformScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Owner", "PlatformAdmin");
    });

    options.AddPolicy(PlatformAuthConstants.PolicySupportOrAbove, policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.PlatformScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Owner", "PlatformAdmin", "Support");
    });

    options.AddPolicy(PlatformAuthConstants.PolicyImagingGateway, policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.ImagingGatewayScheme);
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Clienta API", Version = "v1" });

    // ?? JWT Authentication
    options.AddSecurityDefinition("TenantBearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Tenant JWT: Bearer {token}"
    });

    options.AddSecurityDefinition("PlatformBearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Platform JWT: Bearer {token}"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "TenantBearer"
                }
            },
            new string[] {}
        },
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "PlatformBearer"
                }
            },
            new string[] {}
        }
    });
});

// SignalR
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactDev",
        policy =>
        {
            policy.WithOrigins(
                    "http://localhost:5173",
                    "http://localhost:5174",
                    "http://localhost:5175")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

// S3 and IFileStorage services
builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();
builder.Services.AddScoped<IFileStorage, S3FileStorage>();
builder.Services.AddSingleton<ITeamChatOnlineUsersStore, InMemoryTeamChatOnlineUsersStore>();

var app = builder.Build();


app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
});


// Ensure DB schema is up-to-date in every environment.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}



// ---------- DATABASE + SEED ----------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var db = services.GetRequiredService<AppDbContext>();

    var seeder = services.GetRequiredService<AuthSeedService>();
    await seeder.SeedAsync();

    // Seed drugs
    var drugSeeder = services.GetRequiredService<DrugSeedService>();
    await drugSeeder.SeedAsync();
}



app.UseCors("AllowReactDev");

app.UseRouting();

app.UseDefaultFiles();
app.UseStaticFiles();

//if (app.Environment.IsDevelopment())
//{
app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseAuthentication();

app.UseAuthorization();

// Tenant context AFTER authorization so scheme-specific principals are available.
app.UseMiddleware<TenantMiddleware>();

app.UseMiddleware<SubscriptionMiddleware>();

var forwardOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};

forwardOptions.KnownNetworks.Clear();
forwardOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardOptions);

app.MapControllers();


// SignalR Hubs
app.MapHub<AppointmentsHub>("/hubs/appointments");
app.MapHub<TeamChatHub>("/hubs/team-chat");
app.MapHub<QueueDisplayHub>("/hubs/queue-display");
app.MapFallbackToFile("index.html");

app.Run();

