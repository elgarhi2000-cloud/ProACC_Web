using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Services;

public partial class AccountingDataService
{
    public async Task<JournalEntryExportVm?> GetSavedJournalExportAsync(int glid)
    {
        if (!sessionService.IsAuthenticated || !sessionService.SelectedPeriodId.HasValue) return null;
        await access.RequireEntryAsync(glid);
        var header = await dbContext.GLEntries.AsNoTracking()
            .Include(x => x.Source).Include(x => x.Card).Include(x => x.Method)
            .Include(x => x.Period).Include(x => x.Center1Lookup).Include(x => x.Center2Lookup)
            .FirstOrDefaultAsync(x => x.GLID == glid && x.PeriodID == sessionService.SelectedPeriodId);
        if (header is null) return null;
        var lines = await dbContext.Transactions.AsNoTracking().Where(x => x.GLID == glid)
            .Include(x => x.Account).Include(x => x.Center3Lookup).OrderBy(x => x.TransID).ToListAsync();
        var settings = await GetSettingsDataAsync();
        return new JournalEntryExportVm
        {
            CompanyLogo = settings.CompanyLogo, PrintedBy = sessionService.CurrentUsername,
            CompanyName = settings.CompanyName, CompanyAddress = settings.CompanyAddress,
            CompanyPhone = settings.CompanyPhone, CommercialRecord = settings.CommercialRecord, TaxNumber = settings.TaxNumber,
            EntryId = header.GLID, SerialNumber = header.GLSN, EntryDate = header.GLDate,
            Period = header.Period?.PeriodName ?? header.PeriodID?.ToString() ?? "",
            Source = header.Source?.Source ?? "", Reference = header.GLRef, Description = header.GLDesc,
            Card = header.Card?.CardName ?? "", Bank = header.BankName ?? "", PaymentMethod = header.Method?.MethodName ?? "",
            Center1 = header.Center1Lookup?.CenterName ?? "", Center2 = header.Center2Lookup?.CenterName ?? "",
            UserInfo = header.Info, IsArchived = header.Archive == true,
            Lines = lines.Select((line, index) => new JournalEntryExportLineVm
            {
                Number = index + 1, AccountCode = line.ACCID?.ToString() ?? "", AccountName = line.Account?.Name ?? "",
                Description = line.TransDesc, Debit = line.DR ?? 0, Credit = line.CR ?? 0,
                DocumentReference = line.DocRef, DocumentDate = line.DocDate, Center = line.Center3Lookup?.CenterName ?? ""
            }).ToList()
        };
    }
}
