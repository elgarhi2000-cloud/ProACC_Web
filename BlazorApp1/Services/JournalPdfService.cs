using System.Text.Json;
using Microsoft.Playwright;

namespace BlazorApp1.Services;

// Render the same document builder and print CSS used by the preview.
public sealed class JournalPdfService(IWebHostEnvironment environment, IConfiguration configuration)
{
    private static readonly SemaphoreSlim RenderSlots = new(2);

    public async Task<byte[]> GenerateAsync(JournalEntryExportVm report)
    {
        await RenderSlots.WaitAsync();
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                Channel = configuration["Pdf:BrowserChannel"] ?? (OperatingSystem.IsWindows() ? "msedge" : null),
                Timeout = 30000
            });
            await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
            // All report assets are inline; never send accounting data or fetch remote resources.
            await context.RouteAsync("**/*", route => route.AbortAsync());
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(30000);
            foreach (var script in new[] { "report-designer.js", "journal-entry-export.js" })
            {
                await page.AddScriptTagAsync(new() { Content = await File.ReadAllTextAsync(Path.Combine(environment.WebRootPath, "js", script)) });
            }
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var html = await page.EvaluateAsync<string>("json => window.journalEntryExport.previewDocument(JSON.parse(json))", json);
            await page.SetContentAsync(html, new() { WaitUntil = WaitUntilState.Load });
            await page.EvaluateAsync("async () => { await document.fonts.ready; await Promise.all(Array.from(document.images, image => image.decode().catch(() => {}))); }");
            return await page.PdfAsync(new() { Format = "A4", PreferCSSPageSize = true, PrintBackground = true });
        }
        finally { RenderSlots.Release(); }
    }
}
