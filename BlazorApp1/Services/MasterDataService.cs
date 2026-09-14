using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace BlazorApp1.Services;

public enum MasterFieldKind
{
    Text,
    Multiline,
    Integer,
    Decimal,
    Date,
    Boolean,
    Password,
    Lookup,
    Image
}

public sealed record MasterFieldDefinition(
    string Column,
    string Label,
    MasterFieldKind Kind = MasterFieldKind.Text,
    bool Required = false,
    bool ShowInTable = false,
    string? LookupTable = null,
    string? LookupValueColumn = null,
    string? LookupDisplayColumn = null);

public sealed record MasterSectionDefinition(
    string Key,
    string Title,
    string Area,
    string Category,
    string Table,
    string KeyColumn,
    bool KeyIsIdentity,
    string Icon,
    IReadOnlyList<MasterFieldDefinition> Fields);

public sealed class MasterDataRowVm
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MasterLookupOptionVm
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public interface IMasterDataService
{
    IReadOnlyList<MasterSectionDefinition> GetSections(string area);
    MasterSectionDefinition GetSection(string sectionKey);
    Task<IReadOnlyList<MasterDataRowVm>> GetRowsAsync(string sectionKey, string? filterColumn = null, string? filterValue = null);
    Task<Dictionary<string, IReadOnlyList<MasterLookupOptionVm>>> GetLookupsAsync(string sectionKey);
    Task<ServiceResultVm> SaveAsync(string sectionKey, string? id, IReadOnlyDictionary<string, string?> values);
    Task<ServiceResultVm> DeleteAsync(string sectionKey, string id);
    Task<PayrollDocumentVm?> GetPayrollAsync(string id);
    Task<IReadOnlyList<MasterDataRowVm>> GetPreviousPayrollsAsync();
    Task<EmployeeReportVm> GetEmployeeReportAsync(string? employeeId, DateTime? from, DateTime? to);
    Task<ServiceResultVm> SavePayrollAsync(PayrollDocumentVm document);
}

public sealed partial class MasterDataService : IMasterDataService
{
    private readonly ICompanyDatabaseService companyDatabaseService;
    private readonly ISessionService sessionService;
    private readonly IUserAccessService access;
    private string connectionString => sessionService.IsAuthenticated
        ? companyDatabaseService.BuildConnectionString(sessionService.SelectedDatabaseKey)
        : throw new UnauthorizedAccessException("يجب تسجيل الدخول أولاً.");

    public MasterDataService(
        ICompanyDatabaseService companyDatabaseService,
        ISessionService sessionService, IUserAccessService access)
    {
        this.companyDatabaseService = companyDatabaseService;
        this.sessionService = sessionService;
        this.access = access;
    }

