using BlazorApp1.Data.ProAcc;
using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Services;

public partial class AccountingDataService
{
    public async Task<JournalEntryDetailVm> CreateOpeningEntryDraftAsync()
    {
        await access.RequireAsync("GL");
        if (!sessionService.IsAuthenticated || !sessionService.SelectedPeriodId.HasValue)
            throw new InvalidOperationException("يجب تسجيل الدخول واختيار الفترة المالية أولاً.");

        var current = await dbContext.Periods.AsNoTracking()
            .SingleAsync(p => p.PeriodID == sessionService.SelectedPeriodId.Value);
        if (!current.StartDate.HasValue || current.PeriodClose == true)
            throw new InvalidOperationException("الفترة الحالية مغلقة أو لا يوجد لها تاريخ بداية.");

        var start = current.StartDate.Value.Date;
        var previousStart = start.AddYears(-1);
        var previousEnd = start.AddDays(-1);
        var candidates = await dbContext.Periods.AsNoTracking()
            .Where(p => p.PeriodID != current.PeriodID && p.StartDate.HasValue && p.EndDate.HasValue
                && p.StartDate.Value.Date == previousStart && p.EndDate.Value.Date == previousEnd)
            .ToListAsync();
        if (candidates.Count != 1)
            throw new InvalidOperationException("يجب وجود فترة مالية واحدة للسنة السابقة مباشرة، تنتهي قبل بداية الفترة الحالية بيوم واحد.");
        var previous = candidates[0];
        if (await dbContext.GLEntries.AsNoTracking().AnyAsync(g => g.PeriodID == current.PeriodID && g.SourceID == 4))
            throw new InvalidOperationException("يوجد قيد افتتاحي للفترة الحالية. عدّل القيد الموجود لتجنب تكرار الأرصدة.");
        if (!await dbContext.Sources.AnyAsync(s => s.SourceID == 4))
            throw new InvalidOperationException("مصدر القيد الافتتاحي غير معرّف في النظام.");

        var balanceAccounts = from account in dbContext.Accounts
            join category in dbContext.Categories on account.CategID equals category.CategID
            join accType in dbContext.AccTypes on category.AccTypeID equals accType.ACCTypeID
            join type in dbContext.Types on accType.TypeID equals type.TypeID
            join level1 in dbContext.Level1s on type.Level1ID equals level1.Level1ID
            join level0 in dbContext.Level0s on level1.Level0ID equals level0.Level0ID
            join direction in dbContext.Directions on level0.DirectionID equals direction.DirectionID
            where direction.DirectionName != null && direction.DirectionName.Trim().ToUpper() == "BALANCE"
            select account.ACCID;

        var balances = await dbContext.Transactions.AsNoTracking()
            .Where(t => t.GL != null && t.GL.PeriodID == previous.PeriodID
                && t.ACCID.HasValue && balanceAccounts.Contains(t.ACCID.Value))
            .GroupBy(t => t.ACCID!.Value)
            .Select(g => new { AccountId = g.Key, Net = g.Sum(t => (t.DR ?? 0m) - (t.CR ?? 0m)) })
            .Where(x => x.Net != 0m).OrderBy(x => x.AccountId).ToListAsync();
        if (balances.Count == 0)
            throw new InvalidOperationException("لا توجد أرصدة غير صفرية لحسابات BALANCE في السنة السابقة مباشرة.");

        return new JournalEntryDetailVm
        {
            Header = new GlEntity { PeriodID = current.PeriodID, SourceID = 4, GLDate = start,
                GLDesc = $"أرصدة افتتاحية من {previous.PeriodName}" },
            Transactions = balances.Select(x => new TransEntity
            {
                ACCID = x.AccountId, DR = Math.Max(x.Net, 0m), CR = Math.Max(-x.Net, 0m),
                DocDate = start, TransDesc = $"رصيد مرحل من {previous.PeriodName}"
            }).ToList()
        };
    }
}
