using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.WebUtilities;

namespace BlazorApp1.Services;

public static class SectionAccess
{
    public static readonly string[] Permissions = ["Main", "GL", "RC", "PM", "INV", "Cards", "Reports", "Queries", "EmpCard", "Holidays", "Payroll", "Setting", "Users", "Data", "Designer", "Archive"];
    public static string Source(int? source) => source switch { 2 => "RC", 3 => "PM", 5 or 6 => "INV", _ => "GL" };
    public static string Master(string section) => section.ToLowerInvariant() switch
    {
        "users" => "Users", "employees" => "EmpCard", "holidays" => "Holidays",
        "payroll" or "payroll-trans" => "Payroll", "cards" => "Cards", _ => "Setting"
    };
    public static string PathOnly(string path) => path.Split('?', '#')[0].Trim('/').ToLowerInvariant();
    public static string? Route(string path) => PathOnly(path) switch
    {
        "" or "dashboard" => "Main",
        "accounting/journal-entries" or "accounting/opening-balances" => "GL",
        "accounting/receipts" => "RC", "accounting/payments" => "PM",
        "accounting/invoices" or "accounting/purchase-invoices" => "INV",
        "accounting/report-designer" or "accounting/settings/report-designer" => "Designer",
        "accounting/cards" => "Cards", "accounting/chart-of-accounts" => "Data",
        "accounting/hr/employees" => "EmpCard", "accounting/hr/holidays" => "Holidays",
        "accounting/hr/payroll" => "Payroll",
        var p when p.StartsWith("accounting/hr/payroll/") => "Payroll",
        "accounting/reports" => "Reports",
        var p when p.StartsWith("accounting/reports/") => "Reports",
        _ => null
    };
    public static bool CanRoute(string path, Func<string, bool> has)
    {
        var clean = PathOnly(path);
        if (clean is "accounting/settings") return has("Setting") || has("Users");
        if (clean is "access-denied" or "not-found" or "error") return true;
        return Route(path) is { } permission && has(permission);
    }
    public static string Landing(Func<string, bool> has) => new[] { "/dashboard", "/accounting/journal-entries", "/accounting/receipts", "/accounting/payments", "/accounting/invoices", "/accounting/cards", "/accounting/hr/employees", "/accounting/hr/payroll", "/accounting/hr/holidays", "/accounting/reports", "/accounting/chart-of-accounts", "/accounting/settings", "/accounting/report-designer" }.FirstOrDefault(p => CanRoute(p, has)) ?? "/access-denied";
}

public interface IUserAccessService
{
    bool Has(string permission);
    Task RefreshAsync();
    Task RequireAsync(params string[] anyPermission);
    Task<bool> CanOpenAsync(string relativeUrl);
    Task RequireEntryAsync(int id, int? periodId = null);
}

public sealed class UserAccessService : IUserAccessService, IDisposable
{
    private readonly ICompanyDatabaseService companies;
    private readonly ISessionService session;
    private int sessionVersion;
    public UserAccessService(ICompanyDatabaseService companies, ISessionService session)
    {
        this.companies = companies;
        this.session = session;
        session.OnChange += Clear;
    }
    private void Clear() { sessionVersion++; identity = null; granted = new(StringComparer.OrdinalIgnoreCase); }
    public void Dispose() => session.OnChange -= Clear;
    private HashSet<string> granted = new(StringComparer.OrdinalIgnoreCase);
    private string? identity;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private string? Identity => session.IsAuthenticated ? $"{session.SelectedDatabaseKey}\n{session.CurrentUsername}" : null;
    public bool Has(string permission) => Identity is { } current && identity == current && granted.Contains(permission);
    public async Task RefreshAsync()
    {
        await refreshLock.WaitAsync();
        try
        {
            var current = Identity;
            var version = sessionVersion;
            granted = new(StringComparer.OrdinalIgnoreCase);
            identity = null;
            if (current is null) return;
            await using var connection = new SqlConnection(companies.BuildConnectionString(session.SelectedDatabaseKey));
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(',', SectionAccess.Permissions.Select(p => $"[{p}]"))} FROM [USER] WHERE UserName = @name";
            command.Parameters.AddWithValue("@name", session.CurrentUsername!.Trim());
            await using var reader = await command.ExecuteReaderAsync();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!await reader.ReadAsync()) return;
            foreach (var permission in SectionAccess.Permissions)
                if (reader[permission] is not DBNull && Convert.ToBoolean(reader[permission])) allowed.Add(permission);
            // Ambiguous/deleted user records and changed sessions never inherit permissions.
            if (await reader.ReadAsync() || current != Identity || version != sessionVersion) return;
            granted = allowed;
            identity = current;
        }
        finally { refreshLock.Release(); }
    }
    public async Task RequireAsync(params string[] anyPermission)
    {
        await RefreshAsync();
        if (!session.IsAuthenticated || identity is null || identity != Identity || (anyPermission.Length > 0 && !anyPermission.Any(Has)))
            throw new UnauthorizedAccessException("ليس لديك صلاحية دخول هذا القسم.");
    }
    private async Task<int?> EntrySourceAsync(int id, int? periodId)
    {
        await using var connection = new SqlConnection(companies.BuildConnectionString(session.SelectedDatabaseKey));
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SourceID FROM GL WHERE GLID=@id AND PeriodID=@period";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@period", (object?)periodId ?? DBNull.Value);
        var source = await command.ExecuteScalarAsync();
        return source is null ? null : source is DBNull ? 1 : Convert.ToInt32(source);
    }
    public async Task RequireEntryAsync(int id, int? periodId = null)
    {
        await RequireAsync("GL", "RC", "PM", "INV");
        var source = await EntrySourceAsync(id, periodId ?? session.SelectedPeriodId);
        if (!source.HasValue || !Has(SectionAccess.Source(source))) throw new UnauthorizedAccessException("السجل غير متاح أو لا تملك صلاحية الوصول إليه.");
    }
    public async Task<bool> CanOpenAsync(string relativeUrl)
    {
        await RefreshAsync();
        if (!session.IsAuthenticated) return false;
        var path = SectionAccess.PathOnly(relativeUrl);
        if (path.StartsWith("accounting/journal-entry/"))
        {
            var key = path.Split('/').Last();
            if (key == "new")
            {
                var query = QueryHelpers.ParseQuery(relativeUrl.Contains('?') ? relativeUrl[(relativeUrl.IndexOf('?') + 1)..].Split('#')[0] : "");
                return Has(SectionAccess.Source(query.TryGetValue("sourceId", out var value) && int.TryParse(value, out var source) ? source : 1));
            }
            if (!int.TryParse(key, out var id)) return false;
            var storedSource = await EntrySourceAsync(id, session.SelectedPeriodId);
            return storedSource.HasValue && Has(SectionAccess.Source(storedSource));
        }
        return SectionAccess.CanRoute(relativeUrl, Has);
    }
}
