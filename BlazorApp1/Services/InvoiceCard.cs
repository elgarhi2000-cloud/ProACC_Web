using BlazorApp1.Data.ProAcc;
using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Services;

public partial class AccountingDataService
{
    public async Task<CardEntity?> GetInvoiceCardAsync(int cardId)
    {
        await access.RequireAsync("INV");
        return await dbContext.Cards.AsNoTracking()
            .SingleOrDefaultAsync(card => card.CardID == cardId);
    }
}
