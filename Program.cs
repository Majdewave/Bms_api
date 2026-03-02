using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Authorization;
using Clienta.Api.Middleware;
using Clienta.Api.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddScoped<TenantResolver>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddDbContext<MasterDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<AuthSeedService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOnboardingService, OnboardingService>();
builder.Services.AddScoped<ITenantSeedService, TenantSeedService>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<IPlanEnforcementService, PlanEnforcementService>();
builder.Services.AddSingleton<IPlanProvider, PlanProvider>();

// Background Services
// builder.Services.AddHostedService<TrialReminderService>();
// builder.Services.AddHostedService<GracePeriodService>();

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

builder.Services.AddAuthorization(options =>
{
    // Simple role-based policies - check JWT role claim directly
    options.AddPolicy("manage_clients",
        policy => policy.RequireRole("Admin"));

    options.AddPolicy("manage_appointments",
        policy => policy.RequireRole("Admin"));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter JWT Bearer token"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactDev",
        policy =>
        {
            policy.WithOrigins("http://localhost:5173", "http://localhost:5174", "http://localhost:5175")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

var app = builder.Build();

// SEED TENANT AND ADMIN USER ON STARTUP
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<AuthSeedService>();
    seeder.SeedAsync().GetAwaiter().GetResult();
}

app.UseCors("AllowReactDev");

// Configure static files for uploads
app.UseCors("AllowReactDev");

// Configure static files for uploads
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Clienta API v1");
        c.RoutePrefix = string.Empty;
    });
}

app.MapGet("/health", () => new { 
    message = "Clienta API is running", 
    version = "1.0",
    endpoints = new[] {
        "POST /auth/login",
        "GET /api/clients",
        "POST /api/clients"
    }
});

// Stripe Webhook (must be BEFORE authentication middleware)
// app.MapPost("/stripe/webhook", async (HttpRequest request, IStripeService stripeService) =>
// {
//     request.EnableBuffering();
//     string json;
//     using (var reader = new StreamReader(request.Body, leaveOpen: true))
//     {
//         json = await reader.ReadToEndAsync();
//     }
//     request.Body.Position = 0;
//     var signature = request.Headers["Stripe-Signature"].ToString();
//
//     try
//     {
//         await stripeService.HandleWebhookAsync(json, signature);
//         return Results.Ok();
//     }
//     catch (Exception ex)
//     {
//         return Results.BadRequest(new { error = ex.Message });
//     }
// });


app.UseAuthentication();

app.UseMiddleware<TenantMiddleware>(); 

app.UseAuthorization();

app.MapControllers();

app.Run();
