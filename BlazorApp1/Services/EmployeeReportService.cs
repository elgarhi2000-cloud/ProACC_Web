using System.Data;
using Microsoft.Data.SqlClient;

namespace BlazorApp1.Services;

public sealed class EmployeeReportVm
{
    public List<MasterDataRowVm> Salaries { get; set; } = [];
    public List<MasterDataRowVm> Holidays { get; set; } = [];
}

public sealed partial class MasterDataService
{
    public async Task<EmployeeReportVm> GetEmployeeReportAsync(string? employeeId, DateTime? from, DateTime? to)
    {
        await access.RequireAsync("Reports");
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new ArgumentException("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية.");
        if (!string.IsNullOrWhiteSpace(employeeId) && (!long.TryParse(employeeId, out var employee) || employee <= 0))
            throw new ArgumentException("اختر موظفًا صالحًا.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var report = new EmployeeReportVm();
        foreach (var section in new[] { "payroll-trans", "holidays" })
        {
            var salary = section == "payroll-trans";
            var definition = GetSection(section);
            var columns = new[] { definition.KeyColumn }.Concat(definition.Fields.Select(item => item.Column));
            var extras = salary ? "p.PayrollDate, p.PayrollDesc" : "ht.HolidayType AS HolidayTypeName";
            var joins = salary ? "LEFT JOIN Payroll p ON p.PayrollID = t.PayrollID"
                : "LEFT JOIN HolidayType ht ON ht.HolidayID = t.HolidayType";
            var start = salary ? "p.PayrollDate" : "COALESCE(t.HolFrom,t.HolTo,t.HolidayDate)";
            var end = salary ? "p.PayrollDate" : "COALESCE(t.HolTo,t.HolFrom,t.HolidayDate)";
            await using var command = new SqlCommand($"""
                SELECT {string.Join(",", columns.Select(column => $"t.{Quote(column)}"))},
                    e.EmpName, {extras}
                FROM {Quote(definition.Table)} t
                LEFT JOIN EmpCard e ON e.EmpID = t.EmpID
                {joins}
                WHERE (@employee IS NULL OR t.EmpID = @employee)
                  AND (@from IS NULL OR CAST({end} AS date) >= @from)
                  AND (@to IS NULL OR CAST({start} AS date) <= @to)
                ORDER BY {start} DESC, t.{Quote(definition.KeyColumn)} DESC
                """, connection);
            command.Parameters.Add("@employee", SqlDbType.BigInt).Value = string.IsNullOrWhiteSpace(employeeId) ? DBNull.Value : long.Parse(employeeId);
            command.Parameters.Add("@from", SqlDbType.Date).Value = (object?)from?.Date ?? DBNull.Value;
            command.Parameters.Add("@to", SqlDbType.Date).Value = (object?)to?.Date ?? DBNull.Value;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new MasterDataRowVm { Id = Convert.ToString(reader[definition.KeyColumn])! };
                foreach (var item in definition.Fields)
                    row.Values[item.Column] = reader[item.Column] is DBNull ? null : ToEditorString(reader[item.Column], item.Kind);
                row.Values["EmpName"] = reader["EmpName"] is DBNull ? null : Convert.ToString(reader["EmpName"]);
                if (salary)
                {
                    row.Values["PayrollDate"] = reader["PayrollDate"] is DBNull ? null : ToEditorString(reader["PayrollDate"], MasterFieldKind.Date);
                    row.Values["PayrollDesc"] = reader["PayrollDesc"] is DBNull ? null : Convert.ToString(reader["PayrollDesc"]);
                }
                else row.Values["HolidayTypeName"] = reader["HolidayTypeName"] is DBNull ? null : Convert.ToString(reader["HolidayTypeName"]);
                (salary ? report.Salaries : report.Holidays).Add(row);
            }
        }
        return report;
    }
}
