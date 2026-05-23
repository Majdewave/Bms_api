using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.Hubs;
using Clienta.Api.Middleware;
using Clienta.Api.Services;
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
builder.Services.AddScoped<TenantResolver>();
builder.Services.AddScoped<IFeatureService, FeatureService>();
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
builder.Services.AddScoped<AuthSeedService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<Clienta.Api.Services.TokenService>();
builder.Services.AddScoped<IEmailService, SendGridEmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOnboardingService, OnboardingService>();
builder.Services.AddScoped<ITenantSeedService, TenantSeedService>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<IPlanEnforcementService, PlanEnforcementService>();
builder.Services.AddSingleton<IPlanProvider, PlanProvider>();
builder.Services.AddHostedService<CleanupService>();

builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<Clienta.Api.Repositories.AppointmentsRepository>();
builder.Services.AddScoped<Clienta.Api.Repositories.ClientsRepository>();
builder.Services.AddScoped<Clienta.Api.Repositories.BusinessUsersRepository>();

builder.Services.AddMemoryCache();
builder.Services.AddScoped<ResetRateLimiter>();

// JWT
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
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/hubs/appointments"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
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
});

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Clienta API", Version = "v1" });

    // ?? JWT Authentication
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter JWT token like: Bearer {your token}"
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

var app = builder.Build();


app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
});


// ------------ migrate sqlight DB before everything 
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
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

// Tenant AFTER authentication
app.UseMiddleware<TenantMiddleware>();

app.UseMiddleware<SubscriptionMiddleware>();

app.UseAuthorization();

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
app.MapFallbackToFile("index.html");

app.Run();

