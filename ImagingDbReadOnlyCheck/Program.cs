using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using BCrypt.Net;
using Npgsql;

static string Usage() =>
    "Usage: dotnet run --project ImagingGatewayCredentialTool.csproj -- <TenantId> [Name] | --list | --delete <CredentialId>";

if (args.Length > 0 && string.Equals(args[0], "--delete", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2 || !Guid.TryParse(args[1], out var deleteCredentialId))
    {
        Console.Error.WriteLine("Usage: --delete <CredentialId>");
        return 1;
    }

    var deleteConnectionString = Environment.GetEnvironmentVariable("CLIENTA_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(deleteConnectionString))
    {
        var appSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json"));
        var appSettingsText = await File.ReadAllTextAsync(appSettingsPath);
        var match = Regex.Match(appSettingsText, "\"DefaultConnection\"\\s*:\\s*\"([^\"]+)\"");
        if (!match.Success)
        {
            Console.Error.WriteLine("DefaultConnection not found in appsettings.json.");
            return 1;
        }

        deleteConnectionString = match.Groups[1].Value;
    }

    await using var deleteConnection = new NpgsqlConnection(deleteConnectionString);
    await deleteConnection.OpenAsync();

    await using var deleteCmd = deleteConnection.CreateCommand();
    deleteCmd.CommandText = @"
DELETE FROM ""ImagingGatewayCredentials""
WHERE ""Id"" = @id;";
    deleteCmd.Parameters.AddWithValue("id", deleteCredentialId);

    var deleted = await deleteCmd.ExecuteNonQueryAsync();
    Console.WriteLine($"Deleted rows: {deleted}");
    return 0;
}

if (args.Length > 0 && string.Equals(args[0], "--list", StringComparison.OrdinalIgnoreCase))
{
    var listConnectionString = Environment.GetEnvironmentVariable("CLIENTA_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(listConnectionString))
    {
        var appSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json"));
        var appSettingsText = await File.ReadAllTextAsync(appSettingsPath);
        var match = Regex.Match(appSettingsText, "\"DefaultConnection\"\\s*:\\s*\"([^\"]+)\"");
        if (!match.Success)
        {
            Console.Error.WriteLine("DefaultConnection not found in appsettings.json.");
            return 1;
        }

        listConnectionString = match.Groups[1].Value;
    }

    await using var listConnection = new NpgsqlConnection(listConnectionString);
    await listConnection.OpenAsync();

    await using var list = listConnection.CreateCommand();
    list.CommandText = @"
SELECT ""Id""::text, ""TenantId""::text, ""KeyId"", ""Name"", ""IsActive"", ""CreatedAt""::text
FROM ""ImagingGatewayCredentials""
ORDER BY ""CreatedAt"" DESC;";
    await using var listReader = await list.ExecuteReaderAsync();

    Console.WriteLine("Id\tTenantId\tKeyId\tName\tIsActive\tCreatedAt");
    while (await listReader.ReadAsync())
    {
        Console.WriteLine(string.Join("\t", Enumerable.Range(0, listReader.FieldCount).Select(i => listReader.IsDBNull(i) ? "NULL" : listReader.GetValue(i).ToString())));
    }

    return 0;
}

if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]) || !Guid.TryParse(args[0], out var tenantId))
{
    Console.Error.WriteLine(Usage());
    return 1;
}

var credentialName = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
    ? args[1].Trim()
    : "Development Imaging Gateway";

var connectionString = Environment.GetEnvironmentVariable("CLIENTA_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var appSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json"));
    if (!File.Exists(appSettingsPath))
    {
        Console.Error.WriteLine($"Connection string not found. Set CLIENTA_CONNECTION_STRING or ensure {appSettingsPath} exists.");
        return 1;
    }

    var appSettingsText = await File.ReadAllTextAsync(appSettingsPath);
    var match = Regex.Match(appSettingsText, "\"DefaultConnection\"\\s*:\\s*\"([^\"]+)\"");
    if (!match.Success)
    {
        Console.Error.WriteLine("DefaultConnection not found in appsettings.json.");
        return 1;
    }

    connectionString = match.Groups[1].Value;
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Connection string is empty.");
    return 1;
}

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

var tenant = await LoadTenantAsync(connection, tenantId);
if (tenant == null)
{
    Console.Error.WriteLine($"Tenant not found: {tenantId}");
    return 1;
}

if (tenant.Value.IsSuspended)
{
    Console.Error.WriteLine($"Tenant is suspended: {tenantId}");
    return 1;
}

