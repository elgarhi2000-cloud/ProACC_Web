namespace BlazorApp1.Services;

// Shared across circuits: refreshing the browser must not reset the attempt limit.
public static class LoginAttemptLimiter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (int Count, DateTimeOffset Until)> Attempts = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryBegin(string key)
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in Attempts.Where(x => x.Value.Until <= now).Select(x => x.Key).ToArray())
                Attempts.Remove(expired);
            if (Attempts.TryGetValue(key, out var window))
            {
                if (window.Count >= 5) return false;
                Attempts[key] = (window.Count + 1, window.Until);
                return true;
            }
            if (Attempts.Count >= 10000) return false;
            Attempts[key] = (1, now.AddMinutes(5));
            return true;
        }
    }

    public static void Reset(string key)
    {
        lock (Gate) Attempts.Remove(key);
    }
}
