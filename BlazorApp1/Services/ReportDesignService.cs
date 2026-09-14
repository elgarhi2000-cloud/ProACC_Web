using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorApp1.Services;

public sealed class ReportElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "text";
    public string Zone { get; set; } = "before";
    public string Text { get; set; } = "نص جديد";
    public string Field { get; set; } = "companyName";
    public int FontSize { get; set; } = 12;
    public string Color { get; set; } = "#263247";
    public string Align { get; set; } = "right";
    public bool Bold { get; set; }
}

public sealed class ReportDesign
{
    public string CompanyKey { get; set; } = "";
    public string Kind { get; set; } = "journal";
    public string Name { get; set; } = "التصميم الافتراضي";
    public string Revision { get; set; } = "";
    public int FontSize { get; set; } = 12;
    public int Margin { get; set; } = 12;
    public string Accent { get; set; } = "#5948d6";
    public List<ReportElement> Elements { get; set; } = [];
    public static ReportDesign Default(string kind) => new()
    {
        Kind = kind,
        Elements = [
            new() { Kind = "logo", Zone = "header", Text = "" },
            new() { Kind = "field", Zone = "header", Field = "companyName", Text = "", FontSize = 22, Bold = true },
            new() { Kind = "field", Zone = "header", Field = "companyDetails", Text = "", FontSize = 10 },
            new() { Kind = "field", Zone = "footer", Field = "printedBy", Text = "المستخدم: ", FontSize = 10 },
            new() { Kind = "field", Zone = "footer", Field = "printedAt", Text = "تاريخ الطباعة: ", FontSize = 10, Align = "left" }
        ]
    };
}

