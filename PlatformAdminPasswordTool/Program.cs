using System.Text.RegularExpressions;
using Npgsql;

var appSettingsPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json")
);

var appSettingsText = await File.ReadAllTextAsync(appSettingsPath);

var match = Regex.Match(
    appSettingsText,
    "\"DefaultConnection\"\\s*:\\s*\"([^\"]+)\""
);

if (!match.Success)
{
    Console.Error.WriteLine("DefaultConnection not found.");
    return 1;
}

var connectionString = match.Groups[1].Value;

const string email = "mjd.salman@gmail.com";

Console.Write("Enter NEW Platform Owner password: ");
var password = ReadPassword();

Console.WriteLine();

if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
{
    Console.Error.WriteLine("Password must contain at least 8 characters.");
    return 1;
}

var hash = BCrypt.Net.BCrypt.HashPassword(password);

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

await using var command = connection.CreateCommand();

command.CommandText = @"
UPDATE ""PlatformUsers""
SET
    ""PasswordHash"" = @hash,
    ""UpdatedAt"" = NOW(),
""PasswordResetToken"" = NULL,
""PasswordResetExpiresAt"" = NULL
WHERE LOWER(""Email"") = LOWER(@email)
  AND ""Role"" = 'Owner'
  AND ""IsActive"" = TRUE;
";

command.Parameters.AddWithValue("hash", hash);
command.Parameters.AddWithValue("email", email);

var affected = await command.ExecuteNonQueryAsync();

if (affected != 1)
{
    Console.Error.WriteLine($"RESET FAILED. Rows affected: {affected}");
    return 1;
}

Console.WriteLine("======================================");
Console.WriteLine("PASSWORD RESET SUCCESS");
Console.WriteLine($"Account: {email}");
Console.WriteLine("Rows affected: 1");
Console.WriteLine("======================================");

return 0;

static string ReadPassword()
{
    var password = new System.Text.StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
            break;

        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
                password.Length--;

            continue;
        }

        password.Append(key.KeyChar);
    }

    return password.ToString();
}