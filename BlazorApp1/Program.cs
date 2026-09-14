using BlazorApp1.Components;
using BlazorApp1.Components.Account;
using BlazorApp1.Data;
using BlazorApp1.Data.ProAcc;
using BlazorApp1.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("BlazorApp1")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();

// Each Blazor circuit owns an isolated session so one company can never leak into another user's session.
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddScoped<ReportDesignService>();
builder.Services.AddScoped<JournalPdfService>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddSingleton<ISecureCompanyDatabaseRegistry, SecureCompanyDatabaseRegistry>();
builder.Services.AddSingleton<ICompanyDatabaseService, CompanyDatabaseService>();
builder.Services.AddScoped<IProAccDbContextAccessor, ProAccDbContextAccessor>();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// تسجيل خدمة المصادقة المخصصة
builder.Services.AddScoped<ICustomAuthService, CustomAuthService>();
builder.Services.AddScoped<IAccountingDataService, AccountingDataService>();
builder.Services.AddScoped<JournalAttachmentService>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();
builder.Services.AddScoped<IDatabaseSettingsService, DatabaseSettingsService>();
builder.Services.AddScoped<ICompanyDatabaseAdminService, CompanyDatabaseAdminService>();
builder.Services.AddScoped<ISystemAdminSession, SystemAdminSession>();
builder.Services.AddSingleton<ISystemAdminAuditService, SystemAdminAuditService>();
builder.Services.AddScoped<ISystemAdminAuthService, SystemAdminAuthService>();

var app = builder.Build();

// Initialize/migrate the encrypted company registry before accepting logins.
_ = app.Services.GetRequiredService<ISecureCompanyDatabaseRegistry>().GetAll();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// Liveness only; database connectivity is verified separately before sharing the demo.
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapGet("/assets/proacc-logo.ico", (IWebHostEnvironment env) =>
{
    var logoPath = Path.Combine(env.ContentRootPath, "Icon.ico");
    return Results.File(logoPath, "image/x-icon");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
