using BlazorApp1.Data.ProAcc;
using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Services;

public partial class AccountingDataService
{
    private async Task<bool> CanCopyFromPeriodAsync(int periodId)
    {
        if (!sessionService.IsAuthenticated || !sessionService.SelectedPeriodId.HasValue) return false;
        if (periodId == sessionService.SelectedPeriodId) return true;
        var current = await dbContext.Periods.AsNoTracking()
            .SingleOrDefaultAsync(p => p.PeriodID == sessionService.SelectedPeriodId);
        return current?.StartDate is DateTime start && await dbContext.Periods.AsNoTracking()
            .AnyAsync(p => p.PeriodID == periodId && p.EndDate.HasValue && p.EndDate < start);
    }

    public async Task<IReadOnlyList<JournalEntryVm>> GetCopyableJournalEntriesAsync(int periodId)
    {
        await access.RequireAsync("GL", "RC", "PM", "INV");
        if (!await CanCopyFromPeriodAsync(periodId)) return [];
        return await AccessibleEntries(dbContext.GLEntries.AsNoTracking().Where(g => g.PeriodID == periodId))
            .OrderByDescending(g => g.GLDate).ThenByDescending(g => g.GLID)
            .Select(g => new JournalEntryVm
            {
                GLID = g.GLID, SerialNo = g.GLSN, Date = g.GLDate,
                Description = g.GLDesc ?? "", SourceName = g.Source != null ? (g.Source.Source ?? "") : "",
                CardName = g.Card != null ? (g.Card.CardName ?? g.Card.Title ?? "") : "",
                Debit = g.Transactions.Sum(t => t.DR ?? 0m), Credit = g.Transactions.Sum(t => t.CR ?? 0m)
            }).ToListAsync();
    }

    public async Task<JournalEntryDetailVm?> GetJournalEntryForCopyAsync(int glid, int periodId)
    {
        if (!await CanCopyFromPeriodAsync(periodId)) return null;
        await access.RequireEntryAsync(glid, periodId);
        var header = await dbContext.GLEntries.AsNoTracking()
            .SingleOrDefaultAsync(g => g.GLID == glid && g.PeriodID == periodId);
        if (header is null) return null;
        return new JournalEntryDetailVm { Header = header, Transactions = await dbContext.Transactions.AsNoTracking()
            .Where(t => t.GLID == glid).OrderBy(t => t.TransID).ToListAsync() };
    }
}
