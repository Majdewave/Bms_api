using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Clienta.Api.Services;

public interface IClientFileService
{
    string? ValidateUpload(IFormFile? file);
    string? ValidateProposalUpload(IFormFile? file)
    {
        var validationError = ValidateUpload(file);
        if (validationError != null)
            return validationError;

        var extension = Path.GetExtension(file!.FileName).ToLowerInvariant();
        return extension is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx"
            ? null
            : "Project proposal file type not allowed.";
    }
    bool TryNormalizeTitle(string? raw, out string? title);

    Task<ClientFile> CreateAsync(
        Guid tenantId,
        Guid clientId,
        Guid? projectId,
        Guid userId,
        IFormFile file,
        string? title);

    Task<ClientFile> CreateProjectFileAsync(
        Guid tenantId,
        Guid clientId,
        Guid projectId,
        Guid userId,
        IFormFile file,
        string? title) => CreateAsync(tenantId, clientId, projectId, userId, file, title);

    Task<ClientFile> CreateProposalAsync(
        Guid tenantId,
        Guid clientId,
        Guid projectId,
        Guid userId,
        IFormFile file,
        string title) => CreateProjectFileAsync(tenantId, clientId, projectId, userId, file, title);

    Task<ClientFile> CreateExternalPdfAsync(
        Guid tenantId,
        Guid clientId,
        Guid? projectId,
        Guid userId,
        Guid externalAccountingDocumentId,
        Stream pdfStream,
        long fileSize,
        string fileName,
        string title,
        CancellationToken cancellationToken = default);

    Task<List<FileResponse>> ListAsync(
        IQueryable<ClientFile> query,
        Guid tenantId);

    Task<Stream?> OpenReadAsync(
        ClientFile file,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(ClientFile file);

    string GetContentType(string fileName);
}

// Shared ClientFile metadata/storage logic. Legacy and client-scoped uploads remain local;
// new project-scoped files and durable external accounting PDFs are stored in S3.
public class ClientFileService : IClientFileService
{
    public const long MaxFileSize = 10 * 1024 * 1024;
    public const int MaxTitleLength = 200;

    private const string S3StorageProvider = "S3";

    private static readonly string[] AllowedExtensions =
    {
        ".pdf",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".txt",
        ".jpg",
        ".jpeg",
        ".png"
    };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IFileStorage _fileStorage;

    public ClientFileService(
        AppDbContext db,
        IWebHostEnvironment env,
        IFileStorage fileStorage)
    {
        _db = db;
        _env = env;
        _fileStorage = fileStorage;
    }

    public string? ValidateUpload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return "No file provided.";

        if (file.Length > MaxFileSize)
            return $"File size exceeds {MaxFileSize / (1024 * 1024)} MB limit.";

        if (!AllowedExtensions.Contains(
                Path.GetExtension(file.FileName).ToLowerInvariant()))
            return "File type not allowed.";

        return null;
    }

    public bool TryNormalizeTitle(string? raw, out string? title)
    {
        title = string.IsNullOrWhiteSpace(raw)
            ? null
            : raw.Trim();

        return title == null || title.Length <= MaxTitleLength;
    }

    // Client-scoped uploads and legacy callers retain their original local-disk behavior.
    public Task<ClientFile> CreateAsync(
        Guid tenantId,
        Guid clientId,
        Guid? projectId,
        Guid userId,
        IFormFile file,
        string? title) => CreateLocalAsync(tenantId, clientId, projectId, userId, file, title);

    public Task<ClientFile> CreateProjectFileAsync(
        Guid tenantId,
        Guid clientId,
        Guid projectId,
        Guid userId,
        IFormFile file,
        string? title) => CreateProjectS3Async(tenantId, clientId, projectId, userId, file, title, isProposal: false);

    public Task<ClientFile> CreateProposalAsync(
        Guid tenantId,
        Guid clientId,
        Guid projectId,
        Guid userId,
        IFormFile file,
        string title) => CreateProjectS3Async(tenantId, clientId, projectId, userId, file, title, isProposal: true);

    private async Task<ClientFile> CreateLocalAsync(
        Guid tenantId,
        Guid clientId,
        Guid? projectId,
        Guid userId,
        IFormFile file,
        string? title)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        var uploadDir = Path.Combine(
            _env.ContentRootPath,
            "uploads",
            tenantId.ToString(),
            clientId.ToString());

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

            // NULL explicitly means legacy/local storage.
            StorageProvider = null,
            StorageKey = null,

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
            if (File.Exists(filePath))
                File.Delete(filePath);

