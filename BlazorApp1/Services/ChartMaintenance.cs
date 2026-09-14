using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BlazorApp1.Services;

public sealed class ChartEditVm
{
    public int Level { get; set; } = 1;
    public long? OriginalId { get; set; }
    public string? Version { get; set; }
    public long? Number { get; set; }
    public string Name { get; set; } = "";
    public long? ParentId { get; set; }
    public bool Active { get; set; } = true;
    public bool Chart { get; set; } = true;
    public int? Group1 { get; set; }
    public int? Group2 { get; set; }
}

public sealed record ChartParentOptionVm(long Id, string Name, string? Detail = null);

public partial class AccountingDataService
{
    // Identifiers are exclusively from this allowlist, never from input.
    private sealed record ChartTable(string Table, string Id, string Name, string Parent);
    private static readonly ChartTable[] ChartTables = [
        new("Direction", "DirectionID", "Direction", ""),
        new("Level0", "Level0ID", "Level0", "DirectionID"),
        new("Level1", "Level1ID", "Level1", "Level0ID"),
        new("Type", "TypeID", "Type", "Level1ID"),
        new("ACCType", "ACCTypeID", "ACCType", "TypeID"),
        new("CATEG", "CategID", "Categ", "AccTypeID"),
        new("ACC", "ACCID", "ACC", "CategID")
    ];
    private static string Q(string value) => "[" + value.Replace("]", "]]") + "]";
    // Read the configured string; an already-opened SqlConnection may omit its password.
    private string? ChartConnectionString => dbContext.GetService<IDbContextOptions>().Extensions
        .OfType<RelationalOptionsExtension>().First().ConnectionString ?? dbContext.Database.GetConnectionString();

    public async Task<IReadOnlyList<ChartParentOptionVm>> GetChartDirectionsAsync()
    {
        await access.RequireAsync("Data");
        return await dbContext.Directions.AsNoTracking().OrderBy(x => x.DirectionID)
            .Select(x => new ChartParentOptionVm(x.DirectionID, x.DirectionName ?? "", null)).ToListAsync();
    }

