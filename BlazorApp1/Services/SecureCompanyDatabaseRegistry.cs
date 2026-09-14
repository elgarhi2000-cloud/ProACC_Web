using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace BlazorApp1.Services;

public sealed record CompanyRegistryResult(bool Success, string Message);

public interface ISecureCompanyDatabaseRegistry
{
    IReadOnlyList<CompanyDatabaseDefinition> GetAll();
    Task<CompanyRegistryResult> UpsertAsync(
        CompanyDatabaseDefinition definition,
        string? originalKey = null,
        CancellationToken cancellationToken = default);
    Task<CompanyRegistryResult> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists the company registry as one encrypted payload. Database links and
/// login codes never appear as clear text in appsettings or the stored file.
/// </summary>
public sealed class SecureCompanyDatabaseRegistry : ISecureCompanyDatabaseRegistry
{
    private const string ProtectorPurpose = "BlazorApp1.CompanyDatabaseRegistry.v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly IDataProtector protector;
    private readonly IConfiguration configuration;
    private readonly ILogger<SecureCompanyDatabaseRegistry> logger;
    private readonly string storePath;
    private readonly SemaphoreSlim fileLock = new(1, 1);

    public SecureCompanyDatabaseRegistry(
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<SecureCompanyDatabaseRegistry> logger)
    {
        protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        this.configuration = configuration;
        this.logger = logger;
        storePath = Path.Combine(environment.ContentRootPath, "App_Data", "company-databases.secure.dat");
    }

