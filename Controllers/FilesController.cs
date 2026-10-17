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
    private readonly IClientFileService _files;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        AppDbContext context,
        ITenantContext tenant,
        IClientFileService files,
        ILogger<FilesController> logger)
    {
        _context = context;
        _tenant = tenant;
        _files = files;
        _logger = logger;
    }

    // GET /clients/{clientId}/files
    [Authorize(Policy = "manage_clients")]
    [HttpGet]
    public async Task<IActionResult> GetFiles(Guid clientId, [FromQuery] Guid? projectId = null)
    {
        var clientExists = await _context.Clients
            .AnyAsync(c => c.Id == clientId && c.TenantId == _tenant.TenantId);

        if (!clientExists)
            return NotFound("Client not found.");

        var query = _context.ClientFiles
            .Where(f => f.ClientId == clientId && f.TenantId == _tenant.TenantId);

        if (projectId.HasValue)
            query = query.Where(f => f.ProjectId == projectId.Value);

        return Ok(await _files.ListAsync(query, _tenant.TenantId));
    }

    // POST /clients/{clientId}/files
    [Authorize(Policy = "manage_clients")]
    [HttpPost]
    public async Task<IActionResult> UploadFile(Guid clientId, IFormFile file)
    {
        // Validate client
        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId && c.TenantId == _tenant.TenantId);

        if (client == null)
            return NotFound("Client not found.");

        var validationError = _files.ValidateUpload(file);
        if (validationError != null)
            return BadRequest(validationError);

        try
        {
            // Legacy endpoint always creates a general (non-project) client file.
            var clientFile = await _files.CreateAsync(_tenant.TenantId, clientId, null, _tenant.UserId!.Value, file!, null);
            var response = (await _files.ListAsync(
                _context.ClientFiles.Where(f => f.Id == clientFile.Id && f.TenantId == _tenant.TenantId), _tenant.TenantId)).Single();

            return CreatedAtAction(nameof(GetFiles), new { clientId }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client file upload failed for client {ClientId}", clientId);
            return StatusCode(500, "Error uploading file.");
        }
    }

    // GET /clients/{clientId}/files/{fileId}/download
    [Authorize(Policy = "manage_clients")]
    [HttpGet("{fileId}/download")]
    public async Task<IActionResult> DownloadFile(Guid clientId, Guid fileId)
    {
        var file = await _context.ClientFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.ClientId == clientId && f.TenantId == _tenant.TenantId);

        if (file == null)
            return NotFound("File not found.");

        var filePath = file.FilePath;

        if (!System.IO.File.Exists(filePath))
            return NotFound("File not found on disk.");

        var fileStream = System.IO.File.OpenRead(filePath);
        var contentType = _files.GetContentType(file.FileName);

        return File(fileStream, contentType, file.FileName);
    }

    // DELETE /clients/{clientId}/files/{fileId}
    [Authorize(Policy = "manage_clients")]
    [HttpDelete("{fileId}")]
    public async Task<IActionResult> DeleteFile(Guid clientId, Guid fileId)
    {
        var file = await _context.ClientFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.ClientId == clientId && f.TenantId == _tenant.TenantId);

        if (file == null)
            return NotFound("File not found.");

        try
        {
            await _files.DeleteAsync(file);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client file delete failed for file {FileId}", fileId);
            return StatusCode(500, "Error deleting file.");
        }
    }
}
