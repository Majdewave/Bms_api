using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging/orders/{imagingOrderId}/referral")]
[Authorize(Policy = "manage_appointments")]
public class ImagingOrderReferralsController : ControllerBase
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public ImagingOrderReferralsController(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid imagingOrderId)
    {
        var order = await FindOrderAsync(imagingOrderId);
        if (order == null) return NotFound();

        var document = await FindReferralDocumentAsync(order.Id);
        return Ok(ToDto(order, document));
    }

    [HttpPut]
    public async Task<IActionResult> Update(Guid imagingOrderId, UpdateImagingOrderReferralRequest request)
    {
        var order = await FindOrderAsync(imagingOrderId);
        if (order == null) return NotFound();

        order.ReferringDoctorName = NormalizeDoctorName(request.ReferringDoctorName);
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(ToDto(order, await FindReferralDocumentAsync(order.Id)));
    }

    [HttpPost("document")]
    public async Task<IActionResult> UploadDocument(Guid imagingOrderId, IFormFile file)
    {
        var order = await FindOrderAsync(imagingOrderId);
        if (order == null) return NotFound();
        if (_tenant.UserId == null) return Unauthorized();
        if (file == null || file.Length == 0) return BadRequest("No file provided.");
        if (file.Length > MaxFileSize) return BadRequest("File size exceeds 5 MB limit.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension)) return BadRequest("File type not allowed.");

        byte[] fileData;

        try
        {
            fileData = await ReadFileDataAsync(file);
        }
        catch (InvalidOperationException)
        {
            return BadRequest("File size exceeds 5 MB limit.");
        }
        catch
        {
            return StatusCode(500, "Error reading referral document.");
        }

        ImagingOrderDocument? document = null;
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var existingDocument = await FindReferralDocumentAsync(order.Id);

                if (existingDocument != null)
                {
                    existingDocument.IsDeleted = true;
                    existingDocument.DeletedAt = DateTime.UtcNow;
                    existingDocument.DeletedByUserId = _tenant.UserId.Value;
                }

                document = new ImagingOrderDocument
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenant.TenantId,
                    ClientId = order.ClientId,
                    ImagingOrderId = order.Id,
                    DocumentType = ImagingOrderDocumentTypes.Referral,
                    OriginalFileName = Path.GetFileName(file.FileName),
                    ContentType = GetContentType(extension),
                    FileSize = fileData.LongLength,
                    FileData = fileData,
                    UploadedByUserId = _tenant.UserId.Value,
                    CreatedAt = DateTime.UtcNow,
                };

                _context.ImagingOrderDocuments.Add(document);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            });
        }
        catch
        {
            _context.ChangeTracker.Clear();
            return StatusCode(500, "Error uploading referral document.");
        }

        return Ok(ToDocumentDto(document!));
    }

    [HttpGet("document")]
    public async Task<IActionResult> DownloadDocument(Guid imagingOrderId)
    {
        var order = await FindOrderAsync(imagingOrderId);
        if (order == null) return NotFound();

        var document = await FindReferralDocumentAsync(order.Id);
        if (document == null) return NotFound();

        return File(document.FileData, document.ContentType, document.OriginalFileName);
    }

    [HttpDelete("document")]
    public async Task<IActionResult> DeleteDocument(Guid imagingOrderId)
    {
        var order = await FindOrderAsync(imagingOrderId);
        if (order == null) return NotFound();
        if (_tenant.UserId == null) return Unauthorized();

        var document = await FindReferralDocumentAsync(order.Id);
        if (document == null) return NotFound();

        document.IsDeleted = true;
        document.DeletedAt = DateTime.UtcNow;
        document.DeletedByUserId = _tenant.UserId.Value;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private Task<ImagingOrder?> FindOrderAsync(Guid imagingOrderId) =>
        _context.ImagingOrders.FirstOrDefaultAsync(order => order.Id == imagingOrderId && order.TenantId == _tenant.TenantId);

    private Task<ImagingOrderDocument?> FindReferralDocumentAsync(Guid imagingOrderId) =>
        _context.ImagingOrderDocuments.FirstOrDefaultAsync(document =>
            document.TenantId == _tenant.TenantId &&
            document.ImagingOrderId == imagingOrderId &&
            document.DocumentType == ImagingOrderDocumentTypes.Referral &&
            !document.IsDeleted);

    private static async Task<byte[]> ReadFileDataAsync(IFormFile file)
    {
        await using var input = file.OpenReadStream();
        await using var output = new MemoryStream((int)file.Length);
        var buffer = new byte[81920];
        long totalBytesRead = 0;

        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer);
            if (bytesRead == 0) break;

            totalBytesRead += bytesRead;
            if (totalBytesRead > MaxFileSize)
            {
                throw new InvalidOperationException("File size exceeds the allowed limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead));
        }

        return output.ToArray();
    }

    private static string? NormalizeDoctorName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string GetContentType(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    private static ImagingOrderReferralDocumentDto ToDocumentDto(ImagingOrderDocument document) =>
        new(document.Id, document.DocumentType, document.OriginalFileName, document.ContentType, document.FileSize, document.CreatedAt);

    private static ImagingOrderReferralDto ToDto(ImagingOrder order, ImagingOrderDocument? document) =>
        new(order.Id, order.ReferringDoctorName, document == null ? null : ToDocumentDto(document));
}