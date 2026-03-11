using System.Windows.Threading;
using Nesti.Helpers;
using Nesti.Models;

namespace Nesti.Services;

/// <summary>
/// Fires pre-defined test notifications on a timer so you can develop and
/// test the UI without a real WebSocket server.
///
/// Activated when USE_REAL_WEBSOCKET=false in .env.
/// Interval controlled by DUMMY_INTERVAL_MS in .env.
/// </summary>
public sealed class DummyWebSocketService : IWebSocketSource
{
    public event EventHandler<NotificationMessage>? NotificationReceived;
    public event EventHandler<string>?              StatusChanged;

    private readonly DispatcherTimer _timer;
    private int _index;

    // ── Sample notifications (rotates through these in order) ─────────────────
    private static readonly (string Title, string Message, string Url)[] Samples =
    [
        (
            "New Task Assigned",
            "You have been assigned: Review Q1 Performance Report",
            "https://your-app-url.com/tasks/1"
        ),
        (
            "Meeting Reminder",
            "Team standup starts in 10 minutes — Conference Room 3B",
            "https://your-app-url.com/calendar"
        ),
        (
            "PR Review Requested",
            "Harshit requested your review on: feature/auth-update",
            "https://your-app-url.com/pr/42"
        ),
        (
            "Deployment Complete",
            "Production release v2.3.1 deployed successfully",
            "https://your-app-url.com/deployments"
        ),
        (
            "Alert: High CPU Usage",
            "Server prod-01 is at 92% CPU — check the monitoring dashboard",
            "https://your-app-url.com/monitoring"
        ),
        (
            "New Comment on Your Post",
            "Riya commented on your update in the #general channel",
            "https://your-app-url.com/feed/123"
        ),
        (
            "Weekly Summary Ready",
            "Your activity report for this week is available to view",
            "https://your-app-url.com/reports/weekly"
        ),
    ];

    public DummyWebSocketService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AppConfig.DummyIntervalMs)
        };
        _timer.Tick += OnTick;
    }

    public Task ConnectAsync(string url)
    {
        StatusChanged?.Invoke(this, "connected (dummy mode)");

        var burst = AppConfig.DummyBurstCount;
        if (burst > 0)
            _ = FireBurstAsync(burst);

        _timer.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fires <paramref name="count"/> notifications as fast as possible
    /// to stress-test the UI (eviction, animations, memory under load).
    /// A 300 ms startup delay lets the window finish loading first.
    /// </summary>
    private async Task FireBurstAsync(int count)
    {
        await Task.Delay(300);
        System.Diagnostics.Debug.WriteLine($"[Nesti] Burst: firing {count} notifications");

        for (int i = 0; i < count; i++)
        {
            FireOne();
            // 10 ms gap lets the UI thread process each Dispatcher.Invoke
            // without freezing, while still appearing near-simultaneous.
            await Task.Delay(10);
        }

        System.Diagnostics.Debug.WriteLine($"[Nesti] Burst: done");
    }

    private void OnTick(object? sender, EventArgs e) => FireOne();

    private void FireOne()
    {
        var (title, message, notifUrl) = Samples[_index % Samples.Length];
        _index++;

        var uniqueId = $"dummy-{_index}-{DateTime.Now.Ticks}";

        NotificationReceived?.Invoke(this, new NotificationMessage
        {
            Id          = uniqueId,
            InstanceId  = uniqueId,
            Title       = title,
            Message     = message,
            Url         = notifUrl,
            Timestamp   = DateTime.Now
        });
    }

    public void Dispose() => _timer.Stop();
}
