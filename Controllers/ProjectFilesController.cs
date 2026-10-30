using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

// PM Phase 7A: project-scoped view over the existing ClientFiles table/storage.
[ApiController]
[Route("api/projects/{projectId:guid}/files")]
[Authorize(Policy = "manage_projects")]
public class ProjectFilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPmAccessService _pmAccess;
    private readonly IClientFileService _files;
    private readonly ILogger<ProjectFilesController> _logger;

    public ProjectFilesController(
        AppDbContext db,
        ITenantContext tenant,
        IPmAccessService pmAccess,
        IClientFileService files,
        ILogger<ProjectFilesController> logger)
    {
        _db = db;
        _tenant = tenant;
        _pmAccess = pmAccess;
        _files = files;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid projectId)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;

        var proposalKeyPrefix = ProposalStorageKeyPrefix(project!);
        var query = ProjectFiles(project!)
            .Where(file => file.StorageKey == null || !file.StorageKey.StartsWith(proposalKeyPrefix));
        return Ok(await _files.ListAsync(query, _tenant.TenantId));
    }

    [HttpGet("proposal")]
    public async Task<IActionResult> ListProposals(Guid projectId)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;
        var businessError = await EnsureBusinessTenantAsync();
        if (businessError != null) return businessError;

        var proposalKeyPrefix = ProposalStorageKeyPrefix(project!);
        var proposals = await _files.ListAsync(
            ProjectFiles(project!).Where(file => file.StorageKey != null && file.StorageKey.StartsWith(proposalKeyPrefix)),
            _tenant.TenantId);
        return Ok(proposals);
    }

    [HttpPost]
    public async Task<IActionResult> Upload(Guid projectId, IFormFile file, [FromForm] string? title)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;

        var validationError = _files.ValidateUpload(file);
        if (validationError != null) return BadRequest(validationError);

        if (!_files.TryNormalizeTitle(title, out var normalizedTitle))
            return BadRequest($"Title must be at most {ClientFileService.MaxTitleLength} characters.");

        if (_tenant.UserId is not Guid userId)
            return Unauthorized();

        try
        {
            var created = await _files.CreateProjectFileAsync(_tenant.TenantId, project!.ClientId, project.Id, userId, file, normalizedTitle);
            var response = (await _files.ListAsync(ProjectFiles(project).Where(f => f.Id == created.Id), _tenant.TenantId)).Single();
            return CreatedAtAction(nameof(List), new { projectId }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project file upload failed for project {ProjectId}", projectId);
            return StatusCode(500, "Error uploading file.");
        }
    }

    [HttpPost("proposal")]
    public async Task<IActionResult> UploadProposal(Guid projectId, IFormFile file, [FromForm] string? title)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;
        var businessError = await EnsureBusinessTenantAsync();
        if (businessError != null) return businessError;

        var validationError = _files.ValidateProposalUpload(file);
        if (validationError != null) return BadRequest(validationError);

        if (string.IsNullOrWhiteSpace(title) || !_files.TryNormalizeTitle(title, out var normalizedTitle) || normalizedTitle == null)
            return BadRequest("A proposal title is required and must be at most 200 characters.");

        if (_tenant.UserId is not Guid userId)
            return Unauthorized();

        try
        {
            var created = await _files.CreateProposalAsync(_tenant.TenantId, project!.ClientId, project.Id, userId, file, normalizedTitle);
            var response = (await _files.ListAsync(ProjectFiles(project).Where(f => f.Id == created.Id), _tenant.TenantId)).Single();
            return CreatedAtAction(nameof(ListProposals), new { projectId }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project proposal upload failed for project {ProjectId}", projectId);
            return StatusCode(500, "Error uploading project proposal.");
        }
    }

    [HttpPatch("proposal/{fileId:guid}")]
    public async Task<IActionResult> UpdateProposalTitle(Guid projectId, Guid fileId, [FromBody] UpdateFileTitleRequest request)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;
        var businessError = await EnsureBusinessTenantAsync();
        if (businessError != null) return businessError;

        if (string.IsNullOrWhiteSpace(request.Title) || !_files.TryNormalizeTitle(request.Title, out var normalizedTitle) || normalizedTitle == null)
            return BadRequest("A proposal title is required and must be at most 200 characters.");

        var proposalKeyPrefix = ProposalStorageKeyPrefix(project!);
        var file = await ProjectFiles(project!)
            .FirstOrDefaultAsync(candidate => candidate.Id == fileId && candidate.StorageKey != null && candidate.StorageKey.StartsWith(proposalKeyPrefix));
        if (file == null) return NotFound("Project proposal not found.");

        file.Title = normalizedTitle;
        await _db.SaveChangesAsync();

        var response = (await _files.ListAsync(ProjectFiles(project!).Where(candidate => candidate.Id == fileId), _tenant.TenantId)).Single();
        return Ok(response);
    }

    [HttpGet("proposal/{fileId:guid}/view")]
    public async Task<IActionResult> ViewProposal(Guid projectId, Guid fileId, CancellationToken cancellationToken)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;
        var businessError = await EnsureBusinessTenantAsync();
        if (businessError != null) return businessError;

        var proposalKeyPrefix = ProposalStorageKeyPrefix(project!);
        var file = await ProjectFiles(project!)
            .FirstOrDefaultAsync(candidate => candidate.Id == fileId && candidate.StorageKey != null && candidate.StorageKey.StartsWith(proposalKeyPrefix), cancellationToken);
        if (file == null) return NotFound("Project proposal not found.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF project proposals can be viewed in the browser.");

        var stream = await _files.OpenReadAsync(file, cancellationToken);
        return stream == null ? NotFound("Project proposal file not found.") : File(stream, "application/pdf");
    }

    [HttpGet("{fileId:guid}/download")]
    public async Task<IActionResult> Download(Guid projectId, Guid fileId)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;

        var file = await ProjectFiles(project!)
            .FirstOrDefaultAsync(f => f.Id == fileId);

        if (file == null)
            return NotFound("File not found.");

        var fileStream = await _files.OpenReadAsync(
            file,
            HttpContext.RequestAborted);

        if (fileStream == null)
            return NotFound("File not found.");

        return File(
            fileStream,
            _files.GetContentType(file.FileName),
            file.FileName);
    }

    [HttpPatch("{fileId:guid}")]
    public async Task<IActionResult> UpdateTitle(Guid projectId, Guid fileId, [FromBody] UpdateFileTitleRequest request)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;

        if (!_files.TryNormalizeTitle(request.Title, out var normalizedTitle))
            return BadRequest($"Title must be at most {ClientFileService.MaxTitleLength} characters.");

        var file = await ProjectFiles(project!).FirstOrDefaultAsync(f => f.Id == fileId);
        if (file == null) return NotFound("File not found.");
        if (file.StorageKey?.StartsWith(ProposalStorageKeyPrefix(project!), StringComparison.Ordinal) == true)
            return NotFound("File not found.");

        file.Title = normalizedTitle;
        await _db.SaveChangesAsync();

        var response = (await _files.ListAsync(ProjectFiles(project!).Where(f => f.Id == fileId), _tenant.TenantId)).Single();
        return Ok(response);
    }

    [HttpDelete("{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid fileId)
    {
        var (project, error) = await ResolveProjectAsync(projectId);
        if (error != null) return error;

        var file = await ProjectFiles(project!).FirstOrDefaultAsync(f => f.Id == fileId);
        if (file == null) return NotFound("File not found.");

        try
        {
            await _files.DeleteAsync(file);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project file delete failed for file {FileId}", fileId);
            return StatusCode(500, "Error deleting file.");
        }
    }

    // Every query is bound to tenant + client + project; a file id alone never grants access.
    private IQueryable<ClientFile> ProjectFiles(Project project) =>
        _db.ClientFiles.Where(f =>
            f.TenantId == _tenant.TenantId &&
            f.ClientId == project.ClientId &&
            f.ProjectId == project.Id);

    private string ProposalStorageKeyPrefix(Project project) =>
        $"projects/{_tenant.TenantId:N}/{project.Id:N}/{project.ClientId:N}/proposals/";

    private async Task<IActionResult?> EnsureBusinessTenantAsync()
    {
        var productType = await _db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == _tenant.TenantId)
            .Select(tenant => tenant.ProductType)
            .FirstOrDefaultAsync();
        return productType == TenantProductType.Business
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, "Project proposals are available for Business tenants only.");
    }

    private async Task<(Project? Project, IActionResult? Error)> ResolveProjectAsync(Guid projectId)
    {
        if (_tenant.TenantId == Guid.Empty)
            return (null, Unauthorized("Tenant not resolved"));

        if (!await _pmAccess.IsPmEnabledAsync(_tenant.TenantId))
            return (null, StatusCode(StatusCodes.Status403Forbidden, "PM feature is not enabled for this tenant."));

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == _tenant.TenantId);
        if (project == null)
            return (null, NotFound("Project not found."));

        var clientOk = await _db.Clients.AnyAsync(c => c.Id == project.ClientId && c.TenantId == _tenant.TenantId);
        return clientOk ? (project, null) : (null, NotFound("Project not found."));
    }
}
