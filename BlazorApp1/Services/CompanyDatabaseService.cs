using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BlazorApp1.Services;

public sealed class CompanyDatabaseDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    [JsonIgnore]
    public string LoginCode { get; set; } = string.Empty;
    public string LoginCodeHash { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public interface ICompanyDatabaseService
{
    IReadOnlyList<CompanyDatabaseDefinition> GetAvailableDatabases();
    Task<IReadOnlyList<CompanyDatabaseDefinition>> GetAvailableDatabasesAsync(CancellationToken cancellationToken = default);
    CompanyDatabaseDefinition? FindByLoginCode(string? loginCode);
    CompanyDatabaseDefinition GetRequiredDatabase(string? key);
    string BuildConnectionString(string? key);
}

/// <summary>
/// Keeps the list of databases that the application is allowed to access and
/// builds tenant-specific connection strings without duplicating SQL credentials.
/// </summary>
public sealed class CompanyDatabaseService(
    IConfiguration configuration,
    ILogger<CompanyDatabaseService> logger,
    ISecureCompanyDatabaseRegistry registry) : ICompanyDatabaseService
{
    private const string DatabasesSection = "CompanyDatabases:Databases";
    private const string BaseConnectionName = "CustomUserConnection";
    private readonly SemaphoreSlim discoveryLock = new(1, 1);
    private IReadOnlyList<CompanyDatabaseDefinition>? discoveredDatabases;
    private DateTimeOffset discoveryExpiresAt;

    public IReadOnlyList<CompanyDatabaseDefinition> GetAvailableDatabases()
    {
        var databases = GetConfiguredDatabases();

        if (discoveredDatabases is { Count: > 0 })
        {
            return Merge(databases, discoveredDatabases);
        }

        if (databases.Count > 0)
        {
            return databases;
        }

        return GetFallbackDatabase();
    }

    public async Task<IReadOnlyList<CompanyDatabaseDefinition>> GetAvailableDatabasesAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = GetConfiguredDatabases();
        if (!configuration.GetValue("CompanyDatabases:AutoDiscover", false))
        {
            return configured.Count > 0 ? configured : GetFallbackDatabase();
        }

        if (discoveredDatabases is not null && discoveryExpiresAt > DateTimeOffset.UtcNow)
        {
            return Merge(configured, discoveredDatabases);
        }

        await discoveryLock.WaitAsync(cancellationToken);
        try
        {
            if (discoveredDatabases is null || discoveryExpiresAt <= DateTimeOffset.UtcNow)
            {
                discoveredDatabases = await DiscoverCompatibleDatabasesAsync(cancellationToken);
                discoveryExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not discover compatible company databases.");
            discoveredDatabases ??= [];
            discoveryExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30);
        }
        finally
        {
            discoveryLock.Release();
        }

        var result = Merge(configured, discoveredDatabases);
        return result.Count > 0 ? result : GetFallbackDatabase();
    }

    private IReadOnlyList<CompanyDatabaseDefinition> GetConfiguredDatabases()
    {
        return registry.GetAll()
            .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.DatabaseName))
            .Select(Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private IReadOnlyList<CompanyDatabaseDefinition> GetFallbackDatabase()
    {

        var baseConnection = GetBaseConnectionString();
        var builder = new SqlConnectionStringBuilder(baseConnection);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            throw new InvalidOperationException(
                "لم يتم تعريف أي قاعدة منشأة، كما أن رابط الاتصال الأساسي لا يحتوي على اسم قاعدة بيانات.");
        }

        return
        [
            new CompanyDatabaseDefinition
            {
                Key = builder.InitialCatalog,
                DatabaseName = builder.InitialCatalog,
                DisplayName = builder.InitialCatalog,
                LoginCode = string.Empty,
                Enabled = true
            }
        ];
    }

    public CompanyDatabaseDefinition GetRequiredDatabase(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("يجب اختيار المنشأة أولًا.");
        }

        return GetAvailableDatabases()
            .FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("قاعدة المنشأة المختارة غير مسموح بها أو تم تعطيلها.");
    }

    public CompanyDatabaseDefinition? FindByLoginCode(string? loginCode)
    {
        if (string.IsNullOrWhiteSpace(loginCode))
        {
            return null;
        }

        var trimmedLoginCode = loginCode.Trim();
        var inputHash = SHA256.HashData(Encoding.UTF8.GetBytes(trimmedLoginCode));
        var inputHashBase64 = Convert.ToBase64String(inputHash);
        foreach (var database in GetConfiguredDatabases())
        {
            if (!string.IsNullOrWhiteSpace(database.LoginCode)
                && string.Equals(database.LoginCode, trimmedLoginCode, StringComparison.Ordinal))
            {
                return database;
            }

            if (string.Equals(database.LoginCodeHash, inputHashBase64, StringComparison.OrdinalIgnoreCase))
            {
                return database;
            }

            byte[] configuredHash;
            try { configuredHash = Convert.FromHexString(database.LoginCodeHash); }
            catch (FormatException) { continue; }
            if (configuredHash.Length == inputHash.Length
                && CryptographicOperations.FixedTimeEquals(inputHash, configuredHash))
            {
                return database;
            }
        }

        return null;
    }

    public string BuildConnectionString(string? key)
    {
        var database = GetRequiredDatabase(key);
        var sourceConnection = string.IsNullOrWhiteSpace(database.ConnectionString)
            ? GetBaseConnectionString()
            : database.ConnectionString;
        var builder = new SqlConnectionStringBuilder(sourceConnection)
        {
            InitialCatalog = database.DatabaseName,
            MultipleActiveResultSets = true
        };

        return builder.ConnectionString;
    }

    private string GetBaseConnectionString()
        => configuration.GetConnectionString(BaseConnectionName)
           ?? throw new InvalidOperationException(
               $"Connection string '{BaseConnectionName}' not found.");

    private async Task<IReadOnlyList<CompanyDatabaseDefinition>> DiscoverCompatibleDatabasesAsync(
        CancellationToken cancellationToken)
    {
        var baseBuilder = new SqlConnectionStringBuilder(GetBaseConnectionString())
        {
            InitialCatalog = "master"
        };

        var databaseNames = new List<string>();
        await using (var connection = new SqlConnection(baseBuilder.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT [name]
                FROM sys.databases
                WHERE database_id > 4
                  AND [state] = 0
                  AND HAS_DBACCESS([name]) = 1
                ORDER BY [name]
                """;
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                databaseNames.Add(reader.GetString(0));
            }
        }

        var result = new List<CompanyDatabaseDefinition>();
        foreach (var databaseName in databaseNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var candidateBuilder = new SqlConnectionStringBuilder(GetBaseConnectionString())
                {
                    InitialCatalog = databaseName,
                    ConnectTimeout = 5
                };
                await using var connection = new SqlConnection(candidateBuilder.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                const string schemaSql = """
                    SELECT CASE WHEN
                        OBJECT_ID(N'dbo.COMPANY', N'U') IS NOT NULL AND
                        OBJECT_ID(N'dbo.Periods', N'U') IS NOT NULL AND
                        OBJECT_ID(N'dbo.[USER]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'dbo.ACC', N'U') IS NOT NULL AND
                        OBJECT_ID(N'dbo.GL', N'U') IS NOT NULL AND
                        OBJECT_ID(N'dbo.TRANS', N'U') IS NOT NULL
                    THEN 1 ELSE 0 END
                    """;
                await using var command = new SqlCommand(schemaSql, connection);
                var isCompatible = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
                if (isCompatible)
                {
                    result.Add(new CompanyDatabaseDefinition
                    {
                        Key = databaseName,
                        DatabaseName = databaseName,
                        DisplayName = databaseName,
                        LoginCode = string.Empty,
                        LoginCodeHash = string.Empty,
                        ConnectionString = string.Empty,
                        Enabled = true
                    });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Skipped inaccessible database {DatabaseName}.", databaseName);
            }
        }

        return result;
    }

    private static IReadOnlyList<CompanyDatabaseDefinition> Merge(
        IReadOnlyList<CompanyDatabaseDefinition> configured,
        IReadOnlyList<CompanyDatabaseDefinition> discovered)
        => configured
            .Concat(discovered)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static CompanyDatabaseDefinition Normalize(CompanyDatabaseDefinition source)
    {
        var databaseName = source.DatabaseName.Trim();
        var key = string.IsNullOrWhiteSpace(source.Key) ? databaseName : source.Key.Trim();
        var displayName = string.IsNullOrWhiteSpace(source.DisplayName)
            ? databaseName
            : source.DisplayName.Trim();

        return new CompanyDatabaseDefinition
        {
            Key = key,
            DatabaseName = databaseName,
            DisplayName = displayName,
            LoginCode = source.LoginCode.Trim(),
            LoginCodeHash = source.LoginCodeHash?.Trim() ?? string.Empty,
            ConnectionString = source.ConnectionString?.Trim() ?? string.Empty,
            Enabled = source.Enabled
        };
    }
}
