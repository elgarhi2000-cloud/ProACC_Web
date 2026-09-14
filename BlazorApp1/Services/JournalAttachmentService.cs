using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Services;

public sealed class JournalAttachmentService(IProAccDbContextAccessor accessor, ISessionService session, IUserAccessService access)
{
    public const long MaxFileSize = 25 * 1024 * 1024;
    public async Task<string> GetFolderAsync(int entryId)
    {
        if (!session.IsAuthenticated || !session.SelectedPeriodId.HasValue)
            throw new InvalidOperationException("يرجى تسجيل الدخول واختيار الفترة المالية.");
        await access.RequireAsync("Archive");
        await access.RequireEntryAsync(entryId);
        var db = accessor.Context;
        var entry = await db.GLEntries.AsNoTracking().FirstOrDefaultAsync(g => g.GLID == entryId && g.PeriodID == session.SelectedPeriodId);
        if (entry is null) throw new InvalidOperationException("احفظ القيد أولاً وتأكد من الفترة المالية الحالية.");
        var root = await db.Companies.AsNoTracking().OrderBy(c => c.CompID).Select(c => c.AttachPath).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("حدد مسارًا كاملاً للمرفقات في إعدادات المنشأة (AttachPath).");
        var period = await db.Periods.AsNoTracking().FirstAsync(p => p.PeriodID == entry.PeriodID);
        var periodName = (period.StartDate?.Year ?? period.PeriodID).ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            // AttachPath is an administrator-controlled storage root. It may legitimately be
            // a mapped/network/cloud drive, which Windows can expose as a reparse point.
            return Path.Combine(Path.GetFullPath(root.Trim()), $"{periodName}-{entryId}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            throw new InvalidOperationException("مسار المرفقات المحفوظ في إعدادات المنشأة غير صالح.", ex);
        }
    }

    public async Task SaveAsync(int entryId, string originalName, long size, Stream content)
    {
        if (size > MaxFileSize) throw new InvalidOperationException("الحد الأقصى للملف 25 ميجابايت.");
        var folder = await GetFolderAsync(entryId);
        var name = Path.GetFileName(originalName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("اسم الملف غير صالح.");
        string target;
        try
        {
            Directory.CreateDirectory(folder);
            target = Path.Combine(folder, name);
            if (File.Exists(target)) target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}_{Guid.NewGuid():N}{Path.GetExtension(name)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            throw StorageUnavailable(folder, ex);
        }

        var created = false;
        try
        {
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            created = true;
            await content.CopyToAsync(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            if (created)
            {
                try { File.Delete(target); } catch { /* Preserve the original storage error. */ }
            }
            throw StorageUnavailable(folder, ex);
        }
    }

    public async Task<(string Folder, List<string> Files)> ListAsync(int entryId)
    {
        var folder = await GetFolderAsync(entryId);
        try
        {
            return (folder, Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Where(f => !File.GetAttributes(f).HasFlag(FileAttributes.ReparsePoint)).Select(Path.GetFileName).OfType<string>().OrderBy(f => f).ToList() : new());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            throw StorageUnavailable(folder, ex);
        }
    }

    public async Task<byte[]> ReadAsync(int entryId, string name)
    {
        if (name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidOperationException("اسم الملف غير صالح.");
        var path = Path.Combine(await GetFolderAsync(entryId), name);
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("الملف غير متاح.");
        if (new FileInfo(path).Length > MaxFileSize) throw new InvalidOperationException("حجم الملف يتجاوز حد العرض 25 ميجابايت.");
        return await File.ReadAllBytesAsync(path);
    }

    private static InvalidOperationException StorageUnavailable(string folder, Exception inner) =>
        new($"تعذر الوصول إلى مسار المرفقات: {folder}. تأكد من اتصال القرص ومنح حساب التطبيق صلاحية الكتابة.", inner);
}
