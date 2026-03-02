using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Entities;
using Clienta.Api.DTOs;

namespace Clienta.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/clients/{clientId}/files")]
public class FilesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _env;
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public FilesController(
        AppDbContext context,
        ITenantContext tenant,
        IWebHostEnvironment env)
    {
        _context = context;
        _tenant = tenant;
        _env = env;
    }

    // GET /clients/{clientId}/files
    [Authorize(Policy = "manage_clients")]
    [HttpGet]
    public async Task<IActionResult> GetFiles(Guid clientId)
    {
        var clientExists = await _context.Clients
            .AnyAsync(c => c.Id == clientId);

        if (!clientExists)
            return NotFound("Client not found.");

        var files = await _context.ClientFiles
            .Where(f => f.ClientId == clientId)
            .OrderByDescending(f => f.UploadedAt)
            .Select(f => new FileResponse(
                f.Id,
                f.FileName,
                f.FileSize,
                f.UploadedAt
            ))
            .ToListAsync();

        return Ok(files);
    }

    // POST /clients/{clientId}/files
    [Authorize(Policy = "manage_clients")]
    [HttpPost]
    public async Task<IActionResult> UploadFile(Guid clientId, IFormFile file)
    {
        // Validate client
        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId);

        if (client == null)
            return NotFound("Client not found.");

        // Validate file
        if (file == null || file.Length == 0)
            return BadRequest("No file provided.");

        if (file.Length > MaxFileSize)
            return BadRequest($"File size exceeds {MaxFileSize / (1024 * 1024)} MB limit.");

        var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".jpg", ".jpeg", ".png" };
        var fileExtension = Path.GetExtension(file.FileName).ToLower();

        if (!allowedExtensions.Contains(fileExtension))
            return BadRequest("File type not allowed.");

        try
        {
            // Create directory structure
            var uploadDir = Path.Combine(_env.ContentRootPath, "uploads", 
                _tenant.TenantId.ToString(), clientId.ToString());
            
            Directory.CreateDirectory(uploadDir);

            // Generate unique filename
            var storedFileName = $"{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(uploadDir, storedFileName);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Create metadata record
            var clientFile = new ClientFile
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                ClientId = clientId,
                FileName = file.FileName,
                StoredFileName = storedFileName,
                FilePath = filePath,
                FileSize = file.Length,
                UploadedByUserId = _tenant.UserId!.Value,
                UploadedAt = DateTime.UtcNow
            };

            _context.ClientFiles.Add(clientFile);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetFiles), new { clientId }, 
                new FileResponse(clientFile.Id, clientFile.FileName, clientFile.FileSize, clientFile.UploadedAt));
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error uploading file: {ex.Message}");
        }
    }

    // GET /clients/{clientId}/files/{fileId}/download
    [Authorize(Policy = "manage_clients")]
    [HttpGet("{fileId}/download")]
    public async Task<IActionResult> DownloadFile(Guid clientId, Guid fileId)
    {
        var file = await _context.ClientFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.ClientId == clientId);

        if (file == null)
            return NotFound("File not found.");

        var filePath = file.FilePath;

        if (!System.IO.File.Exists(filePath))
            return NotFound("File not found on disk.");

        var fileStream = System.IO.File.OpenRead(filePath);
        var contentType = GetContentType(file.FileName);

        return File(fileStream, contentType, file.FileName);
    }

    // DELETE /clients/{clientId}/files/{fileId}
    [Authorize(Policy = "manage_clients")]
    [HttpDelete("{fileId}")]
    public async Task<IActionResult> DeleteFile(Guid clientId, Guid fileId)
    {
        var file = await _context.ClientFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.ClientId == clientId);

        if (file == null)
            return NotFound("File not found.");

        try
        {
            // Delete file from disk
            if (System.IO.File.Exists(file.FilePath))
            {
                System.IO.File.Delete(file.FilePath);
            }

            // Delete metadata
            _context.ClientFiles.Remove(file);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error deleting file: {ex.Message}");
        }
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLower();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };
    }
}