    public IReadOnlyList<MasterSectionDefinition> GetSections(string area)
        => MasterDataCatalog.Sections.Values
            .Where(x => string.Equals(x.Area, area, StringComparison.OrdinalIgnoreCase) && access.Has(SectionAccess.Master(x.Key)))
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Title)
            .ToList();

    public MasterSectionDefinition GetSection(string sectionKey)
        => MasterDataCatalog.Sections.TryGetValue(sectionKey, out var definition)
            ? definition
            : throw new ArgumentException("قسم البيانات غير معروف.", nameof(sectionKey));

    public async Task<IReadOnlyList<MasterDataRowVm>> GetRowsAsync(
        string sectionKey,
        string? filterColumn = null,
        string? filterValue = null)
    {
        await access.RequireAsync(SectionAccess.Master(sectionKey));
        var definition = GetSection(sectionKey);
        var columns = new[] { definition.KeyColumn }
            .Concat(definition.Fields.Where(x => x.Kind != MasterFieldKind.Password).Select(x => x.Column))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(filterColumn)
            && !columns.Contains(filterColumn, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("حقل التصفية غير مسموح به.");
        }

        var sql = $"SELECT TOP (500) {string.Join(",", columns.Select(Quote))} FROM {Quote(definition.Table)}";
        if (!string.IsNullOrWhiteSpace(filterColumn))
        {
            sql += $" WHERE {Quote(filterColumn)} = @filter";
        }
        sql += $" ORDER BY {Quote(definition.KeyColumn)} DESC";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        if (!string.IsNullOrWhiteSpace(filterColumn))
        {
            command.Parameters.AddWithValue("@filter", ParseFilterValue(filterValue));
        }

        var rows = new List<MasterDataRowVm>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new MasterDataRowVm { Id = Convert.ToString(reader[definition.KeyColumn], CultureInfo.InvariantCulture) ?? string.Empty };
            foreach (var field in definition.Fields)
            {
                if (field.Kind == MasterFieldKind.Password)
                {
                    row.Values[field.Column] = null;
                    continue;
                }
                var value = reader[field.Column];
                row.Values[field.Column] = value is DBNull ? null : ToEditorString(value, field.Kind);
            }
            rows.Add(row);
        }
        return rows;
    }

    public async Task<Dictionary<string, IReadOnlyList<MasterLookupOptionVm>>> GetLookupsAsync(string sectionKey)
    {
        await access.RequireAsync(SectionAccess.Master(sectionKey), sectionKey == "holidays" ? "Reports" : SectionAccess.Master(sectionKey));
        var definition = GetSection(sectionKey);
        var result = new Dictionary<string, IReadOnlyList<MasterLookupOptionVm>>(StringComparer.OrdinalIgnoreCase);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var field in definition.Fields.Where(x => x.Kind == MasterFieldKind.Lookup))
        {
            if (string.IsNullOrWhiteSpace(field.LookupTable)
                || string.IsNullOrWhiteSpace(field.LookupValueColumn)
                || string.IsNullOrWhiteSpace(field.LookupDisplayColumn))
            {
                continue;
            }

            var sql = $"SELECT {Quote(field.LookupValueColumn)}, {Quote(field.LookupDisplayColumn)} FROM {Quote(field.LookupTable)} ORDER BY {Quote(field.LookupDisplayColumn)}";
            await using var command = new SqlCommand(sql, connection);
            var options = new List<MasterLookupOptionVm>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                options.Add(new MasterLookupOptionVm
                {
                    Value = Convert.ToString(reader[0], CultureInfo.InvariantCulture) ?? string.Empty,
                    Label = Convert.ToString(reader[1], CultureInfo.CurrentCulture) ?? string.Empty
                });
            }
            result[field.Column] = options;
        }

        return result;
    }

    public async Task<ServiceResultVm> SaveAsync(
        string sectionKey,
        string? id,
        IReadOnlyDictionary<string, string?> values)
    {
        await access.RequireAsync(SectionAccess.Master(sectionKey));
        var definition = GetSection(sectionKey);
        var isCreate = string.IsNullOrWhiteSpace(id);

        if (isCreate && string.Equals(sectionKey, "periods", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedValues = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
            foreach (var sequenceField in new[] { "GLID", "RCID", "PMID", "SalesID", "PurchID", "PayrollID" })
            {
                if (!normalizedValues.TryGetValue(sequenceField, out var sequenceValue)
                    || string.IsNullOrWhiteSpace(sequenceValue))
                {
                    normalizedValues[sequenceField] = "1";
                }
            }
            values = normalizedValues;
        }

        if (isCreate && string.Equals(sectionKey, "companies", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("لا يمكن إضافة سجل منشأة جديد؛ يسمح بتعديل بيانات المنشأة الحالية فقط.");
        }

        foreach (var requiredField in definition.Fields.Where(x => x.Required))
        {
            if (!isCreate && requiredField.Kind == MasterFieldKind.Password)
            {
                continue;
            }
            if (!values.TryGetValue(requiredField.Column, out var requiredValue) || string.IsNullOrWhiteSpace(requiredValue))
            {
                return Failed($"حقل «{requiredField.Label}» مطلوب.");
            }
        }

        var fields = definition.Fields
            .Where(field => !(field.Kind == MasterFieldKind.Password
                              && !isCreate
                              && (!values.TryGetValue(field.Column, out var password) || string.IsNullOrWhiteSpace(password))))
            .ToList();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction)transaction;

            if (sectionKey == "payroll")
                await EnsurePayrollArchiveAccessAsync(connection, (SqlTransaction)transaction, id, values);

            if (isCreate)
            {
                var insertColumns = fields.Select(x => x.Column).ToList();
                if (!definition.KeyIsIdentity)
                {
                    if (!values.TryGetValue(definition.KeyColumn, out var keyValue) || string.IsNullOrWhiteSpace(keyValue))
                    {
                        return Failed("رقم السجل مطلوب.");
                    }
                    insertColumns.Insert(0, definition.KeyColumn);
                    command.Parameters.AddWithValue("@keyValue", ParseFilterValue(keyValue));
                }

                var parameterNames = insertColumns.Select((column, index) =>
                    string.Equals(column, definition.KeyColumn, StringComparison.OrdinalIgnoreCase) && !definition.KeyIsIdentity
                        ? "@keyValue"
                        : $"@p{index}").ToList();

                command.CommandText = $"INSERT INTO {Quote(definition.Table)} ({string.Join(",", insertColumns.Select(Quote))}) OUTPUT INSERTED.{Quote(definition.KeyColumn)} VALUES ({string.Join(",", parameterNames)})";
                AddFieldParameters(command, fields, values, insertColumns, definition);
                var insertedId = await command.ExecuteScalarAsync();
                await transaction.CommitAsync();
                return new ServiceResultVm
                {
                    Success = true,
                    Message = "تمت إضافة السجل بنجاح.",
                    EntityId = long.TryParse(Convert.ToString(insertedId, CultureInfo.InvariantCulture), out var parsedId) ? parsedId : null
                };
            }

            var assignments = fields.Select((field, index) => $"{Quote(field.Column)} = @p{index}").ToList();
            command.CommandText = $"UPDATE {Quote(definition.Table)} SET {string.Join(",", assignments)} WHERE {Quote(definition.KeyColumn)} = @id";
            AddFieldParameters(command, fields, values, fields.Select(x => x.Column).ToList(), definition);
            command.Parameters.AddWithValue("@id", ParseFilterValue(id));
            var affected = await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return affected > 0
                ? new ServiceResultVm { Success = true, Message = "تم تحديث السجل بنجاح." }
                : Failed("لم يتم العثور على السجل المطلوب.");
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync();
            return Failed("تعذر الحفظ لأن الرقم أو القيمة مستخدمة مسبقًا.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            System.Diagnostics.Trace.TraceError("Master data save failed: {0}", ex);
            return Failed("تعذر حفظ السجل. تحقق من القيم المطلوبة وعدم تكرار الرقم.");
        }
    }

    public async Task<ServiceResultVm> DeleteAsync(string sectionKey, string id)
    {
        if (string.Equals(sectionKey, "companies", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("لا يمكن حذف سجل بيانات المنشأة؛ يسمح بالتعديل فقط.");
        }

        await access.RequireAsync(SectionAccess.Master(sectionKey));
        var definition = GetSection(sectionKey);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"DELETE FROM {Quote(definition.Table)} WHERE {Quote(definition.KeyColumn)} = @id",
            connection);
        command.Parameters.AddWithValue("@id", ParseFilterValue(id));

        try
        {
            var affected = await command.ExecuteNonQueryAsync();
            return affected > 0
                ? new ServiceResultVm { Success = true, Message = "تم حذف السجل بنجاح." }
                : Failed("لم يتم العثور على السجل المطلوب.");
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            return Failed("لا يمكن حذف السجل لوجود سجلات أخرى مرتبطة به.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Master data delete failed: {0}", ex);
            return Failed("تعذر حذف السجل. تحقق من عدم ارتباطه بسجلات أخرى.");
        }
    }

    private static void AddFieldParameters(
        SqlCommand command,
        IReadOnlyList<MasterFieldDefinition> fields,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<string> insertColumns,
        MasterSectionDefinition definition)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var parameterIndex = -1;
            for (var columnIndex = 0; columnIndex < insertColumns.Count; columnIndex++)
            {
                if (string.Equals(insertColumns[columnIndex], field.Column, StringComparison.OrdinalIgnoreCase))
                {
                    parameterIndex = columnIndex;
                    break;
                }
            }
            if (parameterIndex < 0)
            {
                continue;
            }
            values.TryGetValue(field.Column, out var rawValue);
            command.Parameters.AddWithValue($"@p{parameterIndex}", ToDatabaseValue(rawValue, field.Kind));
        }
    }

    private static object ToDatabaseValue(string? value, MasterFieldKind kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DBNull.Value;
        }

        return kind switch
        {
            MasterFieldKind.Integer or MasterFieldKind.Lookup => long.Parse(value, CultureInfo.InvariantCulture),
            MasterFieldKind.Decimal => decimal.Parse(value.Replace(",", string.Empty), CultureInfo.InvariantCulture),
            MasterFieldKind.Date => DateTime.Parse(value, CultureInfo.InvariantCulture),
            MasterFieldKind.Boolean => bool.Parse(value),
            MasterFieldKind.Image => Convert.FromBase64String(value.Contains(',') ? value[(value.IndexOf(',') + 1)..] : value),
            _ => value.Trim()
        };
    }

    private static object ParseFilterValue(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : value ?? string.Empty;

    private static string ToEditorString(object value, MasterFieldKind kind)
        => kind switch
        {
            MasterFieldKind.Date => Convert.ToDateTime(value, CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MasterFieldKind.Decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture),
            MasterFieldKind.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture).ToString().ToLowerInvariant(),
            MasterFieldKind.Password => string.Empty,
            MasterFieldKind.Image when value is byte[] bytes => ToImageDataUrl(bytes),
            _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
        };

    private static string ToImageDataUrl(byte[] bytes)
    {
        var mimeType = bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            ? "image/png"
            : bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
                ? "image/jpeg"
                : bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46
                    ? "image/gif"
                    : bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                      && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50
                        ? "image/webp"
                        : "image/bmp";
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    private static ServiceResultVm Failed(string message) => new() { Success = false, Message = message };
}

