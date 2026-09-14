using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace BlazorApp1.Services;

public sealed record CompanyDatabaseLinkTestResult(
    bool Success,
    string Message,
    string? Server = null,
    string? Database = null,
    string? CompanyName = null,
    string? ServerVersion = null);

public interface ICompanyDatabaseAdminService
{
    IReadOnlyList<CompanyDatabaseDefinition> GetRecords();
    Task<IReadOnlyList<string>> DiscoverServerDatabasesAsync(CancellationToken cancellationToken = default);
    Task<CompanyDatabaseLinkTestResult> TestAsync(CompanyDatabaseDefinition definition, CancellationToken cancellationToken = default);
    Task<CompanyRegistryResult> SaveAsync(CompanyDatabaseDefinition definition, string? originalKey, CancellationToken cancellationToken = default);
    Task<CompanyRegistryResult> DeleteAsync(string key, CancellationToken cancellationToken = default);
    string GenerateLoginCode();
}

public sealed class CompanyDatabaseAdminService(
    ISecureCompanyDatabaseRegistry registry,
    IDatabaseSettingsService databaseSettingsService,
    ISystemAdminSession adminSession,
    ISystemAdminAuditService auditService) : ICompanyDatabaseAdminService
{
    public IReadOnlyList<CompanyDatabaseDefinition> GetRecords()
    {
        adminSession.EnsureAuthorized();
        return registry.GetAll().OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<string>> DiscoverServerDatabasesAsync(CancellationToken cancellationToken = default)
    {
        adminSession.EnsureAuthorized();
        var baseConnection = databaseSettingsService.GetCurrentConnection().ConnectionString;
        if (string.IsNullOrWhiteSpace(baseConnection)) return [];

        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = "master",
            ConnectTimeout = 8
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(timeout.Token);
        const string sql = """
            SELECT [name]
            FROM sys.databases
            WHERE database_id > 4
              AND [state] = 0
              AND HAS_DBACCESS([name]) = 1
            ORDER BY [name]
            """;
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(timeout.Token);
        var result = new List<string>();
        while (await reader.ReadAsync(timeout.Token)) result.Add(reader.GetString(0));
        await auditService.WriteAsync(adminSession.UserName!, "DiscoverDatabases", "SQL Server", true, $"تم اكتشاف {result.Count} قاعدة.", cancellationToken);
        return result;
    }

    public async Task<CompanyDatabaseLinkTestResult> TestAsync(
        CompanyDatabaseDefinition definition,
        CancellationToken cancellationToken = default)
    {
        adminSession.EnsureAuthorized();
        if (string.IsNullOrWhiteSpace(definition.DatabaseName))
            return new(false, "اختر قاعدة البيانات أولًا.");

        try
        {
            var connectionString = BuildConnectionString(definition);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);

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
            await using var schemaCommand = new SqlCommand(schemaSql, connection);
            if (Convert.ToInt32(await schemaCommand.ExecuteScalarAsync(timeout.Token)) != 1)
            {
                return new(false, "تم الاتصال، لكن بنية القاعدة لا تطابق بنية ProACC المطلوبة.", connection.DataSource, connection.Database);
            }

            string? companyName = null;
            await using (var companyCommand = new SqlCommand("SELECT TOP (1) [Comp] FROM [COMPANY] ORDER BY [CompID]", connection))
            {
                var value = await companyCommand.ExecuteScalarAsync(timeout.Token);
                companyName = value is null or DBNull ? null : Convert.ToString(value);
            }

            return new(
                true,
                $"تم الربط والتحقق من بنية قاعدة {connection.Database} بنجاح.",
                connection.DataSource,
                connection.Database,
                companyName,
                connection.ServerVersion);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "انتهت مهلة الاتصال بقاعدة المنشأة.");
        }
        catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException)
        {
            return new(false, $"تعذر ربط قاعدة المنشأة: {ex.Message}");
        }
    }

    public async Task<CompanyRegistryResult> SaveAsync(
        CompanyDatabaseDefinition definition,
        string? originalKey,
        CancellationToken cancellationToken = default)
    {
        adminSession.EnsureAuthorized();
        if (string.IsNullOrWhiteSpace(originalKey)
            && (string.IsNullOrWhiteSpace(definition.LoginCode) || definition.LoginCode.Trim().Length < 8))
            return new(false, "رمز المنشأة مطلوب ويجب ألا يقل عن 8 محارف.");

        var test = await TestAsync(definition, cancellationToken);
        if (!test.Success) return new(false, $"لم يتم الحفظ. {test.Message}");

        var storedDefinition = new CompanyDatabaseDefinition
        {
            Key = definition.Key,
            DatabaseName = definition.DatabaseName,
            DisplayName = definition.DisplayName,
            LoginCode = definition.LoginCode,
            LoginCodeHash = definition.LoginCodeHash,
            ConnectionString = BuildConnectionString(definition),
            Enabled = definition.Enabled
        };
        var result = await registry.UpsertAsync(storedDefinition, originalKey, cancellationToken);
        await auditService.WriteAsync(
            adminSession.UserName!,
            string.IsNullOrWhiteSpace(originalKey) ? "AddCompanyDatabase" : "UpdateCompanyDatabase",
            definition.DatabaseName,
            result.Success,
            result.Message,
            cancellationToken);
        return result;
    }

    public async Task<CompanyRegistryResult> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        adminSession.EnsureAuthorized();
        var target = registry.GetAll().FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))?.DatabaseName ?? key;
        var result = await registry.DeleteAsync(key, cancellationToken);
        await auditService.WriteAsync(adminSession.UserName!, "DeleteCompanyDatabase", target, result.Success, result.Message, cancellationToken);
        return result;
    }

    public string GenerateLoginCode()
    {
        adminSession.EnsureAuthorized();
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    private string BuildConnectionString(CompanyDatabaseDefinition definition)
    {
        var source = string.IsNullOrWhiteSpace(definition.ConnectionString)
            ? databaseSettingsService.GetCurrentConnection().ConnectionString
            : definition.ConnectionString;
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("رابط الخادم المشترك غير محدد.");

        var builder = new SqlConnectionStringBuilder(source)
        {
            InitialCatalog = definition.DatabaseName.Trim(),
            MultipleActiveResultSets = true
        };
        return builder.ConnectionString;
    }
}
