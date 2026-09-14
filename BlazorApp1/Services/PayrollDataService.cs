using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace BlazorApp1.Services;

public sealed class PayrollDocumentVm
{
    public MasterDataRowVm Header { get; set; } = new();
    public List<MasterDataRowVm> Lines { get; set; } = [];

    // Preserve the target identity, date and archive state when copying.
    public void CopyFrom(PayrollDocumentVm source)
    {
        foreach (var column in new[] { "PayrollDesc", "Info", "PayrollNotes" })
            Header.Values[column] = source.Header.Values.GetValueOrDefault(column);
        Lines = source.Lines.Select(line => new MasterDataRowVm
        {
            Values = line.Values.Where(pair => pair.Key is not "PayrollID" and not "PayrollTransID")
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        }).ToList();
    }
}

public sealed partial class MasterDataService
{
    public async Task<IReadOnlyList<MasterDataRowVm>> GetPreviousPayrollsAsync()
    {
        await access.RequireAsync("Payroll");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT TOP (500) p.PayrollID, p.PayrollDate, p.PayrollDesc,
                COALESCE((SELECT SUM(t.Net) FROM PayrollTrans t WHERE t.PayrollID = p.PayrollID), 0) AS Net
            FROM Payroll p ORDER BY p.PayrollID DESC
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<MasterDataRowVm>();
        while (await reader.ReadAsync())
        {
            rows.Add(new MasterDataRowVm
            {
                Id = Convert.ToString(reader["PayrollID"], CultureInfo.InvariantCulture)!,
                Values = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["PayrollDate"] = reader["PayrollDate"] is DBNull ? null : ToEditorString(reader["PayrollDate"], MasterFieldKind.Date),
                    ["PayrollDesc"] = reader["PayrollDesc"] is DBNull ? null : Convert.ToString(reader["PayrollDesc"]),
                    ["Net"] = ToEditorString(reader["Net"], MasterFieldKind.Decimal)
                }
            });
        }
        return rows;
    }

    public async Task<PayrollDocumentVm?> GetPayrollAsync(string id)
    {
        await access.RequireAsync("Payroll");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var headers = await ReadPayrollRowsAsync(connection, transaction, GetSection("payroll"), id);
        if (headers.Count == 0) return null;
        // Detail reads must not use the generic master list's 500-row limit.
        var lines = await ReadPayrollRowsAsync(connection, transaction, GetSection("payroll-trans"), id);
        await transaction.CommitAsync();
        return new PayrollDocumentVm { Header = headers[0], Lines = lines };
    }

    private static async Task<List<MasterDataRowVm>> ReadPayrollRowsAsync(SqlConnection connection,
        SqlTransaction transaction, MasterSectionDefinition definition, string id)
    {
        var columns = new[] { definition.KeyColumn }.Concat(definition.Fields.Select(item => item.Column));
        await using var command = new SqlCommand(
            $"SELECT {string.Join(",", columns.Select(Quote))} FROM {Quote(definition.Table)} WHERE PayrollID = @id ORDER BY {Quote(definition.KeyColumn)}",
            connection, transaction);
        command.Parameters.AddWithValue("@id", ParseFilterValue(id));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<MasterDataRowVm>();
        while (await reader.ReadAsync())
        {
            var row = new MasterDataRowVm { Id = Convert.ToString(reader[definition.KeyColumn], CultureInfo.InvariantCulture)! };
            foreach (var item in definition.Fields)
                row.Values[item.Column] = reader[item.Column] is DBNull ? null : ToEditorString(reader[item.Column], item.Kind);
            rows.Add(row);
        }
        return rows;
    }

    public async Task<ServiceResultVm> SavePayrollAsync(PayrollDocumentVm document)
    {
        await access.RequireAsync("Payroll");
        var headerDefinition = GetSection("payroll");
        var lineDefinition = GetSection("payroll-trans");
        var error = ValidatePayrollRow(document.Header, headerDefinition);
        if (error is not null) return Failed(error);
        if (document.Lines.Count == 0) return Failed("أضف موظفًا واحدًا على الأقل إلى المسير.");
        var lineIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < document.Lines.Count; index++)
        {
            var line = document.Lines[index];
            error = ValidatePayrollRow(line, lineDefinition);
            if (error is not null) return Failed($"السطر {index + 1}: {error}");
            if (!string.IsNullOrEmpty(line.Id) && !lineIds.Add(line.Id))
                return Failed("يوجد تكرار في أرقام تفاصيل المسير. أعد تحميل المسير.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            await EnsurePayrollArchiveAccessAsync(connection, transaction, document.Header.Id, document.Header.Values);
            var payrollId = document.Header.Id;
            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(payrollId))
            {
                await using var guard = new SqlCommand("SELECT PayrollID FROM Payroll WITH (UPDLOCK, HOLDLOCK) WHERE PayrollID = @id", connection, transaction);
                guard.Parameters.AddWithValue("@id", ParseFilterValue(payrollId));
                if (await guard.ExecuteScalarAsync() is null) return Failed("لم يعد مسير الرواتب موجودًا. ارجع إلى القائمة وحدّثها.");
                var existing = await ReadPayrollRowsAsync(connection, transaction, lineDefinition, payrollId);
                existingIds = existing.Select(line => line.Id).ToHashSet(StringComparer.Ordinal);
            }
            if (lineIds.Any(id => !existingIds.Contains(id)))
                return Failed("أحد السطور لا ينتمي إلى هذا المسير أو تم حذفه. أعد تحميل المسير.");

            foreach (var employeeId in document.Lines.Select(line => line.Values["EmpID"]).Distinct())
            {
                await using var employee = new SqlCommand("SELECT EmpID FROM EmpCard WHERE EmpID = @id", connection, transaction);
                employee.Parameters.AddWithValue("@id", ParseFilterValue(employeeId));
                if (await employee.ExecuteScalarAsync() is null) return Failed("أحد الموظفين المحددين غير موجود. راجع سطور المسير.");
            }

            payrollId = await WritePayrollRowAsync(connection, transaction, headerDefinition, document.Header.Id, document.Header.Values);
            foreach (var removedId in existingIds.Except(lineIds))
            {
                await using var delete = new SqlCommand("DELETE FROM PayrollTrans WHERE PayrollID = @payroll AND PayrollTransID = @id", connection, transaction);
                delete.Parameters.AddWithValue("@payroll", ParseFilterValue(payrollId));
                delete.Parameters.AddWithValue("@id", ParseFilterValue(removedId));
                await delete.ExecuteNonQueryAsync();
            }
            foreach (var line in document.Lines)
            {
                var values = new Dictionary<string, string?>(line.Values, StringComparer.OrdinalIgnoreCase)
                {
                    ["PayrollID"] = payrollId
                };
                await WritePayrollRowAsync(connection, transaction, lineDefinition, line.Id, values);
            }
            await transaction.CommitAsync();
            return new ServiceResultVm { Success = true, EntityId = long.Parse(payrollId, CultureInfo.InvariantCulture), Message = "تم حفظ مسير الرواتب وتفاصيل الموظفين بنجاح." };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Payroll save failed: {0}", ex);
            return Failed("تعذر حفظ المسير. لم تُحفظ أي تغييرات؛ تحقق من بيانات الموظفين ثم أعد المحاولة.");
        }
    }

    private static string? ValidatePayrollRow(MasterDataRowVm row, MasterSectionDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(row.Id) && (!long.TryParse(row.Id, out var id) || id <= 0))
            return "رقم السجل غير صالح.";
        foreach (var item in definition.Fields.Where(item => item.Column != "PayrollID"))
        {
            var value = row.Values.GetValueOrDefault(item.Column);
            if (item.Required && string.IsNullOrWhiteSpace(value)) return $"حقل «{item.Label}» مطلوب.";
            try { _ = ToDatabaseValue(value, item.Kind); }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { return $"قيمة «{item.Label}» غير صالحة."; }
        }
        return null;
    }

    private static async Task<string> WritePayrollRowAsync(SqlConnection connection, SqlTransaction transaction,
        MasterSectionDefinition definition, string id, IReadOnlyDictionary<string, string?> values)
    {
        var fields = definition.Fields;
        var columns = fields.Select(item => item.Column).ToList();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        AddFieldParameters(command, fields, values, columns, definition);
        if (string.IsNullOrWhiteSpace(id))
        {
            command.CommandText = $"INSERT INTO {Quote(definition.Table)} ({string.Join(",", columns.Select(Quote))}) OUTPUT INSERTED.{Quote(definition.KeyColumn)} VALUES ({string.Join(",", columns.Select((_, index) => $"@p{index}"))})";
            return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
        }
        command.CommandText = $"UPDATE {Quote(definition.Table)} SET {string.Join(",", columns.Select((column, index) => $"{Quote(column)} = @p{index}"))} WHERE {Quote(definition.KeyColumn)} = @id";
        command.Parameters.AddWithValue("@id", ParseFilterValue(id));
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("Payroll row was not found.");
        return id;
    }
}
