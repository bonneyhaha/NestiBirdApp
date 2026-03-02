using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nesti.Helpers;
using Nesti.Services;

namespace Nesti.Controls;

public partial class NotificationControl : UserControl
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const string AvatarGifUri = "pack://application:,,,/assets/jarvis.gif";

    // ── Public surface ────────────────────────────────────────────────────────
    public string? NotificationId  { get; private set; }
    public string? NotificationUrl { get; private set; }

    public event EventHandler? DismissRequested;
    public event EventHandler? SnoozeRequested;

    // ── Per-notification API context ──────────────────────────────────────────
    // userId == -1 means the notification is a broadcast; skip all API calls.
    private int?   _userId;
    private string _mEmpId = string.Empty;

    // ── Constructor ───────────────────────────────────────────────────────────
    public NotificationControl()
    {
        InitializeComponent();

        // Point at the shared WriteableBitmap — the player blits frame updates into it
        // automatically. No event subscription, no cleanup wiring needed here.
        AvatarImage.Source = SharedGifPlayer.Get(AvatarGifUri).SharedBitmap;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Populates the card.
    /// userId: the "user id" field from the WebSocket notification (-1 = broadcast, skip API).
    /// mEmpId: the mEmpID resolved at startup, used as userSession in API payloads.
    /// </summary>
    public void SetData(string notificationId, string title, string body, string? url,
                        int? userId, string mEmpId)
    {
        NotificationId    = notificationId;
        TitleBlock.Text   = title;
        MessageBlock.Text = body;
        NotificationUrl   = url;
        _userId           = userId;
        _mEmpId           = mEmpId;
    }

    // ── Animations ────────────────────────────────────────────────────────────
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var dur  = new Duration(TimeSpan.FromMilliseconds(1000));
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

        SlideTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(400, 0, dur) { EasingFunction = ease });
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.9, 1, dur) { EasingFunction = ease });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.9, 1, dur) { EasingFunction = ease });
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400))));
    }

    public void AnimateOut(Action? onComplete = null)
    {
        var dur  = new Duration(TimeSpan.FromMilliseconds(500));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fade = new DoubleAnimation(1, 0, dur);
        fade.Completed += (_, _) => onComplete?.Invoke();

        SlideTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, 400, dur) { EasingFunction = ease });
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.9, dur));
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.9, dur));
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Drops the reference to the shared bitmap and clears text.
    /// The SharedGifPlayer keeps running — it owns the WriteableBitmap.
    /// </summary>
    public void Cleanup()
    {
        AvatarImage.Source = null;
        TitleBlock.Text    = null;
        MessageBlock.Text  = null;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(NotificationUrl))
            OpenUrl(NotificationUrl);

        // Treat card click as Manual Read
        CallMarkAsRead("Manual Read");
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Snooze_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        (sender as UIElement)?.CaptureMouse();
    }

    private void Snooze_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        (sender as UIElement)?.ReleaseMouseCapture();

        // Only call snooze API in real mode and when userId != -1
        if (AppConfig.UseRealWebSocket && _userId != -1 && !string.IsNullOrEmpty(NotificationId))
            _ = NotificationApiService.SnoozeAsync(NotificationId, _mEmpId);

        SnoozeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Dismiss_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        (sender as UIElement)?.CaptureMouse();
    }

    private void Dismiss_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        (sender as UIElement)?.ReleaseMouseCapture();

        CallMarkAsRead("Manual Read");
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls MARK_AS_READ_URL only when:
    ///   • USE_REAL_WEBSOCKET = true
    ///   • user_id from the notification is NOT -1
    ///   • instanceId (NotificationId) is not empty
    /// </summary>
    private void CallMarkAsRead(string actionTaken)
    {
        if (AppConfig.UseRealWebSocket && _userId != -1 && !string.IsNullOrEmpty(NotificationId))
            _ = NotificationApiService.MarkAsReadAsync(NotificationId, _mEmpId, actionTaken);
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }
}
