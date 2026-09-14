using BlazorApp1.Data.ProAcc;
using Microsoft.EntityFrameworkCore;

namespace BlazorApp1.Services;

public interface IProAccDbContextAccessor
{
    ProAccDbContext Context { get; }
    void Reset();
}

/// <summary>
/// Owns one accounting DbContext for the currently selected company database.
/// It automatically replaces the context if the company selection changes.
/// </summary>
public sealed class ProAccDbContextAccessor(
    ISessionService sessionService,
    ICompanyDatabaseService companyDatabaseService) : IProAccDbContextAccessor, IDisposable
{
    private readonly object syncRoot = new();
    private ProAccDbContext? context;
    private string? contextDatabaseKey;
    private bool disposed;

    public ProAccDbContext Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var selectedKey = sessionService.SelectedDatabaseKey
                ?? throw new InvalidOperationException("يجب اختيار المنشأة قبل الوصول إلى بياناتها.");

            lock (syncRoot)
            {
                if (context is not null
                    && string.Equals(contextDatabaseKey, selectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return context;
                }

                ResetCore();
                var options = new DbContextOptionsBuilder<ProAccDbContext>()
                    .UseSqlServer(
                        companyDatabaseService.BuildConnectionString(selectedKey),
                        sql => sql.EnableRetryOnFailure(3))
                    .Options;

                context = new ProAccDbContext(options);
                contextDatabaseKey = selectedKey;
                return context;
            }
        }
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            ResetCore();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (syncRoot)
        {
            ResetCore();
            disposed = true;
        }
    }

    private void ResetCore()
    {
        context?.Dispose();
        context = null;
        contextDatabaseKey = null;
    }
}