    public async Task<IReadOnlyList<ChartParentOptionVm>> GetChartGroupsAsync(int group)
    {
        await access.RequireAsync("Data");
        if (group is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(group));
        await using var connection = new SqlConnection(ChartConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT GroupID, Group{group}Name FROM GROUP{group} ORDER BY GroupID";
        await using var reader = await command.ExecuteReaderAsync();
        var options = new List<ChartParentOptionVm>();
        while (await reader.ReadAsync())
            options.Add(new(Convert.ToInt64(reader.GetValue(0)), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        return options;
    }

    private static string SnapshotSql(ChartTable table, int level, string parameter) =>
        $"SELECT {Q(table.Id)},{Q(table.Name)},{Q(table.Parent)}{(level == 6 ? ",[Active],[Chart],[Group1],[Group2]" : "")} FROM {Q(table.Table)} WHERE {Q(table.Id)}={parameter} FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER";
    private static string VersionOf(string json) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));

    public async Task<ChartEditVm?> GetChartEditAsync(int level, long id)
    {
        await access.RequireAsync("Data");
        if (level is < 1 or > 6) return null;
        var t = ChartTables[level];
        await using var connection = new SqlConnection(ChartConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Q(t.Id)}, {Q(t.Name)}, {Q(t.Parent)}, {(level == 6 ? "[Active],[Chart],[Group1],[Group2]" : "CAST(1 AS bit),CAST(0 AS bit),NULL,NULL")}, ({SnapshotSql(t, level, "@id")}) FROM {Q(t.Table)} WHERE {Q(t.Id)}=@id";
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ChartEditVm { Level = level, OriginalId = id, Number = id,
            Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
            ParentId = reader.IsDBNull(2) ? null : Convert.ToInt64(reader.GetValue(2)),
            Active = reader.IsDBNull(3) || reader.GetBoolean(3),
            Chart = !reader.IsDBNull(4) && reader.GetBoolean(4),
            Group1 = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            Group2 = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            Version = VersionOf(reader.GetString(7)) };
    }

    public Task<ServiceResultVm> SaveChartAsync(ChartEditVm request) => MutateChartAsync(request, false);
    public Task<ServiceResultVm> DeleteChartAsync(int level, long id) =>
        MutateChartAsync(new ChartEditVm { Level = level, OriginalId = id, Number = id }, true);

    private async Task<ServiceResultVm> MutateChartAsync(ChartEditVm request, bool delete)
    {
        ServiceResultVm Invalid(string message) => new() { Message = message };
        if (!sessionService.IsAuthenticated) return Invalid("يجب تسجيل الدخول أولاً.");
        await access.RequireAsync("Data");
        if (request.Level is < 1 or > 6 || request.Number is null or <= 0 ||
            (request.Level < 6 && request.Number > int.MaxValue)) return Invalid("أدخل رقم حساب صحيحاً موجباً ضمن نطاق المستوى.");
        if (!delete && (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100))
            return Invalid("اسم الحساب مطلوب وبحد أقصى 100 حرف.");
        if (!delete && (request.ParentId is null or <= 0 || request.ParentId > int.MaxValue))
            return Invalid(request.Level == 1 ? "اختر تصنيف القائمة المالية من نتائج البحث." : "اختر الحساب الرئيسي من نتائج البحث.");

        var t = ChartTables[request.Level];
        await using var connection = new SqlConnection(ChartConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        async Task<object?> Scalar(string sql)
        {
            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@id", request.Number!.Value);
            cmd.Parameters.AddWithValue("@old", (object?)request.OriginalId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@parent", (object?)request.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", request.Name.Trim());
            cmd.Parameters.AddWithValue("@active", request.Active);
            cmd.Parameters.AddWithValue("@chart", request.Chart);
            cmd.Parameters.Add("@group1", SqlDbType.Int).Value = (object?)request.Group1 ?? DBNull.Value;
            cmd.Parameters.Add("@group2", SqlDbType.Int).Value = (object?)request.Group2 ?? DBNull.Value;
            return await cmd.ExecuteScalarAsync();
        }
        try
        {
            if (request.OriginalId.HasValue && Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM {Q(t.Table)} WITH (UPDLOCK,HOLDLOCK) WHERE {Q(t.Id)}=@old")) == 0)
                return Invalid("الحساب لم يعد موجوداً. أغلق النافذة وحدّث الشجرة.");
            if (!delete && request.OriginalId.HasValue)
            {
                var currentVersion = VersionOf(Convert.ToString(await Scalar(SnapshotSql(t, request.Level, "@old"))) ?? "");
                if (request.Version != currentVersion)
                    return Invalid("تغيرت بيانات هذا الحساب منذ فتح النافذة. أغلقها وافتح التعديل مجدداً لمراجعة أحدث البيانات قبل الحفظ.");
            }
            var references = new HashSet<(string Table, string Column)>();
            // Include physical foreign keys, including tables outside the EF model.
            using (var cmd = new SqlCommand("SELECT OBJECT_SCHEMA_NAME(f.parent_object_id)+'.'+OBJECT_NAME(f.parent_object_id), COL_NAME(f.parent_object_id,f.parent_column_id) FROM sys.foreign_key_columns f WHERE f.referenced_object_id=OBJECT_ID(@table) AND COL_NAME(f.referenced_object_id,f.referenced_column_id)=@key", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@table", t.Table);
                cmd.Parameters.AddWithValue("@key", t.Id);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) references.Add((reader.GetString(0), reader.GetString(1)));
            }
            // Legacy databases also have logical relationships without FK constraints.
            if (request.Level < 6) references.Add(("dbo." + ChartTables[request.Level + 1].Table, ChartTables[request.Level + 1].Parent));
            if (request.Level == 6)
            {
                references.Add(("dbo.TRANS", "ACCID"));
                references.Add(("dbo.GL", "GLACC"));
                references.Add(("dbo.VAT", "SalesTaxACC"));
                references.Add(("dbo.VAT", "PurchTaxACC"));
            }
            string RefTable(string name) => string.Join(".", name.Split('.').Select(Q));
            if (delete)
            {
                foreach (var r in references)
                    if (Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM {RefTable(r.Table)} WITH (UPDLOCK,HOLDLOCK) WHERE {Q(r.Column)}=@old")) > 0)
                        return Invalid("لا يمكن الحذف: توجد حسابات فرعية أو حركات أو إعدادات مرتبطة. انقل الارتباطات أولاً.");
                await Scalar($"DELETE FROM {Q(t.Table)} WHERE {Q(t.Id)}=@old");
            }
            else
            {
                if (request.Level == 6)
                {
                    if (request.Group1.HasValue && Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM GROUP1 WITH (UPDLOCK,HOLDLOCK) WHERE GroupID=@group1")) == 0)
                        return Invalid("تصنيف 1 المحدد غير موجود. اختره من نتائج البحث أو أزل الاختيار.");
                    if (request.Group2.HasValue && Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM GROUP2 WITH (UPDLOCK,HOLDLOCK) WHERE GroupID=@group2")) == 0)
                        return Invalid("تصنيف 2 المحدد غير موجود. اختره من نتائج البحث أو أزل الاختيار.");
                }
                var parent = ChartTables[request.Level - 1];
                if (Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM {Q(parent.Table)} WITH (UPDLOCK,HOLDLOCK) WHERE {Q(parent.Id)}=@parent")) == 0)
                    return Invalid("الحساب الرئيسي المحدد غير موجود في المستوى السابق.");
                if (Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM {Q(t.Table)} WITH (UPDLOCK,HOLDLOCK) WHERE {Q(t.Id)}=@id AND (@old IS NULL OR {Q(t.Id)}<>@old)")) > 0)
                    return Invalid("رقم الحساب مستخدم بالفعل في هذا المستوى.");
                if (request.OriginalId == request.Number)
                    await Scalar($"UPDATE {Q(t.Table)} SET {Q(t.Name)}=@name,{Q(t.Parent)}=@parent{(request.Level == 6 ? ",[Active]=@active,[Chart]=@chart,[Group1]=@group1,[Group2]=@group2" : "")} WHERE {Q(t.Id)}=@old");
                else
                {
                    var columns = new List<string>();
                    bool identity = false;
                    using (var cmd = new SqlCommand("SELECT name,is_identity FROM sys.columns WHERE object_id=OBJECT_ID(@table) AND is_computed=0 AND system_type_id<>189 ORDER BY column_id", connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@table", t.Table);
                        await using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync()) { columns.Add(reader.GetString(0)); identity |= reader.GetBoolean(1); }
                    }
                    string insert;
                    if (request.OriginalId.HasValue)
                    {
                        // Copy every persisted column, including fields not mapped in EF.
                        var values = columns.Select(c => c.Equals(t.Id, StringComparison.OrdinalIgnoreCase) ? "@id" :
                            c.Equals(t.Name, StringComparison.OrdinalIgnoreCase) ? "@name" :
                            c.Equals(t.Parent, StringComparison.OrdinalIgnoreCase) ? "@parent" :
                            request.Level == 6 && c.Equals("Active", StringComparison.OrdinalIgnoreCase) ? "@active" :
                            request.Level == 6 && c.Equals("Chart", StringComparison.OrdinalIgnoreCase) ? "@chart" :
                            request.Level == 6 && c.Equals("Group1", StringComparison.OrdinalIgnoreCase) ? "@group1" :
                            request.Level == 6 && c.Equals("Group2", StringComparison.OrdinalIgnoreCase) ? "@group2" : Q(c));
                        insert = $"INSERT INTO {Q(t.Table)} ({string.Join(',', columns.Select(Q))}) SELECT {string.Join(',', values)} FROM {Q(t.Table)} WHERE {Q(t.Id)}=@old;";
                    }
                    else insert = $"INSERT INTO {Q(t.Table)} ({Q(t.Id)},{Q(t.Name)},{Q(t.Parent)}{(request.Level == 6 ? ",[Active],[Chart],[Group1],[Group2]" : "")}) VALUES (@id,@name,@parent{(request.Level == 6 ? ",@active,@chart,@group1,@group2" : "")});";
                    if (identity) insert = $"BEGIN TRY SET IDENTITY_INSERT {Q(t.Table)} ON; {insert} SET IDENTITY_INSERT {Q(t.Table)} OFF; END TRY BEGIN CATCH SET IDENTITY_INSERT {Q(t.Table)} OFF; THROW; END CATCH";
                    await Scalar(insert);
                    if (request.OriginalId.HasValue)
                    {
                        foreach (var r in references)
                            await Scalar($"UPDATE {RefTable(r.Table)} SET {Q(r.Column)}=@id WHERE {Q(r.Column)}=@old");
                        await Scalar($"DELETE FROM {Q(t.Table)} WHERE {Q(t.Id)}=@old");
                    }
                }
            }
            // ACC.Group1 and Group2 reference independent classification tables,
            // not Level0/Level1. Only change them through the level-six editor fields.
            var savedVersion = delete ? null : VersionOf(Convert.ToString(await Scalar(SnapshotSql(t, request.Level, "@id"))) ?? "");
            await transaction.CommitAsync();
            if (!delete) { request.Version = savedVersion; request.OriginalId = request.Number; }
            dbContext.ChangeTracker.Clear();
            return new ServiceResultVm { Success = true, EntityId = request.Number, Message = delete ? "تم حذف الحساب." : "تم حفظ الحساب وعلاقاته بنجاح." };
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 547 or 515 or 1205)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            logger?.LogWarning(ex, "Chart mutation failed for level {Level}, account {Number}, deleting {Deleting}", request.Level, request.Number, delete);
            var reason = ex.Number switch
            {
                2601 or 2627 => "رقم الحساب أو إحدى القيم الفريدة مستخدم بالفعل. اختر رقماً آخر.",
                547 => delete
                    ? "لا يمكن حذف الحساب لوجود بيانات مرتبطة به. أزل الارتباطات أولاً."
                    : "تعذر حفظ الحساب بسبب علاقة غير صالحة في قاعدة البيانات. تحقق من الحساب الرئيسي وتصنيفات الحساب.",
                515 => "تعذر حفظ الحساب لعدم اكتمال حقل إلزامي في قاعدة البيانات.",
                1205 => "تزامنت العملية مع تعديل آخر. حاول الحفظ مجدداً.",
                _ => "تعذر حفظ الحساب."
            };
            return Invalid(reason + " لم تُحفظ أي تغييرات.");
        }
    }
}