var keyId = await GenerateUniqueKeyIdAsync(connection);
var secret = GenerateBase36(32);
var rawKey = $"cl_img_{keyId}_{secret}";
var keyHash = BCrypt.Net.BCrypt.HashPassword(secret);
var credentialId = Guid.NewGuid();
var now = DateTime.UtcNow;

await using (var insert = connection.CreateCommand())
{
    insert.CommandText = @"
INSERT INTO ""ImagingGatewayCredentials"" (
    ""Id"", ""TenantId"", ""KeyId"", ""KeyHash"", ""Name"", ""IsActive"", ""CreatedAt"", ""LastUsedAt""
)
VALUES (
    @id, @tenantId, @keyId, @keyHash, @name, TRUE, @createdAt, NULL
);";
    insert.Parameters.AddWithValue("id", credentialId);
    insert.Parameters.AddWithValue("tenantId", tenantId);
    insert.Parameters.AddWithValue("keyId", keyId);
    insert.Parameters.AddWithValue("keyHash", keyHash);
    insert.Parameters.AddWithValue("name", credentialName);
    insert.Parameters.AddWithValue("createdAt", now);

    await insert.ExecuteNonQueryAsync();
}

await using var verify = connection.CreateCommand();
verify.CommandText = @"
SELECT
    ""Id""::text AS ""CredentialId"",
    ""TenantId""::text AS ""TenantId"",
    ""KeyId"" AS ""KeyId"",
    ""IsActive"" AS ""IsActive"",
    CASE WHEN ""KeyHash"" IS NOT NULL THEN 1 ELSE 0 END AS ""HasHash""
FROM ""ImagingGatewayCredentials""
WHERE ""TenantId"" = @tenantId AND ""KeyId"" = @keyId;";
verify.Parameters.AddWithValue("tenantId", tenantId);
verify.Parameters.AddWithValue("keyId", keyId);

await using var reader = await verify.ExecuteReaderAsync();
if (!await reader.ReadAsync())
{
    Console.Error.WriteLine("Verification query returned no rows.");
    return 1;
}

var returnedCredentialId = reader.GetString(0);
var returnedTenantId = reader.GetString(1);
var returnedKeyId = reader.GetString(2);
var isActive = reader.GetBoolean(3);
var hasHash = reader.GetInt32(4) == 1;

Console.WriteLine("Credential created successfully.");
Console.WriteLine($"CredentialId: {credentialId}");
Console.WriteLine($"TenantId: {tenantId}");
Console.WriteLine($"KeyId: {keyId}");
Console.WriteLine("GatewayKey: SHOW ONCE — STORE SECURELY");
Console.WriteLine(rawKey);
Console.WriteLine();
Console.WriteLine("Database verification:");
Console.WriteLine($"CredentialId matches: {string.Equals(returnedCredentialId, credentialId.ToString(), StringComparison.OrdinalIgnoreCase)}");
Console.WriteLine($"TenantId matches: {string.Equals(returnedTenantId, tenantId.ToString(), StringComparison.OrdinalIgnoreCase)}");
Console.WriteLine($"KeyId matches: {string.Equals(returnedKeyId, keyId, StringComparison.Ordinal)}");
Console.WriteLine($"IsActive = true: {isActive}");
Console.WriteLine($"KeyHash exists: {hasHash}");
Console.WriteLine("Raw key stored in database: false");

return 0;

static async Task<(Guid TenantId, bool IsSuspended)?> LoadTenantAsync(NpgsqlConnection connection, Guid tenantId)
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = @"
SELECT ""Id"", COALESCE(""IsSuspended"", FALSE)
FROM ""Tenants""
WHERE ""Id"" = @tenantId
LIMIT 1;";
    cmd.Parameters.AddWithValue("tenantId", tenantId);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return (reader.GetGuid(0), reader.GetBoolean(1));
}

static async Task<string> GenerateUniqueKeyIdAsync(NpgsqlConnection connection)
{
    for (var attempt = 0; attempt < 100; attempt++)
    {
        var candidate = GenerateBase36(8);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT EXISTS (
    SELECT 1 FROM ""ImagingGatewayCredentials"" WHERE ""KeyId"" = @keyId
);";
        cmd.Parameters.AddWithValue("keyId", candidate);

        var exists = (bool)(await cmd.ExecuteScalarAsync() ?? true);
        if (!exists)
        {
            return candidate;
        }
    }

    throw new InvalidOperationException("Could not generate a unique KeyId.");
}

static string GenerateBase36(int length)
{
    const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    var chars = new char[length];
    for (var i = 0; i < length; i++)
    {
        chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }

    return new string(chars);
}