    public IReadOnlyList<CompanyDatabaseDefinition> GetAll()
    {
        fileLock.Wait();
        try
        {
            return LoadCore().Select(Clone).ToList();
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task<CompanyRegistryResult> UpsertAsync(
        CompanyDatabaseDefinition definition,
        string? originalKey = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(definition);

        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var records = LoadCore();
            var lookupKey = string.IsNullOrWhiteSpace(originalKey) ? normalized.Key : originalKey.Trim();
            var existingIndex = records.FindIndex(x => string.Equals(x.Key, lookupKey, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(normalized.LoginCode))
            {
                if (normalized.LoginCode.Length < 8) return new(false, "رمز المنشأة يجب ألا يقل عن 8 محارف.");
                normalized.LoginCodeHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(normalized.LoginCode))).ToLowerInvariant();
                normalized.LoginCode = string.Empty;
            }
            else if (existingIndex >= 0)
            {
                normalized.LoginCodeHash = records[existingIndex].LoginCodeHash;
            }

            var validation = Validate(normalized);
            if (validation is not null) return new(false, validation);

            if (records.Any(x => !string.Equals(x.Key, lookupKey, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(x.Key, normalized.Key, StringComparison.OrdinalIgnoreCase)))
            {
                return new(false, "المعرف الداخلي مستخدم لمنشأة أخرى.");
            }

            if (records.Any(x => !string.Equals(x.Key, lookupKey, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(x.DatabaseName, normalized.DatabaseName, StringComparison.OrdinalIgnoreCase)))
            {
                return new(false, "قاعدة البيانات مرتبطة بمنشأة أخرى بالفعل.");
            }

            if (records.Any(x => !string.Equals(x.Key, lookupKey, StringComparison.OrdinalIgnoreCase)
                                 && FixedTimeHashEquals(x.LoginCodeHash, normalized.LoginCodeHash)))
            {
                return new(false, "رمز المنشأة مستخدم بالفعل.");
            }

            if (existingIndex >= 0) records[existingIndex] = normalized;
            else records.Add(normalized);

            await SaveCoreAsync(records, cancellationToken);
            return new(true, existingIndex >= 0 ? "تم تحديث ربط المنشأة بأمان." : "تمت إضافة وربط المنشأة بأمان.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return new(false, $"تعذر حفظ سجل المنشآت المشفر: {ex.Message}");
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task<CompanyRegistryResult> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var records = LoadCore();
            var removed = records.RemoveAll(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                return new(false, "لم يتم العثور على ربط المنشأة.");
            }

            await SaveCoreAsync(records, cancellationToken);
            return new(true, "تم حذف الربط من التطبيق فقط؛ لم تُحذف قاعدة SQL.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return new(false, $"تعذر حذف الربط: {ex.Message}");
        }
        finally
        {
            fileLock.Release();
        }
    }

    private List<CompanyDatabaseDefinition> LoadCore()
    {
        if (!File.Exists(storePath))
        {
            return SeedAndPersist();
        }

        var protectedPayload = File.ReadAllText(storePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(protectedPayload)) return [];

        try
        {
            var json = protector.Unprotect(protectedPayload);
            return JsonSerializer.Deserialize<List<CompanyDatabaseDefinition>>(json, JsonOptions) ?? [];
        }
        catch (CryptographicException ex)
        {
            HandleUnreadablePayload(ex);
            var seeded = SeedAndPersist();
            return seeded;
        }
    }

    private async Task SaveCoreAsync(List<CompanyDatabaseDefinition> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        var json = JsonSerializer.Serialize(records, JsonOptions);
        var payload = protector.Protect(json);
        var temporaryPath = $"{storePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, payload, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, storePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void SaveCore(List<CompanyDatabaseDefinition> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        var json = JsonSerializer.Serialize(records, JsonOptions);
        File.WriteAllText(storePath, protector.Protect(json), new UTF8Encoding(false));
    }

    private static CompanyDatabaseDefinition Normalize(CompanyDatabaseDefinition source)
    {
        var databaseName = source.DatabaseName?.Trim() ?? string.Empty;
        return new CompanyDatabaseDefinition
        {
            Key = string.IsNullOrWhiteSpace(source.Key) ? CreateKey(databaseName) : source.Key.Trim(),
            DatabaseName = databaseName,
            DisplayName = string.IsNullOrWhiteSpace(source.DisplayName) ? databaseName : source.DisplayName.Trim(),
            LoginCode = source.LoginCode?.Trim() ?? string.Empty,
            LoginCodeHash = source.LoginCodeHash?.Trim() ?? string.Empty,
            ConnectionString = source.ConnectionString?.Trim() ?? string.Empty,
            Enabled = source.Enabled
        };
    }

    private static string? Validate(CompanyDatabaseDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.DatabaseName)) return "اسم قاعدة البيانات مطلوب.";
        if (string.IsNullOrWhiteSpace(definition.DisplayName)) return "اسم المنشأة مطلوب.";
        if (string.IsNullOrWhiteSpace(definition.LoginCodeHash)) return "رمز المنشأة مطلوب.";
        return null;
    }

    private static bool FixedTimeHashEquals(string left, string right)
    {
        byte[] leftHash;
        byte[] rightHash;
        try { leftHash = Convert.FromHexString(left); rightHash = Convert.FromHexString(right); }
        catch (FormatException) { return false; }
        return leftHash.Length == rightHash.Length && CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static string CreateKey(string databaseName)
    {
        var clean = new string(databaseName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(clean) ? Guid.NewGuid().ToString("N") : clean;
    }

    private static CompanyDatabaseDefinition Clone(CompanyDatabaseDefinition source)
        => new()
        {
            Key = source.Key,
            DatabaseName = source.DatabaseName,
            DisplayName = source.DisplayName,
            LoginCode = source.LoginCode,
            LoginCodeHash = source.LoginCodeHash,
            ConnectionString = source.ConnectionString,
            Enabled = source.Enabled
        };

    private List<CompanyDatabaseDefinition> SeedAndPersist()
    {
        var seed = configuration.GetSection("CompanyDatabases:Databases")
            .Get<List<CompanyDatabaseDefinition>>() ?? [];
        var normalizedSeed = seed.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.DatabaseName))
            .Select(Normalize)
            .Where(x => Validate(x) is null)
            .ToList();
        var baseConnection = configuration.GetConnectionString("CustomUserConnection");
        if (!string.IsNullOrWhiteSpace(baseConnection))
        {
            foreach (var record in normalizedSeed.Where(x => string.IsNullOrWhiteSpace(x.ConnectionString)))
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = record.DatabaseName };
                    record.ConnectionString = builder.ConnectionString;
                }
                catch (ArgumentException)
                {
                    // Keep the shared-link fallback if the bootstrap link is currently invalid.
                }
            }
        }

        try
        {
            SaveCore(normalizedSeed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            logger.LogWarning(ex, "Failed to persist bootstrap company registry.");
        }

        return normalizedSeed;
    }

    private void HandleUnreadablePayload(CryptographicException ex)
    {
        logger.LogWarning(ex, "Unable to decrypt company registry file. Rebuilding from configuration after backup.");
        try
        {
            var backupPath = $"{storePath}.corrupt.{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.bak";
            File.Copy(storePath, backupPath, overwrite: true);
            logger.LogInformation("Corrupted registry payload backed up to {BackupPath}", backupPath);
        }
        catch (Exception backupEx)
        {
            logger.LogWarning(backupEx, "Unable to back up corrupted company registry payload.");
        }
    }
}
