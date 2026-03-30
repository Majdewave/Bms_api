using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Authorization;
using Clienta.Api.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Clienta.Api.Services;
using Clienta.Api.Hubs;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
// Register DrugSeedService
builder.Services.AddScoped<DrugSeedService>();

// QuestPDF License
QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddScoped<TenantResolver>();
builder.Services.AddScoped<IFeatureService, FeatureService>();

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
 
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<Clienta.Api.Repositories.AppointmentsRepository>();
builder.Services.AddScoped<Clienta.Api.Repositories.ClientsRepository>();
builder.Services.AddScoped<Clienta.Api.Repositories.BusinessUsersRepository>();

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
builder.Services.AddSwaggerGen();

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

var app = builder.Build();


// ---------- TEMPORARY DATA FIX ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var tenant = db.Tenants.FirstOrDefault();

    if (tenant != null)
    {
        var clients = db.Clients
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == Guid.Empty)
            .ToList();

        foreach (var client in clients)
        {
            client.TenantId = tenant.Id;
        }

        db.SaveChanges();
    }
}


// ---------- DATABASE + SEED ----------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var db = services.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var seeder = services.GetRequiredService<AuthSeedService>();
    await seeder.SeedAsync();

    // Seed drugs
    var drugSeeder = services.GetRequiredService<DrugSeedService>();
    await drugSeeder.SeedAsync();
}


// ---------- MIDDLEWARE ----------

app.UseCors("AllowReactDev");

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();

// Tenant AFTER authentication
app.UseMiddleware<TenantMiddleware>();

app.UseAuthorization();

app.MapControllers();

// SignalR Hubs
app.MapHub<AppointmentsHub>("/hubs/appointments");

app.Run();