using System.Net.Http;
using System.Text;
using System.Text.Json;
using Nesti.Helpers;

namespace Nesti.Services;

/// <summary>
/// Handles notification action API calls.
/// All methods are fire-and-forget — they never throw to the caller.
/// All calls are skipped when USE_REAL_WEBSOCKET=false (dummy mode).
/// All calls are skipped when the notification's user_id == -1.
/// </summary>
public static class NotificationApiService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Disposes the shared HttpClient. Call once on application exit.
    /// </summary>
    public static void Dispose() => Http.Dispose();

    // ── Mark as read ──────────────────────────────────────────────────────────

    /// <summary>
    /// POST MARK_AS_READ_URL
    /// Payload:
    /// {
    ///   "instance_id":  "{instanceId from websocket}",
    ///   "userSession":  "{mEmpID used to connect to WebSocket}",
    ///   "isClicked":    true,
    ///   "actionTaken":  "Automatic Read"  |  "Manual Read"
    /// }
    /// </summary>
    public static async Task MarkAsReadAsync(string instanceId, long userSession, string actionTaken)
    {
        var url = AppConfig.MarkAsReadUrl;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[Nesti] MarkAsReadAsync: POST {url} instance={instanceId} action={actionTaken}");
            var json = JsonSerializer.Serialize(new
            {
                instance_id  = instanceId,
                userSession  = userSession,
                isClicked    = true,
                actionTaken  = actionTaken
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await Http.PostAsync(url, content);
        }
        catch { /* never crash on API failure */ }
    }

    // ── Snooze (MARS) ─────────────────────────────────────────────────────────

    /// <summary>
    /// POST MARS_SNOOZE_URL
    /// Payload:
    /// {
    ///   "instance_id":          "{instanceId from websocket}",
    ///   "userSession":          "{mEmpID used to connect to WebSocket}",
    ///   "snoozeDurationMinutes": {SNOOZE_DURATION_MINUTES from .env, default 5}
    /// }
    /// </summary>
    public static async Task SnoozeAsync(string instanceId, long userSession)
    {
        var url = AppConfig.MarsSnoozeUrl;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            var json = JsonSerializer.Serialize(new
            {
                instance_id           = instanceId,
                userSession           = userSession,
                snoozeDurationMinutes = AppConfig.SnoozeDurationMinutes
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await Http.PostAsync(url, content);
        }
        catch { /* never crash on API failure */ }
    }
}
