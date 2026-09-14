namespace BlazorApp1.Services;

public interface IAccountingDataService
{
    Task<BlazorApp1.Data.ProAcc.CardEntity?> GetInvoiceCardAsync(int cardId);
    Task<JournalEntryDetailVm> CreateOpeningEntryDraftAsync();
    Task<IReadOnlyList<JournalEntryVm>> GetCopyableJournalEntriesAsync(int periodId);
    Task<JournalEntryDetailVm?> GetJournalEntryForCopyAsync(int glid, int periodId);
    Task<IReadOnlyList<ChartParentOptionVm>> GetChartDirectionsAsync();
    Task<IReadOnlyList<ChartParentOptionVm>> GetChartGroupsAsync(int group);
    Task<ChartEditVm?> GetChartEditAsync(int level, long id);
    Task<ServiceResultVm> SaveChartAsync(ChartEditVm request);
    Task<ServiceResultVm> DeleteChartAsync(int level, long id);
    Task<IReadOnlyList<CostCenterOptionVm>> GetCostCenterOptionsAsync(int level);
    Task<IReadOnlyList<CostCenterLineVm>> GetCostCenterReportAsync(int level, int? centerId, DateTime? fromDate, DateTime? toDate);
    Task<DashboardDataVm> GetDashboardDataAsync();
    Task<IReadOnlyList<JournalEntryVm>> GetJournalEntriesAsync(int take = 100);
    Task<IReadOnlyList<JournalEntryVm>> GetJournalEntriesBySourceAsync(int sourceId, int take = 100);
    Task<IReadOnlyList<DocumentVm>> GetReceiptsAsync(int take = 100);
    Task<IReadOnlyList<DocumentVm>> GetPaymentsAsync(int take = 100);
    Task<IReadOnlyList<DocumentVm>> GetSalesInvoicesAsync(int take = 100);
    Task<IReadOnlyList<DocumentVm>> GetPurchaseInvoicesAsync(int take = 100);
    Task<ReportsDataVm> GetReportsDataAsync();
    Task<IReadOnlyList<GeneralJournalLineVm>> GetGeneralJournalAsync(
        int take = 0,
        DateTime? fromDate = null,
        DateTime? toDate = null);
    Task<TrialBalanceReportVm> GetTrialBalanceAsync(int? periodId = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<IReadOnlyList<FinancialPeriodOptionVm>> GetFinancialPeriodsAsync();
    Task<IReadOnlyList<AccountStatementAccountVm>> GetAccountStatementAccountsAsync();
    Task<AccountStatementVm?> GetAccountStatementAsync(long accountId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<FinancialStatementVm> GetFinancialStatementAsync(string directionName, DateTime? fromDate = null, DateTime? toDate = null);
    Task<SettingsDataVm> GetSettingsDataAsync();
    Task<IReadOnlyList<ChartTreeNodeVm>> GetChartOfAccountsTreeAsync();
    Task<IReadOnlyList<AccountCategoryVm>> GetAccountCategoriesAsync();
    Task<JournalEntryLookupsVm> GetJournalEntryLookupsAsync();
    Task<JournalEntryDetailVm?> GetJournalEntryDetailAsync(int glid);
    Task<JournalEntryExportVm?> GetSavedJournalExportAsync(int glid);
    Task<ServiceResultVm> SaveJournalEntryAsync(JournalEntryUpsertVm request);
    Task<ServiceResultVm> DeleteJournalEntryAsync(int glid);
}

public class CostCenterOptionVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CostCenterLineVm : GeneralJournalLineVm
{
    public int? CenterId { get; set; }
    public string CenterName { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Info { get; set; } = string.Empty;
}

public class GeneralJournalLineVm
{
    public int TransId { get; set; }
    public int? GLID { get; set; }
    public DateTime? GLDate { get; set; }
    public int? SerialNo { get; set; }
    public long? AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Net => Debit - Credit;
}

public class TrialBalanceRowVm
{
    public long AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string Level1 { get; set; } = string.Empty;
    public string Level2 { get; set; } = string.Empty;
    public string Level3 { get; set; } = string.Empty;
    public string Level4 { get; set; } = string.Empty;
    public string Level5 { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }
    public decimal PeriodBalance => PeriodDebit - PeriodCredit;
    public decimal TotalDebit => PeriodDebit + Math.Max(OpeningBalance, 0m);
    public decimal TotalCredit => PeriodCredit + Math.Max(-OpeningBalance, 0m);
    public decimal ClosingBalance => TotalDebit - TotalCredit;
}

public class TrialBalanceReportVm
{
    public int PeriodId { get; set; }
    public string PeriodName { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public List<TrialBalanceRowVm> Rows { get; set; } = new();
}

public class FinancialPeriodOptionVm
{
    public int PeriodId { get; set; }
    public string PeriodName { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsClosed { get; set; }
}

public class DashboardDataVm
{
    public byte[]? CompanyLogo { get; set; }
    public int JournalEntriesCount { get; set; }
    public int ReceiptsCount { get; set; }
    public int PaymentsCount { get; set; }
    public int SalesInvoicesCount { get; set; }
    public int PurchaseInvoicesCount { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public List<MonthlyPointVm> MonthlyFlow { get; set; } = new();
    public List<Level0BalanceVm> Level0Balances { get; set; } = new();
    public List<Level1BalanceVm> Level1Balances { get; set; } = new();
    public List<AccountBalanceVm> ChartAccountBalances { get; set; } = new();
    public List<MainCategoryBalanceVm> MainCategoryBalances { get; set; } = new();
    public List<TypeBalanceVm> TypeBalances { get; set; } = new();
    public List<JournalEntryVm> RecentEntries { get; set; } = new();
}

public class TypeBalanceVm
{
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Net => Debit - Credit;
}

public class MainCategoryBalanceVm
{
    public string CategoryKey { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Net { get; set; }
}

public class Level0BalanceVm
{
    public int Level0Id { get; set; }
    public string Level0Name { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Net => Debit - Credit;
}

public class AccountStatementAccountVm
{
    public long AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
}

public class AccountStatementVm
{
    public long AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public List<AccountStatementLineVm> Lines { get; set; } = new();
    public decimal TotalDebit => Lines.Sum(x => x.Debit);
    public decimal TotalCredit => Lines.Sum(x => x.Credit);
    public decimal ClosingBalance => OpeningBalance + TotalDebit - TotalCredit;
}

public class AccountStatementLineVm
{
    public int TransId { get; set; }
    public int? GLID { get; set; }
    public int? SerialNo { get; set; }
    public DateTime? Date { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}

public class FinancialStatementVm
{
    public string DirectionName { get; set; } = string.Empty;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int AccountsCount { get; set; }
    public decimal NetResult { get; set; }
    public decimal BalanceDifference { get; set; }
    public List<FinancialStatementNodeVm> Nodes { get; set; } = new();
}

public class FinancialStatementNodeVm
{
    public string Key { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool IsAccount { get; set; }
    public long? AccountId { get; set; }
    public decimal NetBalance { get; set; }
    public List<FinancialStatementNodeVm> Children { get; set; } = new();
}

public class Level1BalanceVm
{
    public int Level1Id { get; set; }
    public string Level1Name { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Net => Debit - Credit;
}

public class JournalEntryVm
{
    public string Info { get; set; } = string.Empty;
    public int GLID { get; set; }
    public int? SerialNo { get; set; }
    public int? SanadID { get; set; }
    public DateTime? Date { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class DocumentVm
{
    public int GLID { get; set; }
    public int? SerialNo { get; set; }
    public DateTime? Date { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Party { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class MonthlyPointVm
{
    public string Month { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class ReportsDataVm
{
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal Balance => TotalDebit - TotalCredit;
    public List<MonthlyPointVm> MonthlyFlow { get; set; } = new();
    public List<AccountBalanceVm> TopAccounts { get; set; } = new();
}

public class AccountBalanceVm
{
    public long AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Net => Debit - Credit;
}

public class SettingsDataVm
{
    public byte[]? CompanyLogo { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyPhone { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string CommercialRecord { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public decimal TaxRate { get; set; }
    public string ActivePeriodName { get; set; } = string.Empty;
    public DateTime? ActivePeriodStart { get; set; }
    public DateTime? ActivePeriodEnd { get; set; }
    public bool IsPeriodClosed { get; set; }
}

public class ChartTreeNodeVm
{
    public string Key { get; set; } = string.Empty;
    public string DisplayNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LevelTitle { get; set; } = string.Empty;
    public string? SecondaryText { get; set; }
    public bool IsAccountLeaf { get; set; }
    public int AccountsCount { get; set; }
    public long? AccountId { get; set; }
    public int? CategoryId { get; set; }
    public List<ChartTreeNodeVm> Children { get; set; } = new();
}

public class AccountCategoryVm
{
    public int CategID { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class AccountUpsertVm
{
    public long? OriginalACCID { get; set; }
    public long? ACCID { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? CategID { get; set; }
    public int? Group1 { get; set; }
    public int? Group2 { get; set; }
    public bool Active { get; set; } = true;
}

public class ServiceResultVm
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public long? EntityId { get; set; }
}

public class JournalEntryLookupsVm
{
    public List<BlazorApp1.Data.ProAcc.AccEntity> Accounts { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.SourceEntity> Sources { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.CardEntity> Cards { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.MethodEntity> Methods { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.BankEntity> Banks { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.Center1Entity> Centers1 { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.Center2Entity> Centers2 { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.Center3Entity> Centers3 { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.VatEntity> VatRates { get; set; } = new();
}

public class JournalEntryDetailVm
{
    public BlazorApp1.Data.ProAcc.GlEntity Header { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.TransEntity> Transactions { get; set; } = new();
}

public class JournalEntryUpsertVm
{
    public BlazorApp1.Data.ProAcc.GlEntity Header { get; set; } = new();
    public List<BlazorApp1.Data.ProAcc.TransEntity> Transactions { get; set; } = new();
}