public static class MasterDataCatalog
{
    private static MasterFieldDefinition F(string column, string label, MasterFieldKind kind = MasterFieldKind.Text,
        bool required = false, bool table = false, string? lookupTable = null, string? lookupValue = null, string? lookupDisplay = null)
        => new(column, label, kind, required, table, lookupTable, lookupValue, lookupDisplay);

    private static MasterSectionDefinition S(string key, string title, string area, string category, string table,
        string keyColumn, bool identity, string icon, params MasterFieldDefinition[] fields)
        => new(key, title, area, category, table, keyColumn, identity, icon, fields);

    public static readonly IReadOnlyDictionary<string, MasterSectionDefinition> Sections =
        new[]
        {
            S("users", "المستخدمون", "settings", "المستخدمون والصلاحيات", "USER", "UserID", true, "bi-people",
                F("UserName", "اسم المستخدم", required: true, table: true), F("UserPass", "كلمة المرور", MasterFieldKind.Password, true),
                F("Main", "الشاشة الرئيسية", MasterFieldKind.Boolean, table: true), F("GL", "قيود اليومية", MasterFieldKind.Boolean),
                F("RC", "سندات القبض", MasterFieldKind.Boolean), F("PM", "سندات الصرف", MasterFieldKind.Boolean),
                F("INV", "الفواتير", MasterFieldKind.Boolean), F("Cards", "جهات التعامل", MasterFieldKind.Boolean),
                F("Reports", "التقارير", MasterFieldKind.Boolean), F("Queries", "الاستعلامات", MasterFieldKind.Boolean),
                F("EmpCard", "الموظفون", MasterFieldKind.Boolean), F("Holidays", "الإجازات", MasterFieldKind.Boolean),
                F("Payroll", "الرواتب", MasterFieldKind.Boolean), F("Setting", "الإعدادات", MasterFieldKind.Boolean),
                F("Users", "إدارة المستخدمين", MasterFieldKind.Boolean), F("Data", "البيانات", MasterFieldKind.Boolean),
                F("Designer", "المصمم", MasterFieldKind.Boolean), F("Archive", "الأرشيف", MasterFieldKind.Boolean)),

            S("group1", "تصنيف الحسابات 1", "settings", "تصنيف الحسابات", "GROUP1", "GroupID", true, "bi-tags",
                F("Group1Name", "اسم التصنيف 1", required: true, table: true)),
            S("group2", "تصنيف الحسابات 2", "settings", "تصنيف الحسابات", "GROUP2", "GroupID", true, "bi-tags-fill",
                F("Group2Name", "اسم التصنيف 2", required: true, table: true)),
            S("center1", "مركز التكلفة 1", "settings", "مراكز التكلفة", "CENTER1", "CenterID", true, "bi-bullseye",
                F("Center1Name", "اسم المركز 1", required: true, table: true)),
            S("center2", "مركز التكلفة 2", "settings", "مراكز التكلفة", "CENTER2", "CenterID", true, "bi-diagram-2",
                F("Center2Name", "اسم المركز 2", required: true, table: true)),
            S("center3", "مركز تكلفة فرعي", "settings", "مراكز التكلفة", "CENTER3", "CenterID", true, "bi-diagram-3",
                F("Center3Name", "اسم المركز الفرعي", required: true, table: true)),
            S("job-titles", "الوظائف", "settings", "الموارد البشرية", "JobTitle", "JobTitleID", true, "bi-briefcase",
                F("JobTitle", "المسمى الوظيفي", required: true, table: true)),
            S("holiday-types", "أنواع الإجازات", "settings", "الموارد البشرية", "HolidayType", "HolidayID", true, "bi-calendar2-week",
                F("HolidayType", "نوع الإجازة", required: true, table: true)),
            S("banks", "البنوك", "settings", "التعريفات العامة", "Bank", "BankID", true, "bi-bank",
                F("BankName", "اسم البنك", required: true, table: true)),
            S("card-types", "أنواع جهات التعامل", "settings", "التعريفات العامة", "CardType", "CardTypeID", true, "bi-person-vcard",
                F("CardType", "نوع جهة التعامل", required: true, table: true)),
            S("periods", "الفترات المالية", "settings", "المالية والمنشأة", "Periods", "PeriodID", false, "bi-calendar-range",
                F("PeriodName", "اسم الفترة", required: true, table: true), F("StartDate", "تاريخ البداية", MasterFieldKind.Date, table: true),
                F("EndDate", "تاريخ النهاية", MasterFieldKind.Date, table: true), F("PeriodClose", "مغلقة", MasterFieldKind.Boolean, table: true),
                F("GLID", "بداية تسلسل القيد", MasterFieldKind.Integer), F("RCID", "بداية تسلسل سندات القبض", MasterFieldKind.Integer),
                F("PMID", "بداية تسلسل سندات الصرف", MasterFieldKind.Integer), F("SalesID", "بداية تسلسل فواتير المبيعات", MasterFieldKind.Integer),
                F("PurchID", "بداية تسلسل فواتير المشتريات", MasterFieldKind.Integer), F("PayrollID", "بداية تسلسل مسيرات الرواتب", MasterFieldKind.Integer)),
            S("vat", "الضريبة", "settings", "المالية والمنشأة", "VAT", "Id", true, "bi-percent",
                F("TaxRate", "نسبة الضريبة", MasterFieldKind.Decimal, required: true, table: true),
                F("SalesTaxACC", "حساب ضريبة المبيعات", MasterFieldKind.Lookup, table: true, lookupTable: "ACC", lookupValue: "ACCID", lookupDisplay: "ACC"),
                F("PurchTaxACC", "حساب ضريبة المشتريات", MasterFieldKind.Lookup, table: true, lookupTable: "ACC", lookupValue: "ACCID", lookupDisplay: "ACC")),
            S("companies", "بيانات المنشأة", "settings", "المالية والمنشأة", "COMPANY", "CompID", true, "bi-buildings",
                F("Comp", "اسم المنشأة", required: true, table: true), F("CompTel", "الهاتف", table: true),
                F("CompAddress", "العنوان", table: true), F("VAT", "الرقم الضريبي", table: true), F("CR", "السجل التجاري", table: true),
                F("Pic", "شعار المنشأة", MasterFieldKind.Image),
                F("Header", "رأس التقارير"), F("Footer", "تذييل التقارير"), F("DBPath", "مسار قاعدة البيانات"),
                F("ReportsPath", "مسار التقارير"), F("AttachPath", "مسار المرفقات"), F("BackupPath", "مسار النسخ الاحتياطي"),
                F("Skin", "السمة"), F("Notes", "ملاحظات", MasterFieldKind.Multiline)),

            S("employees", "تعريف الموظفين", "hr", "الموارد البشرية", "EmpCard", "EmpID", true, "bi-person-badge",
                F("EmpCode", "كود الموظف", MasterFieldKind.Integer, required: true, table: true), F("EmpName", "اسم الموظف", required: true, table: true),
                F("JobTitle", "المسمى الوظيفي", MasterFieldKind.Lookup, table: true, lookupTable: "JobTitle", lookupValue: "JobTitleID", lookupDisplay: "JobTitle"),
                F("Department", "الإدارة", table: true), F("Section", "القسم", table: true), F("EmpStatus", "حالة الموظف", table: true),
                F("Gender", "الجنس"), F("Nationality", "الجنسية"), F("Religion", "الديانة"), F("BirthDate", "تاريخ الميلاد", MasterFieldKind.Date),
                F("Marital", "الحالة الاجتماعية"), F("ChildNo", "عدد الأبناء", MasterFieldKind.Integer), F("Education", "التعليم"),
                F("Qualification", "المؤهل"), F("Specialisation", "التخصص"), F("QDate", "تاريخ المؤهل", MasterFieldKind.Date),
                F("QGrade", "التقدير"), F("CardNo", "رقم الهوية"), F("IDType", "نوع الهوية"), F("CardDate", "تاريخ الهوية", MasterFieldKind.Date),
                F("CardPlace", "مكان الإصدار"), F("CardExpire", "انتهاء الهوية", MasterFieldKind.Date), F("PassNo", "رقم الجواز", MasterFieldKind.Integer),
                F("PassDate", "تاريخ الجواز", MasterFieldKind.Date), F("PassPlace", "مكان إصدار الجواز"), F("PassExpire", "انتهاء الجواز", MasterFieldKind.Date),
                F("ContractType", "نوع العقد"), F("ContractDate", "تاريخ العقد", MasterFieldKind.Date), F("HiringDate", "تاريخ التعيين", MasterFieldKind.Date),
                F("EndDate", "تاريخ نهاية الخدمة", MasterFieldKind.Date), F("WorkHours", "ساعات العمل", MasterFieldKind.Integer),
                F("BasicSalary", "الراتب الأساسي", MasterFieldKind.Decimal, table: true), F("HousAllow", "بدل السكن", MasterFieldKind.Decimal),
                F("TransAllow", "بدل النقل", MasterFieldKind.Decimal), F("OtherAllow", "بدلات أخرى", MasterFieldKind.Decimal),
                F("Insurance", "التأمين", MasterFieldKind.Decimal), F("SalaryStatus", "حالة الراتب"),
                F("AccountNo", "رقم الحساب البنكي"), F("BankName", "البنك"), F("BankBranch", "فرع البنك"),
                F("HolidayBalance", "رصيد الإجازات", MasterFieldKind.Integer), F("Mobile", "الجوال", table: true), F("Email", "البريد الإلكتروني"),
                F("Address1", "العنوان", MasterFieldKind.Multiline), F("Notes", "ملاحظات", MasterFieldKind.Multiline)),

            S("holidays", "إجازات الموظفين", "hr", "الموارد البشرية", "Holidays", "HolidayID", true, "bi-calendar-check",
                F("EmpID", "الموظف", MasterFieldKind.Lookup, required: true, table: true, "EmpCard", "EmpID", "EmpName"),
                F("HolidayType", "نوع الإجازة", MasterFieldKind.Lookup, required: true, table: true, "HolidayType", "HolidayID", "HolidayType"),
                F("HolidayDate", "تاريخ الطلب", MasterFieldKind.Date, table: true), F("HolFrom", "من تاريخ", MasterFieldKind.Date, table: true),
                F("HolTo", "إلى تاريخ", MasterFieldKind.Date, table: true), F("PeriodAdd", "مدة الإضافة", MasterFieldKind.Integer),
                F("PeriodDisc", "مدة الخصم", MasterFieldKind.Integer), F("HolidayDoc", "المستند"), F("Notes", "ملاحظات", MasterFieldKind.Multiline)),

            S("payroll", "مسيرات الرواتب", "hr", "الموارد البشرية", "Payroll", "PayrollID", true, "bi-cash-stack",
                F("PayrollDate", "تاريخ المسير", MasterFieldKind.Date, required: true, table: true),
                F("PayrollDesc", "وصف المسير", required: true, table: true), F("Archive", "مؤرشف", MasterFieldKind.Boolean, table: true),
                F("Info", "معلومات", table: true), F("PayrollNotes", "ملاحظات", MasterFieldKind.Multiline)),
            S("payroll-trans", "تفاصيل مسير الرواتب", "hr", "الموارد البشرية", "PayrollTrans", "PayrollTransID", true, "bi-list-check",
                F("PayrollID", "مسير الرواتب", MasterFieldKind.Lookup, required: true, table: true, "Payroll", "PayrollID", "PayrollDesc"),
                F("EmpID", "الموظف", MasterFieldKind.Lookup, required: true, table: true, "EmpCard", "EmpID", "EmpName"),
                F("BasicSalary", "الراتب الأساسي", MasterFieldKind.Decimal, table: true), F("WorkDays", "أيام العمل", MasterFieldKind.Integer, table: true),
                F("HousAllow", "بدل السكن", MasterFieldKind.Decimal), F("TransAllow", "بدل النقل", MasterFieldKind.Decimal),
                F("OtherAllow", "بدلات أخرى", MasterFieldKind.Decimal), F("Reward", "مكافأة", MasterFieldKind.Decimal),
                F("Bonus", "إضافي", MasterFieldKind.Decimal), F("Total", "الإجمالي", MasterFieldKind.Decimal, table: true),
                F("Loans", "قروض", MasterFieldKind.Decimal), F("Delay", "تأخير", MasterFieldKind.Decimal),
                F("Insurance", "تأمين", MasterFieldKind.Decimal), F("OtherDeduction", "خصومات أخرى", MasterFieldKind.Decimal),
                F("Net", "الصافي", MasterFieldKind.Decimal, table: true), F("Notes", "ملاحظات", MasterFieldKind.Multiline)),

            S("cards", "جهات التعامل", "cards", "جهات التعامل", "CARD", "CardID", true, "bi-person-vcard-fill",
                F("CardNum", "رقم الجهة", MasterFieldKind.Integer, table: true), F("Title", "اللقب", table: true),
                F("CardName", "اسم جهة التعامل", required: true, table: true),
                F("CardType", "نوع الجهة", MasterFieldKind.Lookup, table: true, lookupTable: "CardType", lookupValue: "CardTypeID", lookupDisplay: "CardType"),
                F("Phone1", "الهاتف", table: true), F("Email", "البريد الإلكتروني", table: true), F("Active", "نشط", MasterFieldKind.Boolean, table: true),
                F("Address1", "العنوان 1"), F("Address2", "العنوان 2"), F("Phone2", "هاتف إضافي"), F("Fax", "الفاكس"),
                F("Contact", "مسؤول الاتصال"), F("Ref1", "مرجع 1"), F("Ref2", "مرجع 2"), F("VAT", "الرقم الضريبي"),
                F("Notes", "ملاحظات", MasterFieldKind.Multiline))
        }.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
}
