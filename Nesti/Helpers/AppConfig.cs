using System.IO;
using DotNetEnv;

namespace Nesti.Helpers;

/// <summary>
/// Reads configuration from the .env file that lives next to the executable.
/// All values can be changed in .env and take effect on the next app start.
/// </summary>
public static class AppConfig
{
    static AppConfig()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
        if (File.Exists(path))
            Env.Load(path);
    }

    // ── WebSocket ─────────────────────────────────────────────────────────────
    public static string WsUrl                  => Str("WS_URL",                      "wss://localhost:8080/ws");
    public static int    WsReconnectBaseMs      => Int("WS_RECONNECT_BASE_MS",         2_000);
    public static int    WsReconnectMaxAttempts => Int("WS_RECONNECT_MAX_ATTEMPTS",    10);
    public static int    WsHeartbeatIntervalMs  => Int("WS_HEARTBEAT_INTERVAL_MS",     25_000);
    public static int    WsPongTimeoutMs        => Int("WS_PONG_TIMEOUT_MS",           7_000);

    // ── REST API ──────────────────────────────────────────────────────────────
    // Called as: GET {ApiBaseUrl}?CorpID=Corp\{windowsLogin}
    // Response JSON must contain "mEmpID". WS_URL is then {WS_URL}/{mEmpID}.
    public static string ApiBaseUrl          => Str("API_BASE_URL",              "");
    public static string ApiGetFullnamePath  => Str("API_GET_FULLNAME_PATH",     "");

    // ── App ───────────────────────────────────────────────────────────────────
    public static string BirdDefaultUrl         => Str("BIRD_DEFAULT_URL",          "");
    public static int    NotificationDurationMs => Int("NOTIFICATION_DURATION_MS",  10_000);
    public static int    MaxNotifications       => Int("MAX_NOTIFICATIONS",          5);
    public static bool   SoundEnabled           => Bool("SOUND_ENABLED",            true);

    // ── Test / dummy mode ─────────────────────────────────────────────────────
    /// <summary>
    /// true  → connect to the real WebSocket server.
    /// false → use DummyWebSocketService (fires fake notifications on a timer).
    /// </summary>
    public static bool UseRealWebSocket => Bool("USE_REAL_WEBSOCKET", true);

    /// <summary>How often the dummy service fires a test notification (ms).</summary>
    public static int  DummyIntervalMs  => Int("DUMMY_INTERVAL_MS",   5_000);

    /// <summary>
    /// How many notifications to fire instantly on startup for burst/stress testing.
    /// 0 = disabled (normal timer-only mode).
    /// </summary>
    public static int  DummyBurstCount  => Int("DUMMY_BURST_COUNT",   0);

    // ── Notification action APIs ──────────────────────────────────────────────
    /// <summary>POST: snooze a notification. URL from MARS_SNOOZE_URL in .env.</summary>
    public static string MarsSnoozeUrl  => Str("MARS_SNOOZE_URL",     "");

    /// <summary>POST: mark notification as read. URL from MARK_AS_READ_URL in .env.</summary>
    public static string MarkAsReadUrl      => Str("MARK_AS_READ_URL",          "");

    /// <summary>Snooze duration in minutes sent in the snooze payload.</summary>
    public static int SnoozeDurationMinutes => Int("SNOOZE_DURATION_MINUTES",    5);

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string Str(string key, string def) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v.Trim() : def;

    private static int Int(string key, int def) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;

    private static bool Bool(string key, bool def) =>
        bool.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;
}
