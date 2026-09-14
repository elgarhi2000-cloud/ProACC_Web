using BlazorApp1.Data.ProAcc;

namespace BlazorApp1.Services;

public static class JournalValidation
{
    public static string? ValidateLines(IEnumerable<TransEntity> transactions)
    {
        var rows = transactions.ToList();
        if (rows.Any(x => !x.ACCID.HasValue && ((x.DR ?? 0) != 0 || (x.CR ?? 0) != 0)))
            return "اختر حسابًا لكل سطر يحتوي على مبلغ.";
        var used = rows.Where(x => x.ACCID.HasValue).ToList();
        if (used.Count < 2) return "يجب أن يحتوي القيد على طرفين على الأقل.";
        if (used.Any(x => (x.DR ?? 0) < 0 || (x.CR ?? 0) < 0))
            return "لا يمكن إدخال مبالغ سالبة في المدين أو الدائن.";
        if (used.Any(x => (x.DR ?? 0) > 0 && (x.CR ?? 0) > 0))
            return "اختر المدين أو الدائن لكل سطر، وليس كليهما.";
        if (used.Any(x => decimal.Round(x.DR ?? 0, 2) != (x.DR ?? 0)
            || decimal.Round(x.CR ?? 0, 2) != (x.CR ?? 0)))
            return "أدخل المبالغ بمنزلتين عشريتين كحد أقصى لتجنب اختلاف التوازن بعد الحفظ.";
        var debit = used.Sum(x => x.DR ?? 0);
        var credit = used.Sum(x => x.CR ?? 0);
        if (debit <= 0 || debit != credit) return "يجب أن يتساوى إجمالي المدين والدائن وأن يكون أكبر من صفر.";
        return null;
    }

    public static string? ValidatePeriod(PeriodEntity? period, DateTime? date)
    {
        if (period is null) return "الفترة المالية المحددة غير موجودة.";
        if (period.PeriodClose == true) return "الفترة المالية مغلقة؛ لا يمكن حفظ أو تعديل قيودها.";
        if (!date.HasValue) return "حدد تاريخ القيد.";
        if ((period.StartDate.HasValue && date.Value.Date < period.StartDate.Value.Date)
            || (period.EndDate.HasValue && date.Value.Date > period.EndDate.Value.Date))
            return "يجب أن يقع تاريخ القيد ضمن الفترة المالية المحددة.";
        return null;
    }
}
