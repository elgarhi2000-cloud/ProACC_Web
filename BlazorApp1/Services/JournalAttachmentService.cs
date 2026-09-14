using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Services;

public sealed class JournalAttachmentService(IProAccDbContextAccessor accessor, ISessionService session, IUserAccessService access)
{
    public sealed record LocalContext(string Scope, string ConfiguredPath, string CompanyFolder, string EntryFolder);

    public async Task<LocalContext> GetLocalContextAsync(int entryId)
    {
        if (!session.IsAuthenticated || !session.SelectedPeriodId.HasValue)
            throw new InvalidOperationException("يرجى تسجيل الدخول واختيار الفترة المالية.");
        await access.RequireAsync("Archive");
        await access.RequireEntryAsync(entryId);
        var db = accessor.Context;
        var entry = await db.GLEntries.AsNoTracking().FirstOrDefaultAsync(x => x.GLID == entryId && x.PeriodID == session.SelectedPeriodId);
        if (entry is null) throw new InvalidOperationException("احفظ القيد أولاً وتأكد من الفترة المالية الحالية.");
        var configuredPath = await db.Companies.AsNoTracking().OrderBy(x => x.CompID).Select(x => x.AttachPath).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("حدد مجلد المرفقات في إعدادات المنشأة أولاً، مثل C:\\Attach.");
        var period = await db.Periods.AsNoTracking().FirstAsync(x => x.PeriodID == entry.PeriodID);
        var year = (period.StartDate?.Year ?? period.PeriodID).ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        var company = "proacc-" + Hash(session.SelectedDatabaseName ?? session.SelectedDatabaseKey ?? "")[..16];
        var scope = Hash($"{session.CurrentUsername}|{session.SelectedDatabaseKey}|{configuredPath.Trim()}");
        return new(scope, configuredPath.Trim(), company, $"{year}-{entryId}");
    }
}