            throw;
        }

        return clientFile;
    }

    private async Task<ClientFile> CreateProjectS3Async(
        Guid tenantId,
        Guid clientId,
        Guid projectId,
        Guid userId,
        IFormFile file,
        string? title,
        bool isProposal)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var projectPrefix = $"projects/{tenantId:N}/{projectId:N}/{clientId:N}/";
        var storageKey = $"{projectPrefix}{(isProposal ? "proposals/" : string.Empty)}{storedFileName}";
        var clientFile = new ClientFile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            ProjectId = projectId,
            Title = title,
            FileName = file.FileName,
            StoredFileName = storedFileName,
            // Existing non-null schema field is unused for S3-backed records.
            FilePath = string.Empty,
            StorageProvider = S3StorageProvider,
            StorageKey = storageKey,
            FileSize = file.Length,
            UploadedByUserId = userId,
            UploadedAt = DateTime.UtcNow
        };

        await using var uploadStream = file.OpenReadStream();
        await _fileStorage.UploadAsync(uploadStream, storageKey, GetContentType(file.FileName));

        try
        {
            _db.ClientFiles.Add(clientFile);
            await _db.SaveChangesAsync();
            return clientFile;
        }
        catch
        {
            _db.Entry(clientFile).State = EntityState.Detached;
            try
            {
                await _fileStorage.DeleteAsync(storageKey);
            }
            catch
            {
                // Preserve the original persistence failure; an orphan can be reconciled operationally.
            }

            throw;
        }
    }

    // Used only for durable archived copies of external accounting PDFs.
    // The caller must already have validated that the stream contains
    // the trusted official PDF.
    public async Task<ClientFile> CreateExternalPdfAsync(
        Guid tenantId,
        Guid clientId,
        Guid? projectId,
        Guid userId,
        Guid externalAccountingDocumentId,
        Stream pdfStream,
        long fileSize,
        string fileName,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (fileSize <= 0 || fileSize > MaxFileSize)
            throw new InvalidOperationException(
                $"PDF size must be between 1 byte and {MaxFileSize} bytes.");

        if (!string.Equals(
                Path.GetExtension(fileName),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "External accounting archive must be a PDF.");
        }

        if (!TryNormalizeTitle(title, out var normalizedTitle) ||
            normalizedTitle == null)
        {
            throw new InvalidOperationException(
                $"Title is required and must be at most {MaxTitleLength} characters.");
        }

        // Idempotency: one archived ClientFile per external accounting document.
        var existing = await _db.ClientFiles
            .FirstOrDefaultAsync(
                f => f.TenantId == tenantId &&
                     f.ExternalAccountingDocumentId == externalAccountingDocumentId,
                cancellationToken);

        if (existing != null)
            return existing;

        var storedFileName = $"{Guid.NewGuid()}.pdf";

        // We create the key ourselves and persist the key, not the public URL
        // returned by the existing IFileStorage.UploadAsync contract.
        var storageKey =
            $"accounting/{tenantId:N}/{clientId:N}/{externalAccountingDocumentId:N}/{storedFileName}";

        if (pdfStream.CanSeek)
            pdfStream.Position = 0;

        await _fileStorage.UploadAsync(
            pdfStream,
            storageKey,
            "application/pdf");

        var clientFile = new ClientFile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            ProjectId = projectId,

            ExternalAccountingDocumentId = externalAccountingDocumentId,

            Title = normalizedTitle,
            FileName = fileName,
            StoredFileName = storedFileName,

            // FilePath remains required by the existing schema.
            // S3-backed files are identified exclusively by
            // StorageProvider + StorageKey.
            FilePath = string.Empty,
            StorageProvider = S3StorageProvider,
            StorageKey = storageKey,

            FileSize = fileSize,
            UploadedByUserId = userId,
            UploadedAt = DateTime.UtcNow
        };

        try
        {
            _db.ClientFiles.Add(clientFile);
            await _db.SaveChangesAsync(cancellationToken);
            return clientFile;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_ClientFiles_Tenant_ExternalAccountingDocument"
            })
        {
            _db.Entry(clientFile).State = EntityState.Detached;

            try
            {
                await _fileStorage.DeleteAsync(storageKey);
            }
            catch
            {
                // Cleanup failure must not hide the database conflict.
            }

            var winner = await _db.ClientFiles
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    f => f.TenantId == tenantId &&
                         f.ExternalAccountingDocumentId == externalAccountingDocumentId,
                    cancellationToken);

            if (winner != null)
                return winner;

            throw;
        }
        catch
        {
            _db.Entry(clientFile).State = EntityState.Detached;

            try
            {
                await _fileStorage.DeleteAsync(storageKey);
            }
            catch
            {
                // Preserve the original exception.
            }

            throw;
        }
    }

    public async Task<List<FileResponse>> ListAsync(
        IQueryable<ClientFile> query,
        Guid tenantId)
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
                ProjectName = f.Project != null
                    ? f.Project.Name
                    : null,
                f.UploadedByUserId,
                f.ExternalAccountingDocumentId
            })
            .ToListAsync();

        var userIds = rows
            .Select(r => r.UploadedByUserId)
            .Distinct()
            .ToArray();

        var users = userIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.Users
                .AsNoTracking()
                .Where(u =>
                    u.TenantId == tenantId &&
                    userIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    Name = u.FullName ?? u.Email
                })
                .ToDictionaryAsync(
                    u => u.Id,
                    u => u.Name);

        return rows
            .Select(r => new FileResponse(
                r.Id,
                r.FileName,
                r.FileSize,
                r.UploadedAt,
                r.Title,
                r.ProjectId,
                r.ProjectName,
                users.TryGetValue(
                    r.UploadedByUserId,
                    out var name)
                        ? name
                        : null,
                r.ExternalAccountingDocumentId))
            .ToList();
    }

    public async Task<Stream?> OpenReadAsync(
        ClientFile file,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
                file.StorageProvider,
                S3StorageProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(file.StorageKey))
                return null;

            return await _fileStorage.OpenReadAsync(
                file.StorageKey,
                cancellationToken);
        }

        // Existing/local ClientFile behavior.
        if (string.IsNullOrWhiteSpace(file.FilePath) ||
            !File.Exists(file.FilePath))
        {
            return null;
        }

        return File.OpenRead(file.FilePath);
    }

    public async Task DeleteAsync(ClientFile file)
    {
        if (string.Equals(
                file.StorageProvider,
                S3StorageProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(file.StorageKey))
                await _fileStorage.DeleteAsync(file.StorageKey);
        }
        else
        {
            // Existing/local ClientFile behavior.
            if (!string.IsNullOrWhiteSpace(file.FilePath) &&
                File.Exists(file.FilePath))
            {
                File.Delete(file.FilePath);
            }
        }

        _db.ClientFiles.Remove(file);
        await _db.SaveChangesAsync();
    }

    public string GetContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
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