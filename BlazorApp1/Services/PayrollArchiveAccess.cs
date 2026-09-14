using Microsoft.Data.SqlClient;

namespace BlazorApp1.Services;

public sealed partial class MasterDataService
{
    private async Task EnsurePayrollArchiveAccessAsync(SqlConnection connection, SqlTransaction transaction, string? id, IReadOnlyDictionary<string, string?> values)
    {
        if (access.Has("Archive")) return;
        var requested = values.TryGetValue("Archive", out var value) && bool.TryParse(value, out var archived) && archived;
        var stored = false;
        if (!string.IsNullOrWhiteSpace(id))
        {
            using var command = new SqlCommand("SELECT Archive FROM Payroll WITH (UPDLOCK, HOLDLOCK) WHERE PayrollID=@id", connection, transaction);
            command.Parameters.AddWithValue("@id", ParseFilterValue(id));
            var result = await command.ExecuteScalarAsync();
            stored = result is not null and not DBNull && Convert.ToBoolean(result);
        }
        if (requested != stored) throw new UnauthorizedAccessException("لا تملك صلاحية تغيير الأرشفة.");
    }
}
