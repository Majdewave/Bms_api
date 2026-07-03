using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPlanProvider _planProvider;

    public DashboardController(
        AppDbContext db,
        ITenantContext tenantContext,
        IPlanProvider planProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _planProvider = planProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        var dashboardService = HttpContext.RequestServices.GetService<DashboardService>();
        if (dashboardService == null)
            return StatusCode(500, new { error = "DashboardService not available" });

        var stats = await dashboardService.GetDashboardStatsAsync();

        return Ok(new
        {
            totalClients = stats.TotalClients,
            notDocumentedClientsCount = stats.NotDocumentedClientsCount,
            appointmentsToday = stats.AppointmentsToday,
            completedToday = stats.CompletedAppointmentsToday,
            noShowToday = stats.NoShowToday,
            upcomingAppointmentsList = stats.UpcomingAppointments
        });
    }

    [HttpGet("activity")]
    public async Task<IActionResult> GetRecentActivity()
    {
        var dashboardService = HttpContext.RequestServices.GetService<DashboardService>();
        if (dashboardService == null)
            return StatusCode(500, new { error = "DashboardService not available" });

        var activity = await dashboardService.GetRecentActivityAsync();
        return Ok(activity);
    }
}
