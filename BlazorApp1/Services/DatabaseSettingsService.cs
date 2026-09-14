using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace BlazorApp1.Services;

public sealed record DatabaseConnectionInfo(
    string ConnectionString,
    string Server,
    string Database,
    string UserName,
    bool UsesIntegratedSecurity);

public sealed record DatabaseConnectionTestResult(
    bool IsConnected,
    string Message,
    string? ServerVersion = null,
    DateTimeOffset? CheckedAt = null);

public interface IDatabaseSettingsService
{
    bool VerifyDeveloperPassword(string password);
    DatabaseConnectionInfo GetCurrentConnection();
    Task<DatabaseConnectionTestResult> TestConnectionAsync(string connectionString, CancellationToken cancellationToken = default);
    Task<DatabaseConnectionTestResult> SaveConnectionAsync(string connectionString, CancellationToken cancellationToken = default);
}

public sealed class DatabaseSettingsService(
    IConfiguration configuration,
    IWebHostEnvironment environment) : IDatabaseSettingsService
{
    private const string ConnectionName = "CustomUserConnection";
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public bool VerifyDeveloperPassword(string password)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
        var configuredHash = configuration["SystemAdmin:PasswordHash"];
        if (string.IsNullOrWhiteSpace(configuredHash) || configuredHash.Length != 64
            || !configuredHash.All(Uri.IsHexDigit)) return false;
        var expectedHash = Convert.FromHexString(configuredHash);
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }

    public DatabaseConnectionInfo GetCurrentConnection()
    {
        var connectionString = ReadConnectionStringFromFile()
            ?? configuration.GetConnectionString(ConnectionName)
            ?? string.Empty;

        return ParseConnectionInfo(connectionString);
    }

    public async Task<DatabaseConnectionTestResult> TestConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Failed("رابط الاتصال مطلوب.");
        }

        try
        {
            _ = new SqlConnectionStringBuilder(connectionString);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);

            return new DatabaseConnectionTestResult(
                true,
                $"تم الاتصال بنجاح بقاعدة البيانات {connection.Database} على الخادم {connection.DataSource}.",
                connection.ServerVersion,
                DateTimeOffset.Now);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("انتهت مهلة الاتصال. تحقق من عنوان الخادم وإعدادات الشبكة.");
        }
        catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException)
        {
            return Failed($"تعذر الاتصال: {ex.Message}");
        }
    }

    public async Task<DatabaseConnectionTestResult> SaveConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var testResult = await TestConnectionAsync(connectionString, cancellationToken);
        if (!testResult.IsConnected)
        {
            return testResult with { Message = $"لم يتم الحفظ. {testResult.Message}" };
        }

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var settingsPath = GetSettingsPath();
            var json = await File.ReadAllTextAsync(settingsPath, cancellationToken);
            var root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("ملف إعدادات المشروع غير صالح.");
            var connections = root["ConnectionStrings"] as JsonObject ?? new JsonObject();
            root["ConnectionStrings"] = connections;
            connections[ConnectionName] = connectionString.Trim();

            var temporaryPath = Path.Combine(
                Path.GetDirectoryName(settingsPath)!,
                $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false),
                    cancellationToken);
                File.Move(temporaryPath, settingsPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            return testResult with
            {
                Message = "تم اختبار الاتصال وحفظ الرابط في المشروع. أعد تشغيل المشروع لتعمل جميع الشاشات على قاعدة البيانات الجديدة."
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Failed($"نجح الاتصال، لكن تعذر حفظ ملف الإعدادات: {ex.Message}");
        }
        finally
        {
            FileLock.Release();
        }
    }

    private string? ReadConnectionStringFromFile()
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(GetSettingsPath()));
            return root?["ConnectionStrings"]?[ConnectionName]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string GetSettingsPath() => Path.Combine(environment.ContentRootPath, "appsettings.json");

    private static DatabaseConnectionInfo ParseConnectionInfo(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return new DatabaseConnectionInfo(
                connectionString,
                builder.DataSource,
                builder.InitialCatalog,
                builder.IntegratedSecurity ? "مصادقة Windows" : builder.UserID,
                builder.IntegratedSecurity);
        }
        catch (ArgumentException)
        {
            return new DatabaseConnectionInfo(connectionString, string.Empty, string.Empty, string.Empty, false);
        }
    }

    private static DatabaseConnectionTestResult Failed(string message)
        => new(false, message, CheckedAt: DateTimeOffset.Now);
}
