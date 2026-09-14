using System.Text.Json;

namespace BlazorApp1.Services;

public sealed record SystemAdminLoginResult(bool Success, string Message);

public interface ISystemAdminSession
{
    bool IsAuthenticated { get; }
    string? UserName { get; }
    DateTimeOffset? ExpiresAt { get; }
    void SignIn(string userName);
    void SignOut();
    void EnsureAuthorized();
}

public sealed class SystemAdminSession(IConfiguration configuration) : ISystemAdminSession
{
    private string? userName;
    private DateTimeOffset? expiresAt;

    public bool IsAuthenticated
    {
        get
        {
            if (string.IsNullOrWhiteSpace(userName) || expiresAt <= DateTimeOffset.Now)
            {
                SignOut();
                return false;
            }
            return true;
        }
    }

    public string? UserName => IsAuthenticated ? userName : null;
    public DateTimeOffset? ExpiresAt => IsAuthenticated ? expiresAt : null;

    public void SignIn(string value)
    {
        userName = value;
        var minutes = Math.Clamp(configuration.GetValue("SystemAdmin:SessionMinutes", 20), 5, 120);
        expiresAt = DateTimeOffset.Now.AddMinutes(minutes);
    }

    public void SignOut()
    {
        userName = null;
        expiresAt = null;
    }

    public void EnsureAuthorized()
    {
        if (!IsAuthenticated)
            throw new UnauthorizedAccessException("انتهت جلسة مدير النظام أو لا توجد صلاحية SystemAdmin.");
    }
}

public sealed record SystemAdminAuditEntry(
    DateTimeOffset Timestamp,
    string UserName,
    string Action,
    string Target,
    bool Success,
    string Message);

public interface ISystemAdminAuditService
{
    Task WriteAsync(string userName, string action, string target, bool success, string message, CancellationToken cancellationToken = default);
}

public sealed class SystemAdminAuditService(IWebHostEnvironment environment) : ISystemAdminAuditService
{
    private readonly SemaphoreSlim fileLock = new(1, 1);
    private readonly string auditPath = Path.Combine(environment.ContentRootPath, "App_Data", "system-admin-audit.jsonl");

    public async Task WriteAsync(
        string userName,
        string action,
        string target,
        bool success,
        string message,
        CancellationToken cancellationToken = default)
    {
        var entry = new SystemAdminAuditEntry(DateTimeOffset.Now, userName, action, target, success, message);
        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
            await File.AppendAllTextAsync(auditPath, line, cancellationToken);
        }
        finally
        {
            fileLock.Release();
        }
    }
}

public interface ISystemAdminAuthService
{
    Task<SystemAdminLoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemAdminAuthService(
    IConfiguration configuration,
    IDatabaseSettingsService databaseSettingsService,
    ISystemAdminSession session,
    ISystemAdminAuditService auditService) : ISystemAdminAuthService
{
    public async Task<SystemAdminLoginResult> LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var configuredUser = configuration["SystemAdmin:UserName"] ?? "developer";
        var attemptKey = "system-admin:" + configuredUser;
        if (!LoginAttemptLimiter.TryBegin(attemptKey))
            return new(false, "تم إيقاف المحاولات مؤقتًا. أعد المحاولة بعد خمس دقائق.");
        var validUser = string.Equals(userName?.Trim(), configuredUser, StringComparison.OrdinalIgnoreCase);
        var validPassword = databaseSettingsService.VerifyDeveloperPassword(password ?? string.Empty);
        if (!validUser || !validPassword)
        {
            await auditService.WriteAsync(userName?.Trim() ?? "(empty)", "Login", "SystemAdmin", false, "فشل تسجيل الدخول.", cancellationToken);
            return new(false, "اسم حساب مدير النظام أو كلمة المرور غير صحيحة.");
        }

        LoginAttemptLimiter.Reset(attemptKey);
        session.SignIn(configuredUser);
        await auditService.WriteAsync(configuredUser, "Login", "SystemAdmin", true, "تم فتح جلسة مدير النظام.", cancellationToken);
        return new(true, "تم تسجيل الدخول بنجاح.");
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var userName = session.UserName ?? "(unknown)";
        session.SignOut();
        await auditService.WriteAsync(userName, "Logout", "SystemAdmin", true, "تم إنهاء جلسة مدير النظام.", cancellationToken);
    }
}
