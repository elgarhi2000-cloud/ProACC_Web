using BlazorApp1.Data.ProAcc;
using BlazorApp1.Services;
using Microsoft.EntityFrameworkCore;

// Read-only integration regression. Supply an existing company connection via the environment.
var connection = Environment.GetEnvironmentVariable("PROACC_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set PROACC_TEST_CONNECTION to a test company database.");
var session = new SessionService();
await session.SelectDatabaseAsync("test", "test");
await session.LoginAsync("integration-test", 2026);
var options = new DbContextOptionsBuilder<ProAccDbContext>().UseSqlServer(connection).Options;
var accessor = new IndependentAccessor(options);
var service = new AccountingDataService(accessor, session, new AllowedAccess());
var first = service.GetDashboardDataAsync();
var second = service.GetDashboardDataAsync();
var results = await Task.WhenAll(first, second);
if (accessor.Created.Count != 2 || ReferenceEquals(accessor.Created[0], accessor.Created[1]))
    throw new Exception("Dashboard loads must use separate contexts.");
if (results[0].JournalEntriesCount != results[1].JournalEntriesCount || results[0].TotalDebit != results[1].TotalDebit)
    throw new Exception("Concurrent dashboard results differ.");
foreach (var context in accessor.Created)
{
    try { _ = context.GLEntries.ToQueryString(); throw new Exception("Context was not disposed."); }
    catch (ObjectDisposedException) { }
}
Console.WriteLine("PASS: overlapping dashboard loads, no shared-context access, consistent results, contexts disposed.");

sealed class IndependentAccessor(DbContextOptions<ProAccDbContext> options) : IProAccDbContextAccessor
{
    public List<ProAccDbContext> Created { get; } = [];
    public ProAccDbContext Context => throw new Exception("Dashboard accessed the circuit's shared context.");
    public ProAccDbContext CreateContext()
    {
        var context = new ProAccDbContext(options);
        Created.Add(context);
        return context;
    }
    public void Reset() => throw new NotSupportedException();
}

sealed class AllowedAccess : IUserAccessService
{
    public bool Has(string permission) => true;
    public Task RefreshAsync() => Task.CompletedTask;
    public Task RequireAsync(params string[] anyPermission) => Task.CompletedTask;
    public Task<bool> CanOpenAsync(string relativeUrl) => Task.FromResult(true);
    public Task RequireEntryAsync(int id, int? periodId = null) => Task.CompletedTask;
}
