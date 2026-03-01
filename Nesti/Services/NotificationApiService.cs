using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Nesti.Helpers;

namespace Nesti.Services;

/// <summary>
/// Handles notification action API calls:
///   • Snooze  → POST MARS_SNOOZE_URL
///   • Dismiss → POST MARK_AS_READ_URL  +  stores ID in local dismissed.json
///
/// All methods fire-and-forget: they never throw to the caller.
/// Fill the snooze payload body below when the API contract is defined.
/// </summary>
public static class NotificationApiService
{
    // Local store path: %AppData%\Nesti\dismissed.json
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Nesti", "dismissed.json");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    // Loaded once from disk on first access; all subsequent reads/writes use this cache.
    // Eliminates repeated full-file deserialise → LOH allocation on every dismiss.
    private static HashSet<string>? _dismissedCache;

    // ── Snooze (MARS) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the MARS snooze API for the given notification.
    /// Payload: fill the body object below once the API contract is known.
    /// </summary>
    public static async Task SnoozeAsync(string notificationId)
    {
        var url = AppConfig.MarsSnoozeUrl;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            // ── TODO: replace body with real payload when API contract is ready ──
            var body = new
            {
                id = notificationId
                // Add more fields here, e.g.:
                // duration = "1h",
                // reason   = "busy"
            };

            var json    = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await Http.PostAsync(url, content);
        }
        catch { /* never crash on API failure */ }
    }

    // ── Mark as read ──────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the mark-as-read API and persists the ID locally so the
    /// notification is never shown again after an app restart.
    /// </summary>
    public static async Task MarkAsReadAsync(string notificationId)
    {
        // 1. Call API
        var url = AppConfig.MarkAsReadUrl;
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                var body    = JsonSerializer.Serialize(new { id = notificationId });
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                await Http.PostAsync(url, content);
            }
            catch { }
        }

        // 2. Store locally
        await StoreDismissedIdAsync(notificationId);
    }

    /// <summary>
    /// Returns true if the notification was previously dismissed/read.
    /// Uses the in-memory cache — no disk I/O after first call.
    /// </summary>
    public static bool IsDismissed(string notificationId) =>
        GetCache().Contains(notificationId);

    // ── Local file helpers ────────────────────────────────────────────────────

    private static async Task StoreDismissedIdAsync(string id)
    {
        try
        {
            var ids = GetCache();
            if (ids.Add(id))
            {
                // Write to disk asynchronously; subsequent calls use in-memory cache.
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                await File.WriteAllTextAsync(StorePath, JsonSerializer.Serialize(ids));
            }
        }
        catch { }
    }

    /// <summary>
    /// Returns the in-memory dismissed ID cache, loading from disk the first time.
    /// All subsequent calls return the same HashSet instance — zero disk I/O.
    /// </summary>
    private static HashSet<string> GetCache()
    {
        if (_dismissedCache is not null) return _dismissedCache;

        try
        {
            if (File.Exists(StorePath))
            {
                var raw = File.ReadAllText(StorePath);
                _dismissedCache = JsonSerializer.Deserialize<HashSet<string>>(raw) ?? new();
                return _dismissedCache;
            }
        }
        catch { }

        _dismissedCache = new HashSet<string>();
        return _dismissedCache;
    }
}