public sealed class ReportDesignService(IWebHostEnvironment environment, ISessionService session, IUserAccessService access)
{
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["journal"] = "قيود اليومية", ["receipt"] = "سندات القبض", ["payment"] = "سندات الصرف",
        ["sales"] = "فواتير المبيعات", ["purchase"] = "فواتير المشتريات", ["general-journal"] = "اليومية العامة",
        ["trial-balance"] = "ميزان المراجعة", ["chart"] = "دليل الحسابات",
        ["employees"] = "الموظفون", ["holidays"] = "الإجازات", ["payroll"] = "مسيرات الرواتب",
        ["employee-reports"] = "تقارير الموظفين", ["account-statement"] = "كشف حساب",
        ["financial-position"] = "المركز المالي", ["income-statement"] = "قائمة الدخل",
        ["cost-centers"] = "مراكز التكلفة", ["reports"] = "ملخص التقارير"
    };
    public static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>
    {
        ["companyDetails"] = "عنوان المنشأة والهاتف والسجل والرقم الضريبي",
        ["companyName"] = "اسم المنشأة", ["companyAddress"] = "عنوان المنشأة", ["companyPhone"] = "هاتف المنشأة",
        ["commercialRecord"] = "السجل التجاري", ["taxNumber"] = "الرقم الضريبي", ["printedBy"] = "اسم المستخدم",
        ["printedAt"] = "تاريخ ووقت الطباعة", ["entryId"] = "رقم القيد", ["reference"] = "المرجع",
        ["description"] = "بيان السجل", ["card"] = "جهة التعامل", ["periodName"] = "الفترة المالية"
    };
    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string FilePath(string kind)
    {
        if (!Kinds.ContainsKey(kind)) throw new ArgumentException("نوع المطبوعة غير معروف.");
        if (!session.IsAuthenticated || string.IsNullOrWhiteSpace(session.SelectedDatabaseKey)) throw new UnauthorizedAccessException();
        // A client can choose a report kind, never another company's storage path.
        var company = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session.SelectedDatabaseKey)));
        return Path.Combine(environment.ContentRootPath, "App_Data", "ReportDesigns", company, kind + ".json");
    }
    public async Task<ReportDesign> EditAsync(string kind)
    {
        await access.RequireAsync("Designer");
        var design = await ReadAsync(kind) ?? ReportDesign.Default(kind);
        design.CompanyKey = session.SelectedDatabaseKey!;
        return design;
    }
    public static string PrintPermission(string kind) => kind switch
    {
        "receipt" => "RC", "payment" => "PM", "sales" or "purchase" => "INV", "chart" => "Data",
        "employees" => "EmpCard", "holidays" => "Holidays", "payroll" => "Payroll",
        "journal" => "GL", _ when Kinds.ContainsKey(kind) => "Reports",
        _ => throw new ArgumentException("نوع المطبوعة غير معروف.")
    };
    public async Task<ReportDesign?> ForPrintAsync(string kind)
    {
        var permission = PrintPermission(kind);
        if (kind == "journal") await access.RequireAsync("GL", "RC", "PM", "INV", "Designer");
        else await access.RequireAsync(permission, "Designer");
        return await ReadAsync(kind) ?? ReportDesign.Default(kind);
    }
    private async Task<ReportDesign?> ReadAsync(string kind)
    {
        var path = FilePath(kind);
        if (!File.Exists(path)) return null;
        var design = JsonSerializer.Deserialize<ReportDesign>(await File.ReadAllTextAsync(path), Json) ?? throw new InvalidDataException("تعذر قراءة التصميم.");
        Validate(design);
        if (design.Kind != kind) throw new InvalidDataException("نوع التصميم المحفوظ غير مطابق.");
        if (design.CompanyKey != session.SelectedDatabaseKey) throw new InvalidDataException("التصميم غير مرتبط بالمنشأة الحالية.");
        return design;
    }
    public static void Validate(ReportDesign design)
    {
        if (!Kinds.ContainsKey(design.Kind) || string.IsNullOrWhiteSpace(design.Name) || design.Name.Length > 100 || design.FontSize is < 9 or > 16 || design.Margin is < 8 or > 22 || !Color(design.Accent))
            throw new ArgumentException("راجع اسم التصميم وحجم الخط (9–16) والهامش (8–22 مم) والألوان.");
        if (design.Elements is null || design.Elements.Count > 24 || design.Elements.Select(x => x.Id).Distinct().Count() != design.Elements.Count)
            throw new ArgumentException("الحد الأقصى 24 عنصرًا دون تكرار.");
        foreach (var element in design.Elements)
        {
            if (!new[] { "text", "field", "logo", "line" }.Contains(element.Kind) || !new[] { "header", "before", "after", "footer" }.Contains(element.Zone)
                || !new[] { "right", "center", "left" }.Contains(element.Align) || element.FontSize is < 9 or > 24 || !Color(element.Color)
                || element.Text is null || element.Text.Length > 500 || !Fields.ContainsKey(element.Field))
                throw new ArgumentException("يوجد عنصر غير صالح؛ النص بحد أقصى 500 حرف والخط من 9 إلى 24.");
            if (element.Zone == "footer" && (element.FontSize > 12 || element.Text.Length > 100 || element.Kind == "logo"))
                throw new ArgumentException("التذييل يدعم النصوص والحقول والفواصل بخط حتى 12 ونص حتى 100 حرف.");
            if (element.Zone is "header" or "footer" && element.Kind == "field" && element.Field == "description")
                throw new ArgumentException("ضع بيان السجل في متن التقرير لإتاحة امتداده إلى عدة صفحات.");
            if (element.Zone == "footer" && element.Kind == "field" && element.Field is "companyDetails" or "companyAddress" or "card")
                throw new ArgumentException("ضع بيانات المنشأة التفصيلية وجهة التعامل في الرأس أو المتن.");
            if (element.Zone == "header" && element.Text.Length > 160)
                throw new ArgumentException("نص عنصر الرأس بحد أقصى 160 حرفًا؛ انقل النص الطويل إلى المتن.");
        }
        if (design.Elements.Count(x => x.Zone == "header") > 6 || design.Elements.Count(x => x.Zone == "footer") > 3)
            throw new ArgumentException("الرأس بحد أقصى 6 عناصر والتذييل 3 عناصر للحفاظ على مساحة البيانات.");
    }
    private static bool Color(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
    public async Task<string> SaveAsync(ReportDesign design)
    {
        await access.RequireAsync("Designer");
        Validate(design);
        if (design.CompanyKey != session.SelectedDatabaseKey) throw new InvalidOperationException("تغيّرت المنشأة. أعد تحميل التصميم قبل حفظه.");
        var path = FilePath(design.Kind);
        await FileLock.WaitAsync();
        try
        {
            if (design.CompanyKey != session.SelectedDatabaseKey) throw new InvalidOperationException("تغيّرت المنشأة. أعد تحميل التصميم.");
            var existing = await ReadAsync(design.Kind);
            if ((existing?.Revision ?? "") != design.Revision) throw new InvalidOperationException("تم تعديل التصميم في جلسة أخرى. أعد تحميله قبل الحفظ.");
            var copy = JsonSerializer.Deserialize<ReportDesign>(JsonSerializer.Serialize(design, Json), Json)!;
            copy.Revision = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(copy, Json)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return copy.Revision;
        }
        finally { FileLock.Release(); }
    }
}
