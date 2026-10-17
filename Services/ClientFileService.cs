using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IClientFileService
{
    string? ValidateUpload(IFormFile? file);
    bool TryNormalizeTitle(string? raw, out string? title);
    Task<ClientFile> CreateAsync(Guid tenantId, Guid clientId, Guid? projectId, Guid userId, IFormFile file, string? title);
    Task<List<FileResponse>> ListAsync(IQueryable<ClientFile> query, Guid tenantId);
    Task DeleteAsync(ClientFile file);
    string GetContentType(string fileName);
}

// Shared local-disk storage + metadata logic for ClientFiles (general and project-linked).
public class ClientFileService : IClientFileService
{
    public const long MaxFileSize = 10 * 1024 * 1024;
    public const int MaxTitleLength = 200;
    private static readonly string[] AllowedExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".jpg", ".jpeg", ".png" };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ClientFileService(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public string? ValidateUpload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return "No file provided.";
        if (file.Length > MaxFileSize)
            return $"File size exceeds {MaxFileSize / (1024 * 1024)} MB limit.";
        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            return "File type not allowed.";
        return null;
    }

    public bool TryNormalizeTitle(string? raw, out string? title)
    {
        title = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        return title == null || title.Length <= MaxTitleLength;
    }

    public async Task<ClientFile> CreateAsync(Guid tenantId, Guid clientId, Guid? projectId, Guid userId, IFormFile file, string? title)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var uploadDir = Path.Combine(_env.ContentRootPath, "uploads", tenantId.ToString(), clientId.ToString());
        Directory.CreateDirectory(uploadDir);

        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadDir, storedFileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var clientFile = new ClientFile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            ProjectId = projectId,
            Title = title,
            FileName = file.FileName,
            StoredFileName = storedFileName,
            FilePath = filePath,
            FileSize = file.Length,
            UploadedByUserId = userId,
            UploadedAt = DateTime.UtcNow
        };

        try
        {
            _db.ClientFiles.Add(clientFile);
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            throw;
        }

        return clientFile;
    }

    public async Task<List<FileResponse>> ListAsync(IQueryable<ClientFile> query, Guid tenantId)
    {
        var rows = await query
            .OrderByDescending(f => f.UploadedAt)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.FileSize,
                f.UploadedAt,
                f.Title,
                f.ProjectId,
                ProjectName = f.Project != null ? f.Project.Name : null,
                f.UploadedByUserId
            })
            .ToListAsync();

        var userIds = rows.Select(r => r.UploadedByUserId).Distinct().ToArray();
        var users = userIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FullName ?? u.Email })
                .ToDictionaryAsync(u => u.Id, u => u.Name);

        return rows.Select(r => new FileResponse(
            r.Id, r.FileName, r.FileSize, r.UploadedAt, r.Title, r.ProjectId, r.ProjectName,
            users.TryGetValue(r.UploadedByUserId, out var name) ? name : null)).ToList();
    }

    public async Task DeleteAsync(ClientFile file)
    {
        if (File.Exists(file.FilePath))
            File.Delete(file.FilePath);

        _db.ClientFiles.Remove(file);
        await _db.SaveChangesAsync();
    }

    public string GetContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
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
