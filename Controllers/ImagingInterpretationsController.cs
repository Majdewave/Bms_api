using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging")]
[Authorize(Policy = "manage_appointments")]
public class ImagingInterpretationsController : ControllerBase
{

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IEmailService _emailService;
    public ImagingInterpretationsController(
        AppDbContext context,
        ITenantContext tenant,
        IEmailService emailService)
    {
        _context = context;
        _tenant = tenant;
        _emailService = emailService;
    }

    [HttpGet("interpreters")]
    public async Task<IActionResult> GetInterpreters(CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized();

        var interpreters = await _context.Users
            .AsNoTracking()
            .Where(user =>
                user.TenantId == _tenant.TenantId &&
                user.IsActive &&
                user.Role == "Interpreter")
            .OrderBy(user => user.FullName)
            .Select(user => new
            {
                userId = user.Id,
                fullName = user.FullName ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        return Ok(interpreters);
    }
    [HttpPost("orders/{imagingOrderId:guid}/interpretation-request")]
    public async Task<IActionResult> Create(
        Guid imagingOrderId,
        CreateInterpretationRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty || _tenant.UserId == null)
            return Unauthorized();

        var order = await _context.ImagingOrders
            .FirstOrDefaultAsync(order => order.Id == imagingOrderId && order.TenantId == _tenant.TenantId, cancellationToken);
        if (order == null)
            return NotFound();

        var interpreter = await _context.Users
            .FirstOrDefaultAsync(user =>
                user.Id == request.AssignedInterpreterId &&
                user.TenantId == _tenant.TenantId &&
                user.IsActive &&
                user.Role == "Interpreter", cancellationToken);
        if (interpreter == null)
            return NotFound();

        var study = await _context.ImagingStudies
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == request.ImagingStudyId &&
                candidate.TenantId == _tenant.TenantId &&
                candidate.ImagingOrderId == order.Id, cancellationToken);
        if (study == null)
            return NotFound();

        if (await _context.InterpretationRequests.AnyAsync(existing => existing.ImagingOrderId == order.Id, cancellationToken))
            return Conflict(new { error = "An interpretation request already exists for this imaging order." });

        var interpretationRequest = new InterpretationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ImagingOrderId = order.Id,
            ImagingStudyId = study.Id,
            AssignedInterpreterId = interpreter.Id,
            RequestedByUserId = _tenant.UserId.Value,
            Status = InterpretationRequestStatuses.Pending,
            RequestedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.InterpretationRequests.Add(interpretationRequest);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "An interpretation request already exists for this imaging order." });
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(interpreter.Email))
            {
                const string portalUrl = "https://clienta.digitalpenpro.com/interpreter/requests";

                var subject = "בדיקת אולטרסאונד חדשה ממתינה לפענוח";

                var body = $@"
            <div dir='rtl' style='font-family:Arial,sans-serif;line-height:1.6'>
                <h2>בדיקה חדשה הוקצתה לך לפענוח</h2>

                <p>שלום {System.Net.WebUtility.HtmlEncode(interpreter.FullName)},</p>

                <p>
                    בדיקת אולטרסאונד חדשה הוקצתה לך לפענוח במערכת Clienta.
                </p>

                <p>
                    לצפייה בבדיקה ולשליחת הפענוח יש להיכנס לפורטל המפענחים.
                </p>

                <p style='margin-top:24px'>
                    <a href='{portalUrl}'
                       style='background:#2563eb;color:#ffffff;padding:10px 18px;
                              text-decoration:none;border-radius:6px;display:inline-block'>
                        כניסה לפורטל הפענוח
                    </a>
                </p>

                <p style='margin-top:24px;font-size:12px;color:#64748b'>
                    מטעמי פרטיות, פרטי המטופל והבדיקה אינם מוצגים בהודעת הדוא״ל.
                </p>
            </div>";

                await _emailService.SendEmailAsync(
                    interpreter.Email,
                    subject,
                    body);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Failed to send interpretation assignment email. " +
                $"RequestId={interpretationRequest.Id}. Error={exception.Message}");
        }


        return CreatedAtAction(nameof(Get), new { imagingOrderId }, ToDto(interpretationRequest, interpreter.FullName));
    }

    [HttpGet("orders/{imagingOrderId:guid}/interpretation-request")]
    public async Task<IActionResult> Get(Guid imagingOrderId, CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized();

        var interpretationRequest = await QueryRequests()
            .Where(request => request.ImagingOrderId == imagingOrderId)
            .Select(request => new InterpretationRequestDto(
                request.Id,
                request.ImagingOrderId,
                request.ImagingStudyId,
                request.AssignedInterpreterId,
                request.AssignedInterpreter.FullName ?? string.Empty,
                _context.InterpretationReports
                    .Where(report =>
                        report.TenantId == _tenant.TenantId &&
                        report.InterpretationRequestId == request.Id)
                    .Select(report => new InterpretationReportDto(
                        report.Id,
                        report.Content,
                        report.CreatedAt,
                        report.UpdatedAt))
                    .FirstOrDefault(),
                request.Status,
                request.RequestedAt,
                request.RequestedByUserId,
                request.StartedAt,
                request.CompletedAt,
                request.CreatedAt,
                request.UpdatedAt,
                _context.InterpretationReportDocuments.Any(document =>
                    document.TenantId == _tenant.TenantId &&
                    document.InterpretationRequestId == request.Id &&
                    document.Version == 1 &&
                    !document.IsDeleted)))
            .FirstOrDefaultAsync(cancellationToken);

        return interpretationRequest == null ? NotFound() : Ok(interpretationRequest);
    }

    [HttpDelete("orders/{imagingOrderId:guid}/interpretation-request")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteInterpretationRequest(
    Guid imagingOrderId,
    CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized();

        var interpretationRequest = await _context.InterpretationRequests
            .FirstOrDefaultAsync(
                request =>
                    request.ImagingOrderId == imagingOrderId &&
                    request.TenantId == _tenant.TenantId,
                cancellationToken);

        if (interpretationRequest == null)
            return NotFound();

        var reportDocuments = await _context.InterpretationReportDocuments
            .Where(document =>
                document.InterpretationRequestId == interpretationRequest.Id &&
                document.TenantId == _tenant.TenantId)
            .ToListAsync(cancellationToken);

        if (reportDocuments.Count > 0)
            _context.InterpretationReportDocuments.RemoveRange(reportDocuments);

        var reports = await _context.InterpretationReports
            .Where(report =>
                report.InterpretationRequestId == interpretationRequest.Id &&
                report.TenantId == _tenant.TenantId)
            .ToListAsync(cancellationToken);

        if (reports.Count > 0)
            _context.InterpretationReports.RemoveRange(reports);

        _context.InterpretationRequests.Remove(interpretationRequest);

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }


    [HttpGet("interpretation-requests/{requestId:guid}/report/pdf")]
    public async Task<IActionResult> GetReportPdf(
        Guid requestId,
        [FromQuery] bool download,
        CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized();

        var document = await _context.InterpretationRequests
            .AsNoTracking()
            .Where(request =>
                request.Id == requestId &&
                request.TenantId == _tenant.TenantId &&
                request.ImagingOrder.TenantId == _tenant.TenantId)
            .SelectMany(request => _context.InterpretationReportDocuments
                .Where(candidate =>
                    candidate.TenantId == _tenant.TenantId &&
                    candidate.InterpretationRequestId == request.Id &&
                    candidate.Version == 1 &&
                    !candidate.IsDeleted))
            .Select(candidate => new
            {
                candidate.Content,
                candidate.ContentType,
                candidate.FileName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document == null)
            return NotFound();

        if (!download)
            Response.Headers.ContentDisposition = $"inline; filename=\"{document.FileName}\"";

        return download
            ? File(document.Content, document.ContentType, document.FileName, enableRangeProcessing: true)
            : File(document.Content, document.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("interpretation-requests")]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized();

        var requests = await QueryRequests()
            .OrderByDescending(request => request.RequestedAt)
            .Select(request => new InterpretationRequestListItemDto(
                request.Id,
                request.ImagingOrderId,
                request.ImagingStudyId,
                request.ImagingOrder.AccessionNumber,
                request.ImagingOrder.Modality,
                request.ImagingOrder.ClientId,
                request.ImagingOrder.Client.FullName,
                request.AssignedInterpreterId,
                request.AssignedInterpreter.FullName ?? string.Empty,
                request.Status,
                request.RequestedAt,
                request.StartedAt,
                request.CompletedAt))
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    private IQueryable<InterpretationRequest> QueryRequests() => _context.InterpretationRequests
        .AsNoTracking()
        .Where(request => request.TenantId == _tenant.TenantId);

    private static InterpretationRequestDto ToDto(InterpretationRequest request, string? assignedInterpreterName) => new(
        request.Id,
        request.ImagingOrderId,
        request.ImagingStudyId,
        request.AssignedInterpreterId,
        assignedInterpreterName ?? string.Empty,
        null,
        request.Status,
        request.RequestedAt,
        request.RequestedByUserId,
        request.StartedAt,
        request.CompletedAt,
        request.CreatedAt,
        request.UpdatedAt,
        false);
}
