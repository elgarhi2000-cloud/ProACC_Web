using BlazorApp1.Data.ProAcc;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Services;

public partial class AccountingDataService(IProAccDbContextAccessor dbContextAccessor, ISessionService sessionService, IUserAccessService access, ILogger<AccountingDataService>? logger = null) : IAccountingDataService
{
    private ProAccDbContext dbContext => dbContextAccessor.Context;
    private static readonly string[] ReceiptKeywords = ["قبض", "receipt", "rc"];
    private static readonly string[] PaymentKeywords = ["صرف", "payment", "pm"];
    private static readonly string[] SalesKeywords = ["مبيعات", "sales", "inv"];
    private static readonly string[] PurchaseKeywords = ["مشتريات", "purchase", "purch"];

    public async Task<DashboardDataVm> GetDashboardDataAsync()
    {
        await access.RequireAsync("Main");
        var periodGlQuery = GetFilteredGlEntries();
        var counts = await periodGlQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Journal = g.Count(),
                Receipts = g.Count(x => x.SourceID == 2),
                Payments = g.Count(x => x.SourceID == 3),
                Sales = g.Count(x => x.SourceID == 5),
                Purchases = g.Count(x => x.SourceID == 6)
            })
            .FirstOrDefaultAsync();

        var accountHierarchy = await (
            from account in dbContext.Accounts.AsNoTracking()
            join category in dbContext.Categories.AsNoTracking()
                on account.CategID equals (int?)category.CategID into categoryJoin
            from category in categoryJoin.DefaultIfEmpty()
            join accountType in dbContext.AccTypes.AsNoTracking()
                on category.AccTypeID equals (int?)accountType.ACCTypeID into accountTypeJoin
            from accountType in accountTypeJoin.DefaultIfEmpty()
            join type in dbContext.Types.AsNoTracking()
                on accountType.TypeID equals (int?)type.TypeID into typeJoin
            from type in typeJoin.DefaultIfEmpty()
            join level1 in dbContext.Level1s.AsNoTracking()
                on type.Level1ID equals (int?)level1.Level1ID into level1Join
            from level1 in level1Join.DefaultIfEmpty()
            join level0 in dbContext.Level0s.AsNoTracking()
                on level1.Level0ID equals (int?)level0.Level0ID into level0Join
            from level0 in level0Join.DefaultIfEmpty()
            select new
            {
                AccountId = account.ACCID,
                AccountName = account.Name ?? $"حساب {account.ACCID}",
                IsChartAccount = account.Chart == true,
                Level1Id = level1 == null ? (int?)null : level1.Level1ID,
                Level1Name = level1 == null ? null : level1.Level1Name,
                Level0Id = level0 == null ? (int?)null : level0.Level0ID,
                Level0Name = level0 == null ? null : level0.Level0Name
            }).ToListAsync();

        var accountTotals = await GetFilteredTransactions()
            .GroupBy(t => t.ACCID)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(x => x.DR ?? 0m),
                Credit = g.Sum(x => x.CR ?? 0m)
            })
            .ToListAsync();

        var totalsByAccount = accountTotals
            .Where(x => x.AccountId.HasValue)
            .ToDictionary(x => x.AccountId!.Value, x => (x.Debit, x.Credit));

        var accountBalances = accountHierarchy
            .Select(x =>
            {
                totalsByAccount.TryGetValue(x.AccountId, out var totals);
                return new
                {
                    x.AccountId,
                    x.AccountName,
                    x.IsChartAccount,
                    x.Level1Id,
                    x.Level1Name,
                    x.Level0Id,
                    x.Level0Name,
                    Debit = totals.Debit,
                    Credit = totals.Credit
                };
            })
            .ToList();

        var level0Balances = accountBalances
            .Where(x => x.Level0Id.HasValue)
            .GroupBy(x => new { Id = x.Level0Id!.Value, x.Level0Name })
            .Select(g => new Level0BalanceVm
            {
                Level0Id = g.Key.Id,
                Level0Name = g.Key.Level0Name ?? $"المستوى {g.Key.Id}",
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .OrderBy(x => x.Level0Id)
            .ToList();

        var level1Balances = accountBalances
            .Where(x => x.Level1Id.HasValue)
            .GroupBy(x => new { Id = x.Level1Id!.Value, x.Level1Name })
            .Select(g => new Level1BalanceVm
            {
                Level1Id = g.Key.Id,
                Level1Name = g.Key.Level1Name ?? $"المستوى {g.Key.Id}",
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .Where(x => x.Net != 0m)
            .OrderByDescending(x => Math.Abs(x.Net))
            .ThenBy(x => x.Level1Id)
            .ToList();

        var chartAccountBalances = accountBalances
            .Where(x => x.IsChartAccount)
            .Select(x => new AccountBalanceVm
            {
                AccountId = x.AccountId,
                AccountName = x.AccountName,
                Debit = x.Debit,
                Credit = x.Credit
            })
            .OrderByDescending(x => Math.Abs(x.Net))
            .ThenBy(x => x.AccountId)
            .ToList();

        var journalEntries = await BuildJournalQuery().Take(8).ToListAsync();
        var companyLogo = await dbContext.Companies
            .AsNoTracking()
            .OrderBy(x => x.CompID)
            .Select(x => x.Pic)
            .FirstOrDefaultAsync();

        return new DashboardDataVm
        {
            CompanyLogo = companyLogo,
            JournalEntriesCount = counts?.Journal ?? 0,
            ReceiptsCount = counts?.Receipts ?? 0,
            PaymentsCount = counts?.Payments ?? 0,
            SalesInvoicesCount = counts?.Sales ?? 0,
            PurchaseInvoicesCount = counts?.Purchases ?? 0,
            TotalDebit = accountTotals.Sum(x => x.Debit),
            TotalCredit = accountTotals.Sum(x => x.Credit),
            Level0Balances = level0Balances,
            Level1Balances = level1Balances,
            ChartAccountBalances = chartAccountBalances,
            RecentEntries = journalEntries
        };
    }

    public async Task<IReadOnlyList<JournalEntryVm>> GetJournalEntriesAsync(int take = 100)
    {
        await access.RequireAsync("GL");
        var query = BuildJournalQuery();

        if (take > 0)
        {
            query = query.Take(take);
        }

        return await query.ToListAsync();
    }

    public async Task<IReadOnlyList<JournalEntryVm>> GetJournalEntriesBySourceAsync(int sourceId, int take = 100)
    {
        await access.RequireAsync(SectionAccess.Source(sourceId));
        var query = GetFilteredGlEntries()
            .Where(g => g.SourceID == sourceId)
            .OrderByDescending(g => g.GLDate)
            .ThenByDescending(g => g.GLID)
            .Select(g => new JournalEntryVm
            {
                Info = g.Info ?? string.Empty,
                GLID = g.GLID,
                SerialNo = g.GLSN,
                SanadID = g.SanadID,
                Date = g.GLDate,
                SourceName = g.Source != null ? (g.Source.Source ?? g.Source.SourceCode ?? string.Empty) : string.Empty,
                CardName = g.Card != null ? (g.Card.CardName ?? g.Card.Title ?? string.Empty) : string.Empty,
                Description = g.GLDesc ?? string.Empty,
                Debit = g.Transactions.Sum(t => t.DR ?? 0m),
                Credit = g.Transactions.Sum(t => t.CR ?? 0m)
            });

        if (take > 0)
        {
            query = query.Take(take);
        }

        return await query.ToListAsync();
    }

    public Task<IReadOnlyList<DocumentVm>> GetReceiptsAsync(int take = 100)
        => GetDocumentsByKeywordsAsync(ReceiptKeywords, take);

    public Task<IReadOnlyList<DocumentVm>> GetPaymentsAsync(int take = 100)
        => GetDocumentsByKeywordsAsync(PaymentKeywords, take);

    public Task<IReadOnlyList<DocumentVm>> GetSalesInvoicesAsync(int take = 100)
        => GetDocumentsByKeywordsAsync(SalesKeywords, take);

    public Task<IReadOnlyList<DocumentVm>> GetPurchaseInvoicesAsync(int take = 100)
        => GetDocumentsByKeywordsAsync(PurchaseKeywords, take);

    public async Task<IReadOnlyList<ChartTreeNodeVm>> GetChartOfAccountsTreeAsync()
    {
        await access.RequireAsync("Data");
        var directions = await dbContext.Directions.AsNoTracking().ToListAsync();
        var level0s = await dbContext.Level0s.AsNoTracking().ToListAsync();
        var level1s = await dbContext.Level1s.AsNoTracking().ToListAsync();
        var types = await dbContext.Types.AsNoTracking().ToListAsync();
        var accTypes = await dbContext.AccTypes.AsNoTracking().ToListAsync();
        var categories = await dbContext.Categories.AsNoTracking().ToListAsync();
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync();

        var directionById = directions.ToDictionary(x => x.DirectionID, x => x.DirectionName);
        var level1ByLevel0 = level1s.ToLookup(x => x.Level0ID);
        var typesByLevel1 = types.ToLookup(x => x.Level1ID);
        var accTypesByType = accTypes.ToLookup(x => x.TypeID);
        var categByAccType = categories.ToLookup(x => x.AccTypeID);
        var accountsByCateg = accounts.ToLookup(x => x.CategID);

        var roots = new List<ChartTreeNodeVm>();

        foreach (var l0 in level0s.OrderBy(x => x.Level0Name))
        {
            var rootNode = new ChartTreeNodeVm
            {
                Key = $"L0-{l0.Level0ID}",
                DisplayNumber = l0.Level0ID.ToString(),
                Name = l0.Level0Name ?? $"Level0-{l0.Level0ID}",
                LevelTitle = "المستوى 1",
                SecondaryText = l0.DirectionID.HasValue && directionById.TryGetValue(l0.DirectionID.Value, out var dirName) ? dirName : null
            };

            foreach (var l1 in level1ByLevel0[l0.Level0ID].OrderBy(x => x.Level1Name))
            {
                var l1Node = new ChartTreeNodeVm
                {
                    Key = $"L1-{l1.Level1ID}",
                    DisplayNumber = l1.Level1ID.ToString(),
                    Name = l1.Level1Name ?? $"Level1-{l1.Level1ID}",
                    LevelTitle = "المستوى 2"
                };

                foreach (var type in typesByLevel1[l1.Level1ID].OrderBy(x => x.TypeName))
                {
                    var typeNode = new ChartTreeNodeVm
                    {
                        Key = $"T-{type.TypeID}",
                        DisplayNumber = type.TypeID.ToString(),
                        Name = type.TypeName ?? $"Type-{type.TypeID}",
                        LevelTitle = "المستوى 3"
                    };

                    foreach (var accType in accTypesByType[type.TypeID].OrderBy(x => x.ACCTypeName))
                    {
                        var accTypeNode = new ChartTreeNodeVm
                        {
                            Key = $"AT-{accType.ACCTypeID}",
                            DisplayNumber = accType.ACCTypeID.ToString(),
                            Name = accType.ACCTypeName ?? $"ACCType-{accType.ACCTypeID}",
                            LevelTitle = "المستوى 4"
                        };

                        foreach (var categ in categByAccType[accType.ACCTypeID].OrderBy(x => x.CategName))
                        {
                            var categNode = new ChartTreeNodeVm
                            {
                                Key = $"C-{categ.CategID}",
                                DisplayNumber = categ.CategID.ToString(),
                                Name = categ.CategName ?? $"CATEG-{categ.CategID}",
                                LevelTitle = "المستوى 5",
                                CategoryId = categ.CategID
                            };

                            foreach (var acc in accountsByCateg[categ.CategID].OrderBy(x => x.Name))
                            {
                                categNode.Children.Add(new ChartTreeNodeVm
                                {
                                    Key = $"A-{acc.ACCID}",
                                    DisplayNumber = acc.ACCID.ToString(),
                                    Name = acc.Name ?? $"ACC-{acc.ACCID}",
                                    LevelTitle = "المستوى 6",
                                    IsAccountLeaf = true,
                                    SecondaryText = acc.Active == false ? "غير نشط" : null,
                                    AccountId = acc.ACCID,
                                    CategoryId = acc.CategID,
                                    AccountsCount = 1
                                });
                            }

                            UpdateNodeCounts(categNode);
                            accTypeNode.Children.Add(categNode);
                        }

                        UpdateNodeCounts(accTypeNode);
                        typeNode.Children.Add(accTypeNode);
                    }

                    UpdateNodeCounts(typeNode);
                    l1Node.Children.Add(typeNode);
                }

                UpdateNodeCounts(l1Node);
                rootNode.Children.Add(l1Node);
            }

            UpdateNodeCounts(rootNode);
            roots.Add(rootNode);
        }

        return roots;
    }

    public async Task<IReadOnlyList<AccountCategoryVm>> GetAccountCategoriesAsync()
    {
        await access.RequireAsync("Data");
        return await dbContext.Categories
            .AsNoTracking()
            .OrderBy(c => c.CategName)
            .Select(c => new AccountCategoryVm
            {
                CategID = c.CategID,
                Name = c.CategName ?? $"CATEG-{c.CategID}"
            })
            .ToListAsync();
    }

    public async Task<ReportsDataVm> GetReportsDataAsync()
    {
        await access.RequireAsync("Reports");
        var totals = await GetFilteredTransactions()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Debit = g.Sum(x => x.DR ?? 0m),
                Credit = g.Sum(x => x.CR ?? 0m)
            })
            .FirstOrDefaultAsync();

        var topAccounts = await GetFilteredTransactions()
            .Where(t => t.ACCID.HasValue)
            .GroupBy(t => new { t.ACCID, Name = t.Account != null ? t.Account.Name : null })
            .Select(g => new AccountBalanceVm
            {
                AccountId = g.Key.ACCID!.Value,
                AccountName = g.Key.Name ?? $"ACC-{g.Key.ACCID}",
                Debit = g.Sum(x => x.DR ?? 0m),
                Credit = g.Sum(x => x.CR ?? 0m)
            })
            .OrderByDescending(x => Math.Abs(x.Net))
            .Take(8)
            .ToListAsync();

        return new ReportsDataVm
        {
            TotalDebit = totals?.Debit ?? 0m,
            TotalCredit = totals?.Credit ?? 0m,
            MonthlyFlow = await GetMonthlyFlowAsync(),
            TopAccounts = topAccounts
        };
    }

    public async Task<IReadOnlyList<GeneralJournalLineVm>> GetGeneralJournalAsync(
        int take = 0,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        await access.RequireAsync("Reports");
        var transactions = GetFilteredTransactions();

        if (fromDate.HasValue)
        {
            var startDate = fromDate.Value.Date;
            transactions = transactions.Where(t =>
                t.GL != null && t.GL.GLDate.HasValue && t.GL.GLDate.Value >= startDate);
        }

        if (toDate.HasValue)
        {
            var endDateExclusive = toDate.Value.Date.AddDays(1);
            transactions = transactions.Where(t =>
                t.GL != null && t.GL.GLDate.HasValue && t.GL.GLDate.Value < endDateExclusive);
        }

        var query = transactions
            .OrderByDescending(t => t.GL != null ? t.GL.GLDate : null)
            .ThenByDescending(t => t.GLID)
            .ThenByDescending(t => t.TransID)
            .Select(t => new GeneralJournalLineVm
            {
                TransId = t.TransID,
                GLID = t.GLID,
                GLDate = t.GL != null ? t.GL.GLDate : null,
                SerialNo = t.GL != null ? t.GL.GLSN : null,
                AccountId = t.ACCID,
                AccountName = t.Account != null ? (t.Account.Name ?? string.Empty) : string.Empty,
                Description = t.TransDesc ?? (t.GL != null ? t.GL.GLDesc ?? string.Empty : string.Empty),
                Debit = t.DR ?? 0m,
                Credit = t.CR ?? 0m
            });

        if (take > 0)
        {
            query = query.Take(take);
        }

        return await query.ToListAsync();
    }

    public async Task<IReadOnlyList<FinancialPeriodOptionVm>> GetFinancialPeriodsAsync()
    {
        await access.RequireAsync();
        return await dbContext.Periods
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .ThenByDescending(x => x.PeriodID)
            .Select(x => new FinancialPeriodOptionVm
            {
                PeriodId = x.PeriodID,
                PeriodName = x.PeriodName ?? x.PeriodID.ToString(),
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                IsClosed = x.PeriodClose == true
            })
            .ToListAsync();
    }

    public async Task<TrialBalanceReportVm> GetTrialBalanceAsync(
        int? periodId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        await access.RequireAsync("Reports");
        var effectivePeriodId = periodId ?? sessionService.SelectedPeriodId;
        var periodQuery = dbContext.Periods.AsNoTracking();
        var period = effectivePeriodId.HasValue
            ? await periodQuery.FirstOrDefaultAsync(x => x.PeriodID == effectivePeriodId.Value)
            : await periodQuery.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.PeriodID).FirstOrDefaultAsync();

        if (period is null)
        {
            throw new InvalidOperationException("لا توجد فترة مالية متاحة لعرض ميزان المراجعة.");
        }

        var periodStart = (period.StartDate ?? new DateTime(DateTime.Today.Year, 1, 1)).Date;
        var periodEnd = (period.EndDate ?? periodStart.AddYears(1).AddDays(-1)).Date;
        var normalizedFrom = fromDate?.Date ?? periodStart;
        var normalizedTo = toDate?.Date ?? periodEnd;

        if (normalizedFrom < periodStart) normalizedFrom = periodStart;
        if (normalizedTo > periodEnd) normalizedTo = periodEnd;
        if (normalizedFrom > normalizedTo)
        {
            throw new InvalidOperationException("تاريخ البداية يجب أن يكون سابقاً لتاريخ النهاية وداخل الفترة المالية المختارة.");
        }

        var exclusiveTo = normalizedTo.AddDays(1);
        var aggregated = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.ACCID.HasValue
                        && t.GL != null
                        && t.GL.PeriodID == period.PeriodID
                        && (t.GL.SourceID == 4
                            || (t.GL.GLDate.HasValue && t.GL.GLDate.Value < exclusiveTo)))
            .GroupBy(t => new
            {
                AccountId = t.ACCID!.Value,
                AccountName = t.Account != null ? t.Account.Name : null,
                CategoryId = t.Account != null ? t.Account.CategID : null
            })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.AccountName,
                g.Key.CategoryId,
                OpeningBalance = g.Sum(x =>
                    x.GL!.SourceID == 4
                    || ((!x.GL.SourceID.HasValue || x.GL.SourceID != 4)
                        && x.GL.GLDate.HasValue
                        && x.GL.GLDate.Value < normalizedFrom)
                        ? (x.DR ?? 0m) - (x.CR ?? 0m)
                        : 0m),
                PeriodDebit = g.Sum(x =>
                    (!x.GL!.SourceID.HasValue || x.GL.SourceID != 4)
                    && x.GL.GLDate.HasValue
                    && x.GL.GLDate.Value >= normalizedFrom
                    && x.GL.GLDate.Value < exclusiveTo
                        ? x.DR ?? 0m
                        : 0m),
                PeriodCredit = g.Sum(x =>
                    (!x.GL!.SourceID.HasValue || x.GL.SourceID != 4)
                    && x.GL.GLDate.HasValue
                    && x.GL.GLDate.Value >= normalizedFrom
                    && x.GL.GLDate.Value < exclusiveTo
                        ? x.CR ?? 0m
                        : 0m)
            })
            .ToListAsync();

        var categories = await dbContext.Categories.AsNoTracking().ToDictionaryAsync(x => x.CategID);
        var accountTypes = await dbContext.AccTypes.AsNoTracking().ToDictionaryAsync(x => x.ACCTypeID);
        var types = await dbContext.Types.AsNoTracking().ToDictionaryAsync(x => x.TypeID);
        var level1s = await dbContext.Level1s.AsNoTracking().ToDictionaryAsync(x => x.Level1ID);
        var level0s = await dbContext.Level0s.AsNoTracking().ToDictionaryAsync(x => x.Level0ID);

        var rows = aggregated.Select(item =>
        {
            categories.TryGetValue(item.CategoryId ?? 0, out var category);
            accountTypes.TryGetValue(category?.AccTypeID ?? 0, out var accountType);
            types.TryGetValue(accountType?.TypeID ?? 0, out var type);
            level1s.TryGetValue(type?.Level1ID ?? 0, out var level1);
            level0s.TryGetValue(level1?.Level0ID ?? 0, out var level0);

            return new TrialBalanceRowVm
            {
                AccountId = item.AccountId,
                AccountName = item.AccountName ?? $"ACC-{item.AccountId}",
                Level1 = level0?.Level0Name ?? string.Empty,
                Level2 = level1?.Level1Name ?? string.Empty,
                Level3 = type?.TypeName ?? string.Empty,
                Level4 = accountType?.ACCTypeName ?? string.Empty,
                Level5 = category?.CategName ?? string.Empty,
                OpeningBalance = item.OpeningBalance,
                PeriodDebit = item.PeriodDebit,
                PeriodCredit = item.PeriodCredit
            };
        })
        .Where(x => x.OpeningBalance != 0m || x.PeriodDebit != 0m || x.PeriodCredit != 0m)
        .OrderBy(x => x.AccountId)
        .ToList();

        return new TrialBalanceReportVm
        {
            PeriodId = period.PeriodID,
            PeriodName = period.PeriodName ?? period.PeriodID.ToString(),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            FromDate = normalizedFrom,
            ToDate = normalizedTo,
            Rows = rows
        };
    }

    private async Task<List<MainCategoryBalanceVm>> GetMainCategoryBalancesAsync()
    {
        var level0s = await dbContext.Level0s
            .AsNoTracking()
            .Select(x => new { x.Level0ID, x.Level0Name })
            .ToListAsync();

        var trialRows = await GetFilteredTransactions()
            .Where(t => t.ACCID.HasValue)
            .GroupBy(t => new
            {
                AccountId = t.ACCID!.Value,
                AccountName = t.Account != null ? t.Account.Name : null,
                Level0Id = t.Account != null ? t.Account.Group1 : null
            })
            .Select(g => new
            {
                g.Key.Level0Id,
                Debit = g.Sum(x => x.DR ?? 0m),
                Credit = g.Sum(x => x.CR ?? 0m)
            })
            .ToListAsync();

        var level0NameById = level0s.ToDictionary(x => x.Level0ID, x => x.Level0Name ?? $"المستوى {x.Level0ID}");

        var mapped = trialRows
            .Where(x => x.Level0Id.HasValue)
            .Select(x =>
            {
                var level0Id = x.Level0Id!.Value;
                var level0Name = level0NameById.TryGetValue(level0Id, out var name) ? name : $"المستوى {level0Id}";
                var category = ResolveMainCategory(level0Name);

                return new
                {
                    category.Key,
                    category.Name,
                    Debit = x.Debit,
                    Credit = x.Credit
                };
            })
            .GroupBy(x => new { x.Key, x.Name })
            .Select(g => new MainCategoryBalanceVm
            {
                CategoryKey = g.Key.Key,
                CategoryName = g.Key.Name,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .ToList();

        foreach (var item in mapped)
        {
            item.Net = item.CategoryKey switch
            {
                "assets" or "expenses" => item.Debit - item.Credit,
                _ => item.Credit - item.Debit
            };
        }

        var ordered = new[]
        {
            (Key: "assets", Name: "الأصول"),
            (Key: "liabilities", Name: "الخصوم"),
            (Key: "equity", Name: "حقوق الملكية"),
            (Key: "revenues", Name: "الإيرادات"),
            (Key: "expenses", Name: "المصروفات")
        };

        var byKey = mapped.ToDictionary(x => x.CategoryKey, x => x, StringComparer.OrdinalIgnoreCase);
        var result = new List<MainCategoryBalanceVm>();

        foreach (var category in ordered)
        {
            if (byKey.TryGetValue(category.Key, out var existing))
            {
                existing.CategoryName = category.Name;
                result.Add(existing);
            }
            else
            {
                result.Add(new MainCategoryBalanceVm
                {
                    CategoryKey = category.Key,
                    CategoryName = category.Name,
                    Debit = 0m,
                    Credit = 0m,
                    Net = 0m
                });
            }
        }

        return result;
    }

    private static (string Key, string Name) ResolveMainCategory(string level0Name)
    {
        if (string.IsNullOrWhiteSpace(level0Name))
        {
            return ("other", "أخرى");
        }

        var name = level0Name.Trim();

        if (name.Contains("أصول", StringComparison.OrdinalIgnoreCase)
            || name.Contains("موجود", StringComparison.OrdinalIgnoreCase)
            || name.Contains("asset", StringComparison.OrdinalIgnoreCase))
            return ("assets", "الأصول");
        if (name.Contains("خصوم", StringComparison.OrdinalIgnoreCase)
            || name.Contains("التزام", StringComparison.OrdinalIgnoreCase)
            || name.Contains("liab", StringComparison.OrdinalIgnoreCase))
            return ("liabilities", "الخصوم");
        if (name.Contains("ملكية", StringComparison.OrdinalIgnoreCase)
            || name.Contains("رأس", StringComparison.OrdinalIgnoreCase)
            || name.Contains("راس", StringComparison.OrdinalIgnoreCase)
            || name.Contains("equity", StringComparison.OrdinalIgnoreCase))
            return ("equity", "حقوق الملكية");
        if (name.Contains("إيراد", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ايراد", StringComparison.OrdinalIgnoreCase)
            || name.Contains("دخل", StringComparison.OrdinalIgnoreCase)
            || name.Contains("مبيعات", StringComparison.OrdinalIgnoreCase)
            || name.Contains("revenue", StringComparison.OrdinalIgnoreCase))
            return ("revenues", "الإيرادات");
        if (name.Contains("مصروف", StringComparison.OrdinalIgnoreCase)
            || name.Contains("مصاريف", StringComparison.OrdinalIgnoreCase)
            || name.Contains("تكاليف", StringComparison.OrdinalIgnoreCase)
            || name.Contains("expense", StringComparison.OrdinalIgnoreCase))
            return ("expenses", "المصروفات");

        return ("other", "أخرى");
    }

    private async Task<List<TypeBalanceVm>> GetTypeBalancesAsync()
    {
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Select(x => new { x.CategID, x.AccTypeID })
            .ToListAsync();

        var accTypes = await dbContext.AccTypes
            .AsNoTracking()
            .Select(x => new { x.ACCTypeID, x.TypeID })
            .ToListAsync();

        var types = await dbContext.Types
            .AsNoTracking()
            .Select(x => new { x.TypeID, x.TypeName })
            .ToListAsync();

        var categToType = categories
            .Join(accTypes,
                c => c.AccTypeID,
                a => a.ACCTypeID,
                (c, a) => new { c.CategID, a.TypeID })
            .ToDictionary(x => x.CategID, x => x.TypeID);

        var typeNameById = types.ToDictionary(x => x.TypeID, x => x.TypeName ?? $"Type-{x.TypeID}");

        var accountTypeRows = await GetFilteredTransactions()
            .Where(t => t.ACCID.HasValue && t.Account != null && t.Account.CategID.HasValue)
            .Select(t => new
            {
                CategId = t.Account!.CategID!.Value,
                Debit = t.DR ?? 0m,
                Credit = t.CR ?? 0m
            })
            .ToListAsync();

        var grouped = accountTypeRows
            .Where(x => categToType.ContainsKey(x.CategId))
            .GroupBy(x => categToType[x.CategId]!.Value)
            .Select(g => new TypeBalanceVm
            {
                TypeId = g.Key,
                TypeName = typeNameById.TryGetValue(g.Key, out var typeName) ? typeName : $"Type-{g.Key}",
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .OrderBy(x => x.TypeName)
            .ToList();

        return grouped;
    }

    public async Task<SettingsDataVm> GetSettingsDataAsync()
    {
        await access.RequireAsync();
        var company = await dbContext.Companies.AsNoTracking().OrderBy(x => x.CompID).FirstOrDefaultAsync();
        var vat = await dbContext.VatRates.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();

        PeriodEntity? period;
        if (sessionService.SelectedPeriodId.HasValue)
        {
            period = await dbContext.Periods
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PeriodID == sessionService.SelectedPeriodId.Value);
        }
        else
        {
            period = await dbContext.Periods
                .AsNoTracking()
                .OrderByDescending(x => x.PeriodID)
                .FirstOrDefaultAsync();
        }

        return new SettingsDataVm
        {
            CompanyLogo = company?.Pic,
            CompanyName = sessionService.SelectedCompanyName ?? company?.Comp ?? string.Empty,
            CompanyPhone = company?.CompTel ?? string.Empty,
            CompanyAddress = company?.CompAddress ?? string.Empty,
            CommercialRecord = company?.CR ?? string.Empty,
            TaxNumber = company?.VAT ?? string.Empty,
            TaxRate = vat?.TaxRate ?? 0m,
            ActivePeriodName = period?.PeriodName ?? string.Empty,
            ActivePeriodStart = period?.StartDate,
            ActivePeriodEnd = period?.EndDate,
            IsPeriodClosed = period?.PeriodClose ?? false
        };
    }

    public async Task<IReadOnlyList<CostCenterOptionVm>> GetCostCenterOptionsAsync(int level)
    {
        await access.RequireAsync("Reports");
        var query = level switch
        {
            1 => dbContext.Set<Center1Entity>().AsNoTracking().Select(c => new CostCenterOptionVm { Id = c.CenterID, Name = c.CenterName ?? "" }),
            2 => dbContext.Set<Center2Entity>().AsNoTracking().Select(c => new CostCenterOptionVm { Id = c.CenterID, Name = c.CenterName ?? "" }),
            3 => dbContext.Set<Center3Entity>().AsNoTracking().Select(c => new CostCenterOptionVm { Id = c.CenterID, Name = c.CenterName ?? "" }),
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
        return await query.OrderBy(c => c.Id).ToListAsync();
    }

    public async Task<IReadOnlyList<CostCenterLineVm>> GetCostCenterReportAsync(int level, int? centerId, DateTime? fromDate, DateTime? toDate)
    {
        await access.RequireAsync("Reports");
        if (level is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(level));
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
            throw new ArgumentException("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية.");
        var query = GetFilteredTransactions().Where(t => t.GL != null);
        query = level switch
        {
            1 => query.Where(t => t.GL!.Center1.HasValue && (!centerId.HasValue || t.GL.Center1 == centerId)),
            2 => query.Where(t => t.GL!.Center2.HasValue && (!centerId.HasValue || t.GL.Center2 == centerId)),
            _ => query.Where(t => t.Center3.HasValue && (!centerId.HasValue || t.Center3 == centerId))
        };
        if (fromDate.HasValue) { var start = fromDate.Value.Date; query = query.Where(t => t.GL!.GLDate >= start); }
        if (toDate.HasValue) { var end = toDate.Value.Date.AddDays(1); query = query.Where(t => t.GL!.GLDate < end); }
        return await query.OrderByDescending(t => t.GL!.GLDate).ThenByDescending(t => t.GLID).ThenBy(t => t.TransID)
            .Select(t => new CostCenterLineVm
            {
                TransId = t.TransID, GLID = t.GLID, GLDate = t.GL!.GLDate, SerialNo = t.GL.GLSN,
                CenterId = level == 1 ? t.GL.Center1 : level == 2 ? t.GL.Center2 : t.Center3,
                CenterName = level == 1 ? (t.GL.Center1Lookup != null ? t.GL.Center1Lookup.CenterName ?? "" : "")
                    : level == 2 ? (t.GL.Center2Lookup != null ? t.GL.Center2Lookup.CenterName ?? "" : "")
                    : (t.Center3Lookup != null ? t.Center3Lookup.CenterName ?? "" : ""),
                AccountId = t.ACCID, AccountName = t.Account != null ? t.Account.Name ?? "" : "",
                Description = t.TransDesc ?? t.GL.GLDesc ?? "", Info = t.GL.Info ?? "",
                SourceName = t.GL.Source != null ? t.GL.Source.Source ?? "" : "",
                Debit = t.DR ?? 0m, Credit = t.CR ?? 0m
            }).ToListAsync();
    }

    private IQueryable<GlEntity> GetFilteredGlEntries()
    {
        var query = dbContext.GLEntries.AsNoTracking();

        if (sessionService.SelectedPeriodId.HasValue)
        {
            query = query.Where(g => g.PeriodID == sessionService.SelectedPeriodId.Value);
        }

        return query;
    }

    private IQueryable<TransEntity> GetFilteredTransactions()
    {
        var query = dbContext.Transactions.AsNoTracking();

        if (sessionService.SelectedPeriodId.HasValue)
        {
            query = query.Where(t => t.GL != null && t.GL.PeriodID == sessionService.SelectedPeriodId.Value);
        }

        return query;
    }

    private IQueryable<GlEntity> AccessibleEntries(IQueryable<GlEntity> query)
    {
        var gl = access.Has("GL"); var rc = access.Has("RC"); var pm = access.Has("PM"); var inv = access.Has("INV");
        return query.Where(g => (g.SourceID == 2 && rc) || (g.SourceID == 3 && pm) || ((g.SourceID == 5 || g.SourceID == 6) && inv) || ((g.SourceID == null || (g.SourceID != 2 && g.SourceID != 3 && g.SourceID != 5 && g.SourceID != 6)) && gl));
    }

    private IQueryable<JournalEntryVm> BuildJournalQuery()
    {
        return AccessibleEntries(GetFilteredGlEntries())
            .OrderByDescending(g => g.GLDate)
            .ThenByDescending(g => g.GLID)
            .Select(g => new JournalEntryVm
            {
                Info = g.Info ?? string.Empty,
                GLID = g.GLID,
                SerialNo = g.GLSN,
                SanadID = g.SanadID,
                Date = g.GLDate,
                SourceName = g.Source != null ? (g.Source.Source ?? g.Source.SourceCode ?? "") : "",
                Description = g.GLDesc ?? string.Empty,
                CardName = g.Card != null ? (g.Card.CardName ?? g.Card.Title ?? "") : string.Empty,
                Debit = g.Transactions.Sum(t => t.DR ?? 0m),
                Credit = g.Transactions.Sum(t => t.CR ?? 0m)
            });
    }

    private async Task<IReadOnlyList<DocumentVm>> GetDocumentsByKeywordsAsync(string[] keywords, int take)
    {
        await access.RequireAsync(ReferenceEquals(keywords, ReceiptKeywords) ? "RC" : ReferenceEquals(keywords, PaymentKeywords) ? "PM" : "INV");
        var sourceIds = await GetSourceIdsByKeywordsAsync(keywords);

        IQueryable<GlEntity> query = GetFilteredGlEntries()
            .Where(g => g.SourceID.HasValue && sourceIds.Contains(g.SourceID.Value))
            .OrderByDescending(g => g.GLDate)
            .ThenByDescending(g => g.GLID);

        if (take > 0)
        {
            query = query.Take(take);
        }

        return await query
            .Select(g => new DocumentVm
            {
                GLID = g.GLID,
                SerialNo = g.GLSN,
                Date = g.GLDate,
                SourceName = g.Source != null ? (g.Source.Source ?? g.Source.SourceCode ?? "") : "",
                Description = g.GLDesc ?? string.Empty,
                Party = g.Card != null ? (g.Card.CardName ?? g.Card.Title ?? "") : "",
                Method = g.Method != null ? (g.Method.MethodName ?? "") : "",
                Debit = g.Transactions.Sum(t => t.DR ?? 0m),
                Credit = g.Transactions.Sum(t => t.CR ?? 0m),
                Amount = g.Transactions.Sum(t => t.TotalAmount ?? t.Amount ?? 0m)
            })
            .ToListAsync();
    }

    private async Task<List<int>> GetSourceIdsByKeywordsAsync(string[] keywords)
    {
        var sources = await dbContext.Sources.AsNoTracking().ToListAsync();

        var ids = sources
            .Where(s => keywords.Any(k =>
                (!string.IsNullOrWhiteSpace(s.Source) && s.Source.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(s.SourceCode) && s.SourceCode.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .Select(s => s.SourceID)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            ids = sources.Select(x => x.SourceID).Take(1).ToList();
        }

        return ids;
    }

    private async Task<List<MonthlyPointVm>> GetMonthlyFlowAsync()
    {
        var since = DateTime.Today.AddMonths(-5);

        var raw = await GetFilteredGlEntries()
            .Where(g => g.GLDate.HasValue && g.GLDate.Value >= since)
            .SelectMany(g => g.Transactions.Select(t => new
            {
                Year = g.GLDate!.Value.Year,
                Month = g.GLDate.Value.Month,
                Debit = t.DR ?? 0m,
                Credit = t.CR ?? 0m
            }))
            .GroupBy(x => new { x.Year, x.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ToListAsync();

        return raw
            .Select(x => new MonthlyPointVm
            {
                Month = $"{x.Year}/{x.Month:D2}",
                Debit = x.Debit,
                Credit = x.Credit
            })
            .ToList();
    }

    private async Task<List<Level0BalanceVm>> GetLevel0BalancesAsync()
    {
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Select(x => new { x.CategID, x.AccTypeID })
            .ToListAsync();

        var accTypes = await dbContext.AccTypes
            .AsNoTracking()
            .Select(x => new { x.ACCTypeID, x.TypeID })
            .ToListAsync();

        var types = await dbContext.Types
            .AsNoTracking()
            .Select(x => new { x.TypeID, x.Level1ID })
            .ToListAsync();

        var level1s = await dbContext.Level1s
            .AsNoTracking()
            .Select(x => new { x.Level1ID, x.Level0ID })
            .ToListAsync();

        var level0s = await dbContext.Level0s
            .AsNoTracking()
            .Select(x => new { x.Level0ID, x.Level0Name })
            .ToListAsync();

        var categToAccType = categories.ToDictionary(x => x.CategID, x => x.AccTypeID);
        var accTypeToType = accTypes.ToDictionary(x => x.ACCTypeID, x => x.TypeID);
        var typeToLevel1 = types.ToDictionary(x => x.TypeID, x => x.Level1ID);
        var level1ToLevel0 = level1s.ToDictionary(x => x.Level1ID, x => x.Level0ID);
        var level0NameById = level0s.ToDictionary(x => x.Level0ID, x => x.Level0Name ?? $"المستوى {x.Level0ID}");

        var transRows = await GetFilteredTransactions()
            .Where(t => t.Account != null && t.Account.CategID.HasValue)
            .Select(t => new
            {
                CategId = t.Account!.CategID!.Value,
                Debit = t.DR ?? 0m,
                Credit = t.CR ?? 0m
            })
            .ToListAsync();

        var grouped = transRows
            .Select(x =>
            {
                if (!categToAccType.TryGetValue(x.CategId, out var accTypeId) || !accTypeId.HasValue)
                {
                    return null;
                }

                if (!accTypeToType.TryGetValue(accTypeId.Value, out var typeId) || !typeId.HasValue)
                {
                    return null;
                }

                if (!typeToLevel1.TryGetValue(typeId.Value, out var level1Id) || !level1Id.HasValue)
                {
                    return null;
                }

                if (!level1ToLevel0.TryGetValue(level1Id.Value, out var level0Id) || !level0Id.HasValue)
                {
                    return null;
                }

                return new
                {
                    Level0Id = level0Id.Value,
                    x.Debit,
                    x.Credit
                };
            })
            .Where(x => x is not null)
            .GroupBy(x => x!.Level0Id)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Debit = g.Sum(x => x!.Debit),
                    Credit = g.Sum(x => x!.Credit)
                });

        return level0s
            .Select(x =>
            {
                grouped.TryGetValue(x.Level0ID, out var totals);
                return new Level0BalanceVm
                {
                    Level0Id = x.Level0ID,
                    Level0Name = level0NameById.TryGetValue(x.Level0ID, out var name) ? name : $"المستوى {x.Level0ID}",
                    Debit = totals?.Debit ?? 0m,
                    Credit = totals?.Credit ?? 0m
                };
            })
            .OrderBy(x => x.Level0Id)
            .ToList();
    }

    public async Task<IReadOnlyList<AccountStatementAccountVm>> GetAccountStatementAccountsAsync()
    {
        await access.RequireAsync("Reports");
        return await dbContext.Accounts
            .AsNoTracking()
            .OrderBy(x => x.ACCID)
            .Select(x => new AccountStatementAccountVm
            {
                AccountId = x.ACCID,
                AccountName = x.Name ?? $"ACC-{x.ACCID}"
            })
            .ToListAsync();
    }

    public async Task<AccountStatementVm?> GetAccountStatementAsync(
        long accountId,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        await access.RequireAsync("Reports");
        var account = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.ACCID == accountId)
            .Select(x => new { x.ACCID, x.Name })
            .FirstOrDefaultAsync();

        if (account is null)
        {
            return null;
        }

        var normalizedFrom = fromDate?.Date;
        var normalizedTo = toDate?.Date;
        var accountTransactions = GetFilteredTransactions().Where(t => t.ACCID == accountId);

        var openingBalance = 0m;
        if (normalizedFrom.HasValue)
        {
            openingBalance = await accountTransactions
                .Where(t => t.GL != null
                            && t.GL.GLDate.HasValue
                            && t.GL.GLDate.Value < normalizedFrom.Value)
                .SumAsync(t => (t.DR ?? 0m) - (t.CR ?? 0m));
        }

        var statementQuery = accountTransactions;
        if (normalizedFrom.HasValue)
        {
            statementQuery = statementQuery.Where(t => t.GL != null
                                                         && t.GL.GLDate.HasValue
                                                         && t.GL.GLDate.Value >= normalizedFrom.Value);
        }

        if (normalizedTo.HasValue)
        {
            var exclusiveTo = normalizedTo.Value.AddDays(1);
            statementQuery = statementQuery.Where(t => t.GL != null
                                                         && t.GL.GLDate.HasValue
                                                         && t.GL.GLDate.Value < exclusiveTo);
        }

        var lines = await statementQuery
            .OrderBy(t => t.GL != null ? t.GL.GLDate : null)
            .ThenBy(t => t.GLID)
            .ThenBy(t => t.TransID)
            .Select(t => new AccountStatementLineVm
            {
                TransId = t.TransID,
                GLID = t.GLID,
                SerialNo = t.GL != null ? t.GL.GLSN : null,
                Date = t.GL != null ? t.GL.GLDate : null,
                SourceName = t.GL != null && t.GL.Source != null
                    ? (t.GL.Source.Source ?? t.GL.Source.SourceCode ?? string.Empty)
                    : string.Empty,
                Reference = t.GL != null ? t.GL.GLRef ?? string.Empty : string.Empty,
                Description = t.TransDesc ?? (t.GL != null ? t.GL.GLDesc ?? string.Empty : string.Empty),
                Debit = t.DR ?? 0m,
                Credit = t.CR ?? 0m
            })
            .ToListAsync();

        var runningBalance = openingBalance;
        foreach (var line in lines)
        {
            runningBalance += line.Debit - line.Credit;
            line.RunningBalance = runningBalance;
        }

        return new AccountStatementVm
        {
            AccountId = account.ACCID,
            AccountName = account.Name ?? $"ACC-{account.ACCID}",
            OpeningBalance = openingBalance,
            Lines = lines
        };
    }

    public async Task<FinancialStatementVm> GetFinancialStatementAsync(
        string directionName,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        await access.RequireAsync("Reports");
        var normalizedDirection = directionName.Trim();
        var directions = await dbContext.Directions.AsNoTracking().ToListAsync();
        var direction = directions.FirstOrDefault(x =>
            string.Equals(x.DirectionName?.Trim(), normalizedDirection, StringComparison.OrdinalIgnoreCase));

        var result = new FinancialStatementVm
        {
            DirectionName = direction?.DirectionName ?? normalizedDirection,
            FromDate = fromDate?.Date,
            ToDate = toDate?.Date
        };

        if (direction is null)
        {
            return result;
        }

        var level0s = await dbContext.Level0s
            .AsNoTracking()
            .Where(x => x.DirectionID == direction.DirectionID)
            .OrderBy(x => x.Level0ID)
            .ToListAsync();

        var level0Ids = level0s.Select(x => x.Level0ID).ToList();
        var level1s = await dbContext.Level1s
            .AsNoTracking()
            .Where(x => x.Level0ID.HasValue && level0Ids.Contains(x.Level0ID.Value))
            .OrderBy(x => x.Level1ID)
            .ToListAsync();

        var level1Ids = level1s.Select(x => x.Level1ID).ToList();
        var types = await dbContext.Types
            .AsNoTracking()
            .Where(x => x.Level1ID.HasValue && level1Ids.Contains(x.Level1ID.Value))
            .OrderBy(x => x.TypeID)
            .ToListAsync();

        var typeIds = types.Select(x => x.TypeID).ToList();
        var accTypes = await dbContext.AccTypes
            .AsNoTracking()
            .Where(x => x.TypeID.HasValue && typeIds.Contains(x.TypeID.Value))
            .OrderBy(x => x.ACCTypeID)
            .ToListAsync();

        var accTypeIds = accTypes.Select(x => x.ACCTypeID).ToList();
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Where(x => x.AccTypeID.HasValue && accTypeIds.Contains(x.AccTypeID.Value))
            .OrderBy(x => x.CategID)
            .ToListAsync();

        var categoryIds = categories.Select(x => x.CategID).ToList();
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.CategID.HasValue && categoryIds.Contains(x.CategID.Value))
            .OrderBy(x => x.ACCID)
            .Select(x => new { x.ACCID, x.Name, x.CategID })
            .ToListAsync();

        var transactionQuery = GetFilteredTransactions()
            .Where(t => t.ACCID.HasValue
                        && t.Account != null
                        && t.Account.CategID.HasValue
                        && categoryIds.Contains(t.Account.CategID.Value));

        var normalizedFrom = fromDate?.Date;
        var normalizedTo = toDate?.Date;
        if (normalizedFrom.HasValue)
        {
            transactionQuery = transactionQuery.Where(t => t.GL != null
                                                             && t.GL.GLDate.HasValue
                                                             && t.GL.GLDate.Value >= normalizedFrom.Value);
        }

        if (normalizedTo.HasValue)
        {
            var exclusiveTo = normalizedTo.Value.AddDays(1);
            transactionQuery = transactionQuery.Where(t => t.GL != null
                                                             && t.GL.GLDate.HasValue
                                                             && t.GL.GLDate.Value < exclusiveTo);
        }

        var accountTotals = await transactionQuery
            .GroupBy(t => t.ACCID!.Value)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(x => x.DR ?? 0m),
                Credit = g.Sum(x => x.CR ?? 0m)
            })
            .ToDictionaryAsync(x => x.AccountId);

        var level1Lookup = level1s.ToLookup(x => x.Level0ID);
        var typeLookup = types.ToLookup(x => x.Level1ID);
        var accTypeLookup = accTypes.ToLookup(x => x.TypeID);
        var categoryLookup = categories.ToLookup(x => x.AccTypeID);
        var accountLookup = accounts.ToLookup(x => x.CategID);

        foreach (var level0 in level0s)
        {
            var level0Node = new FinancialStatementNodeVm
            {
                Key = $"L0-{level0.Level0ID}",
                Code = level0.Level0ID.ToString(),
                Name = level0.Level0Name ?? $"Level0-{level0.Level0ID}",
                LevelName = "المستوى الأول",
                Level = 1
            };

            foreach (var level1 in level1Lookup[level0.Level0ID])
            {
                var level1Node = new FinancialStatementNodeVm
                {
                    Key = $"L1-{level1.Level1ID}",
                    Code = level1.Level1ID.ToString(),
                    Name = level1.Level1Name ?? $"Level1-{level1.Level1ID}",
                    LevelName = "المستوى الثاني",
                    Level = 2
                };

                foreach (var type in typeLookup[level1.Level1ID])
                {
                    var typeNode = new FinancialStatementNodeVm
                    {
                        Key = $"T-{type.TypeID}",
                        Code = type.TypeID.ToString(),
                        Name = type.TypeName ?? $"Type-{type.TypeID}",
                        LevelName = "المستوى الثالث",
                        Level = 3
                    };

                    foreach (var accType in accTypeLookup[type.TypeID])
                    {
                        var accTypeNode = new FinancialStatementNodeVm
                        {
                            Key = $"AT-{accType.ACCTypeID}",
                            Code = accType.ACCTypeID.ToString(),
                            Name = accType.ACCTypeName ?? $"ACCType-{accType.ACCTypeID}",
                            LevelName = "المستوى الرابع",
                            Level = 4
                        };

                        foreach (var category in categoryLookup[accType.ACCTypeID])
                        {
                            var categoryNode = new FinancialStatementNodeVm
                            {
                                Key = $"C-{category.CategID}",
                                Code = category.CategID.ToString(),
                                Name = category.CategName ?? $"Categ-{category.CategID}",
                                LevelName = "المستوى الخامس",
                                Level = 5
                            };

                            foreach (var account in accountLookup[category.CategID])
                            {
                                accountTotals.TryGetValue(account.ACCID, out var totals);
                                var rawBalance = (totals?.Debit ?? 0m) - (totals?.Credit ?? 0m);
                                var netBalance = NormalizeFinancialBalance(level0Node.Name, rawBalance);

                                if (netBalance == 0m)
                                {
                                    continue;
                                }

                                categoryNode.Children.Add(new FinancialStatementNodeVm
                                {
                                    Key = $"A-{account.ACCID}",
                                    Code = account.ACCID.ToString(),
                                    Name = account.Name ?? $"ACC-{account.ACCID}",
                                    LevelName = "الحساب",
                                    Level = 6,
                                    IsAccount = true,
                                    AccountId = account.ACCID,
                                    NetBalance = netBalance
                                });
                                result.AccountsCount++;
                            }

                            AddFinancialNodeIfNotEmpty(accTypeNode, categoryNode);
                        }

                        AddFinancialNodeIfNotEmpty(typeNode, accTypeNode);
                    }

                    AddFinancialNodeIfNotEmpty(level1Node, typeNode);
                }

                AddFinancialNodeIfNotEmpty(level0Node, level1Node);
            }

            if (level0Node.Children.Count > 0)
            {
                level0Node.NetBalance = level0Node.Children.Sum(x => x.NetBalance);
                result.Nodes.Add(level0Node);

                if (string.Equals(normalizedDirection, "Income", StringComparison.OrdinalIgnoreCase))
                {
                    result.NetResult += ResolveMainCategory(level0Node.Name).Key switch
                    {
                        "revenues" => level0Node.NetBalance,
                        "expenses" => -level0Node.NetBalance,
                        _ => level0Node.NetBalance
                    };
                }
                else if (string.Equals(normalizedDirection, "Balance", StringComparison.OrdinalIgnoreCase))
                {
                    result.BalanceDifference += ResolveMainCategory(level0Node.Name).Key switch
                    {
                        "assets" => level0Node.NetBalance,
                        "liabilities" or "equity" => -level0Node.NetBalance,
                        _ => 0m
                    };
                }
            }
        }

        return result;
    }

    private static void AddFinancialNodeIfNotEmpty(
        FinancialStatementNodeVm parent,
        FinancialStatementNodeVm child)
    {
        if (child.Children.Count == 0)
        {
            return;
        }

        child.NetBalance = child.Children.Sum(x => x.NetBalance);
        parent.Children.Add(child);
    }

    private static decimal NormalizeFinancialBalance(string level0Name, decimal rawBalance)
    {
        var category = ResolveMainCategory(level0Name).Key;
        return category is "liabilities" or "equity" or "revenues"
            ? -rawBalance
            : rawBalance;
    }

    private async Task<List<Level1BalanceVm>> GetLevel1BalancesAsync()
    {
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Select(x => new { x.CategID, x.AccTypeID })
            .ToListAsync();

        var accTypes = await dbContext.AccTypes
            .AsNoTracking()
            .Select(x => new { x.ACCTypeID, x.TypeID })
            .ToListAsync();

        var types = await dbContext.Types
            .AsNoTracking()
            .Select(x => new { x.TypeID, x.Level1ID })
            .ToListAsync();

        var level1s = await dbContext.Level1s
            .AsNoTracking()
            .Select(x => new { x.Level1ID, x.Level1Name })
            .ToListAsync();

        var categToAccType = categories.ToDictionary(x => x.CategID, x => x.AccTypeID);
        var accTypeToType = accTypes.ToDictionary(x => x.ACCTypeID, x => x.TypeID);
        var typeToLevel1 = types.ToDictionary(x => x.TypeID, x => x.Level1ID);

        var transactionRows = await GetFilteredTransactions()
            .Where(t => t.Account != null && t.Account.CategID.HasValue)
            .Select(t => new
            {
                CategId = t.Account!.CategID!.Value,
                Debit = t.DR ?? 0m,
                Credit = t.CR ?? 0m
            })
            .ToListAsync();

        var grouped = transactionRows
            .Select(x =>
            {
                if (!categToAccType.TryGetValue(x.CategId, out var accTypeId) || !accTypeId.HasValue
                    || !accTypeToType.TryGetValue(accTypeId.Value, out var typeId) || !typeId.HasValue
                    || !typeToLevel1.TryGetValue(typeId.Value, out var level1Id) || !level1Id.HasValue)
                {
                    return null;
                }

                return new { Level1Id = level1Id.Value, x.Debit, x.Credit };
            })
            .Where(x => x is not null)
            .GroupBy(x => x!.Level1Id)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Debit = g.Sum(x => x!.Debit),
                    Credit = g.Sum(x => x!.Credit)
                });

        return level1s
            .Select(x =>
            {
                grouped.TryGetValue(x.Level1ID, out var totals);
                return new Level1BalanceVm
                {
                    Level1Id = x.Level1ID,
                    Level1Name = x.Level1Name ?? $"المستوى {x.Level1ID}",
                    Debit = totals?.Debit ?? 0m,
                    Credit = totals?.Credit ?? 0m
                };
            })
            .OrderByDescending(x => Math.Abs(x.Net))
            .ThenBy(x => x.Level1Id)
            .ToList();
    }

    private async Task<List<AccountBalanceVm>> GetChartAccountBalancesAsync()
    {
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Chart == true)
            .Select(x => new { x.ACCID, x.Name })
            .OrderBy(x => x.ACCID)
            .ToListAsync();

        var totals = await GetFilteredTransactions()
            .Where(t => t.ACCID.HasValue && t.Account != null && t.Account.Chart == true)
            .GroupBy(t => t.ACCID!.Value)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(x => x.DR ?? 0m),
                Credit = g.Sum(x => x.CR ?? 0m)
            })
            .ToDictionaryAsync(x => x.AccountId);

        return accounts
            .Select(x =>
            {
                totals.TryGetValue(x.ACCID, out var total);
                return new AccountBalanceVm
                {
                    AccountId = x.ACCID,
                    AccountName = x.Name ?? $"حساب {x.ACCID}",
                    Debit = total?.Debit ?? 0m,
                    Credit = total?.Credit ?? 0m
                };
            })
            .OrderByDescending(x => Math.Abs(x.Net))
            .ThenBy(x => x.AccountId)
            .ToList();
    }

    private static int UpdateNodeCounts(ChartTreeNodeVm node)
    {
        if (node.IsAccountLeaf)
        {
            node.AccountsCount = 1;
            return 1;
        }

        var total = 0;
        foreach (var child in node.Children)
        {
            total += UpdateNodeCounts(child);
        }

        node.AccountsCount = total;
        return total;
    }

    private static bool TryParseNodeKey(string nodeKey, out string prefix, out int numericId)
    {
        prefix = string.Empty;
        numericId = 0;

        var parts = nodeKey.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out numericId))
        {
            return false;
        }

        prefix = parts[0].ToUpperInvariant();
        return true;
    }

    public async Task<JournalEntryLookupsVm> GetJournalEntryLookupsAsync()
    {
        await access.RequireAsync("GL", "RC", "PM", "INV");
        var vatRates = await dbContext.VatRates
            .AsNoTracking()
            .Where(x => x.TaxRate.HasValue)
            .OrderBy(x => x.TaxRate)
            .ToListAsync();

        var taxAccountIds = vatRates
            .SelectMany(vat => new long?[] { vat.SalesTaxACC, vat.PurchTaxACC })
            .Where(accountId => accountId.HasValue)
            .Select(accountId => accountId!.Value)
            .Distinct()
            .ToList();

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Active == true || taxAccountIds.Contains(x.ACCID))
            .OrderBy(x => x.Name)
            .ToListAsync();

        var sources = await dbContext.Sources
            .AsNoTracking()
            .OrderBy(x => x.Source)
            .ToListAsync();

        var cards = await dbContext.Cards
            .AsNoTracking()
            .Where(x => x.Active != false)
            .OrderBy(x => x.CardName)
            .ToListAsync();

        var methods = await dbContext.Methods
            .AsNoTracking()
            .OrderBy(x => x.MethodName)
            .ToListAsync();

        var banks = await dbContext.Banks
            .AsNoTracking()
            .OrderBy(x => x.BankName)
            .ToListAsync();

        var centers1 = await dbContext.Centers1
            .AsNoTracking()
            .OrderBy(x => x.CenterName)
            .ToListAsync();

        var centers2 = await dbContext.Centers2
            .AsNoTracking()
            .OrderBy(x => x.CenterName)
            .ToListAsync();

        var centers3 = await dbContext.Centers3
            .AsNoTracking()
            .OrderBy(x => x.CenterName)
            .ToListAsync();

        return new JournalEntryLookupsVm
        {
            Accounts = accounts,
            Sources = sources.Where(source => access.Has(SectionAccess.Source(source.SourceID))).ToList(),
            Cards = cards,
            Methods = methods,
            Banks = banks,
            Centers1 = centers1,
            Centers2 = centers2,
            Centers3 = centers3,
            VatRates = vatRates
        };
    }

    public async Task<JournalEntryDetailVm?> GetJournalEntryDetailAsync(int glid)
    {
        if (!sessionService.IsAuthenticated || !sessionService.SelectedPeriodId.HasValue) return null;
        await access.RequireEntryAsync(glid);
        var header = await dbContext.GLEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.GLID == glid && x.PeriodID == sessionService.SelectedPeriodId);

        if (header == null)
        {
            return null;
        }

        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.GLID == glid)
            .OrderBy(x => x.TransID)
            .ToListAsync();

        return new JournalEntryDetailVm
        {
            Header = header,
            Transactions = transactions
        };
    }

    public async Task<ServiceResultVm> SaveJournalEntryAsync(JournalEntryUpsertVm request)
    {
        if (!sessionService.IsAuthenticated || !sessionService.SelectedPeriodId.HasValue)
            return new() { Success = false, Message = "يجب تسجيل الدخول واختيار الفترة المالية أولاً." };
        if (request.Header.PeriodID.HasValue && request.Header.PeriodID != sessionService.SelectedPeriodId)
            return new() { Success = false, Message = "لا يمكن نقل القيد إلى فترة مالية أخرى." };
        await access.RequireAsync(SectionAccess.Source(request.Header.SourceID));
        if (request.Header.GLID > 0) await access.RequireEntryAsync(request.Header.GLID);
        else if (request.Header.Archive == true && !access.Has("Archive"))
            return new() { Success = false, Message = "لا تملك صلاحية الأرشفة." };
        var validationError = JournalValidation.ValidateLines(request.Transactions);
        if (validationError is not null)
            return new() { Success = false, Message = validationError };
        var transactionRows = request.Transactions
            .Where(t => t.ACCID.HasValue)
            .Select(t => new TransEntity
            {
                ACCID = t.ACCID,
                DR = t.DR ?? 0m,
                CR = t.CR ?? 0m,
                TransDesc = t.TransDesc,
                ItemCode = t.ItemCode,
                Amount = t.Amount,
                TaxRate = t.TaxRate,
                TaxAmount = t.TaxAmount,
                TotalAmount = t.TotalAmount,
                Qty = t.Qty,
                Unit = t.Unit,
                UnitPrice = t.UnitPrice,
                Discount = t.Discount,
                DocRef = t.DocRef,
                DocDate = t.DocDate,
                Center3 = t.Center3
            })
            .ToList();

        if (transactionRows.Count == 0)
        {
            return new ServiceResultVm
            {
                Success = false,
                Message = "لا يمكن حفظ القيد بدون سجلات حركة مرتبطة"
            };
        }

        var originalHeaderId = request.Header.GLID;
        var attempt = 0;
        try
        {
            var executionStrategy = dbContext.Database.CreateExecutionStrategy();
            return await executionStrategy.ExecuteAsync(async () =>
            {
                // Do not replay writes after an ambiguous commit or reuse generated identities.
                if (++attempt > 1) throw new InvalidOperationException("Journal write requires reconciliation before retry.");
                await using var databaseTransaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var isNew = request.Header.GLID == 0;

            var username = string.IsNullOrWhiteSpace(sessionService.CurrentUsername)
                ? "Unknown"
                : sessionService.CurrentUsername.Trim();
            var savedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            // Keep the full timestamp within the Info column's 50-character limit.
            var maxUsernameLength = 50 - savedAt.Length - 1;
            if (username.Length > maxUsernameLength)
            {
                username = username[..maxUsernameLength];
            }
            request.Header.Info = $"{username} {savedAt}";

            if (!request.Header.PeriodID.HasValue && sessionService.SelectedPeriodId.HasValue)
            {
                request.Header.PeriodID = sessionService.SelectedPeriodId.Value;
            }

            if (!request.Header.PeriodID.HasValue)
            {
                return new ServiceResultVm { Success = false, Message = "يجب تحديد الفترة المالية قبل حفظ الحركة" };
            }

            var period = await dbContext.Periods.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PeriodID == request.Header.PeriodID.Value);
            var periodError = JournalValidation.ValidatePeriod(period, request.Header.GLDate);
            if (periodError is not null)
                return new ServiceResultVm { Success = false, Message = periodError };

            if (isNew)
            {

                var generalStart = Math.Max(period!.GLID ?? 1, 1);
                if (request.Header.SourceID == 4 && await dbContext.GLEntries.AnyAsync(x =>
                    x.PeriodID == request.Header.PeriodID && x.SourceID == 4))
                    return new ServiceResultVm { Success = false, Message = "يوجد قيد افتتاحي لهذه الفترة. عدّل القيد الموجود لتجنب تكرار الأرصدة." };
                var lastGeneralSequence = await dbContext.GLEntries
                    .Where(x => x.PeriodID == period.PeriodID && x.GLSN.HasValue)
                    .MaxAsync(x => (int?)x.GLSN);
                request.Header.GLSN = Math.Max(generalStart, (lastGeneralSequence ?? generalStart - 1) + 1);

                var documentStart = request.Header.SourceID switch
                {
                    2 => period.RCID,
                    3 => period.PMID,
                    5 => period.SalesID,
                    6 => period.PurchID,
                    _ => null
                };
                if (documentStart.HasValue)
                {
                    var normalizedStart = Math.Max(documentStart.Value, 1);
                    var lastDocumentSequence = await dbContext.GLEntries
                        .Where(x => x.PeriodID == period.PeriodID
                                    && x.SourceID == request.Header.SourceID
                                    && x.SanadID.HasValue)
                        .MaxAsync(x => (int?)x.SanadID);
                    request.Header.SanadID = Math.Max(normalizedStart, (lastDocumentSequence ?? normalizedStart - 1) + 1);
                }
                else
                {
                    request.Header.SanadID = null;
                }

                dbContext.GLEntries.Add(request.Header);
            }
            else
            {
                var existing = await dbContext.GLEntries
                    .Include(x => x.Transactions)
                    .FirstOrDefaultAsync(x => x.GLID == request.Header.GLID && x.PeriodID == sessionService.SelectedPeriodId);

                if (existing == null)
                {
                    return new ServiceResultVm
                    {
                        Success = false,
                        Message = "القيد المطلوب غير موجود"
                    };
                }

                if (!access.Has(SectionAccess.Source(existing.SourceID)))
                    return new ServiceResultVm { Success = false, Message = "لا تملك صلاحية تعديل هذا السجل." };
                if (!access.Has("Archive") && (existing.Archive == true) != (request.Header.Archive == true))
                    return new ServiceResultVm { Success = false, Message = "لا تملك صلاحية تغيير الأرشفة." };
                existing.GLSN = request.Header.GLSN;
                existing.GLDate = request.Header.GLDate;
                existing.PeriodID = request.Header.PeriodID;
                existing.SourceID = request.Header.SourceID;
                existing.GLDesc = request.Header.GLDesc;
                existing.GLRef = request.Header.GLRef;
                existing.GLACC = request.Header.GLACC;
                existing.CardID = request.Header.CardID;
                existing.MethodID = request.Header.MethodID;
                existing.BankName = request.Header.BankName;
                existing.SanadID = request.Header.SanadID;
                existing.Archive = request.Header.Archive;
                existing.Info = request.Header.Info;
                existing.Center1 = request.Header.Center1;
                existing.Center2 = request.Header.Center2;

                foreach (var oldTrans in existing.Transactions.ToList())
                {
                    dbContext.Transactions.Remove(oldTrans);
                }

                request.Header = existing;
            }

            await dbContext.SaveChangesAsync();

            foreach (var row in transactionRows)
            {
                row.GLID = request.Header.GLID;
            }

            dbContext.Transactions.AddRange(transactionRows);

            await dbContext.SaveChangesAsync();
            await databaseTransaction.CommitAsync();

            var movementName = request.Header.SourceID switch
            {
                2 => "سند القبض",
                3 => "سند الصرف",
                5 => "فاتورة المبيعات",
                6 => "فاتورة المشتريات",
                _ => "القيد"
            };

                return new ServiceResultVm
                {
                    Success = true,
                    Message = isNew ? $"تم إضافة {movementName} بنجاح" : $"تم تعديل {movementName} بنجاح",
                    EntityId = request.Header.GLID
                };
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Journal save failed: {0}", ex);
            dbContext.ChangeTracker.Clear();
            request.Header.GLID = originalHeaderId;
            return new ServiceResultVm
            {
                Success = false,
                Message = "تعذر تأكيد حفظ القيد. حدّث السجل وتحقق من وجوده قبل إعادة المحاولة."
            };
        }
    }

    public async Task<ServiceResultVm> DeleteJournalEntryAsync(int glid)
    {
        if (!sessionService.IsAuthenticated || !sessionService.SelectedPeriodId.HasValue)
            return new() { Success = false, Message = "يجب تسجيل الدخول واختيار الفترة المالية أولاً." };
        await access.RequireEntryAsync(glid);
        var attempt = 0;
        try
        {
            return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
            if (++attempt > 1) throw new InvalidOperationException("Journal deletion requires reconciliation before retry.");
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var entry = await dbContext.GLEntries
                .Include(x => x.Transactions)
                .FirstOrDefaultAsync(x => x.GLID == glid && x.PeriodID == sessionService.SelectedPeriodId);

            if (entry == null)
            {
                return new ServiceResultVm
                {
                    Success = false,
                    Message = "القيد المطلوب غير موجود"
                };
            }

            if (!access.Has(SectionAccess.Source(entry.SourceID)))
                return new ServiceResultVm { Success = false, Message = "لا تملك صلاحية حذف هذا السجل." };
            var period = await dbContext.Periods.AsNoTracking().FirstOrDefaultAsync(x => x.PeriodID == entry.PeriodID);
            if (period is null || period.PeriodClose == true)
                return new() { Success = false, Message = "لا يمكن حذف قيد من فترة مغلقة أو غير موجودة." };

            foreach (var trans in entry.Transactions.ToList())
            {
                dbContext.Transactions.Remove(trans);
            }

            dbContext.GLEntries.Remove(entry);
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new ServiceResultVm
            {
                Success = true,
                Message = "تم حذف القيد بنجاح"
            };
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Journal delete failed: {0}", ex);
            dbContext.ChangeTracker.Clear();
            return new ServiceResultVm
            {
                Success = false,
                Message = "تعذر حذف القيد. قد يكون مرتبطًا بسجلات أخرى."
            };
        }
    }
}

