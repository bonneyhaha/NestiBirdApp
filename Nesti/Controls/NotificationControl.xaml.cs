using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nesti.Helpers;
using Nesti.Services;

namespace Nesti.Controls;

public partial class NotificationControl : UserControl
{
    // ── Public surface ────────────────────────────────────────────────────────
    public string? NotificationId  { get; private set; }
    public string? NotificationUrl { get; private set; }

    public event EventHandler? DismissRequested;

    // ── Per-notification API context ──────────────────────────────────────────
    // userId == -1 means the notification is a broadcast; skip all API calls.
    private int?  _userId;
    private long  _mEmpId;

    // ── Constructor ───────────────────────────────────────────────────────────
    public NotificationControl()
    {
        InitializeComponent();
        // Re-subscribe every time the control is attached to the visual tree
        // so pooled controls animate in correctly on reuse.
        Loaded += (_, _) => AnimateIn();
    }

    /// <summary>
    /// Populates the card.
    /// userId: the "user_id" field from the WebSocket notification (-1 = broadcast, skip API).
    /// mEmpId: the mEmpID resolved at startup, used as userSession in API payloads.
    /// </summary>
    public void SetData(string notificationId, string title, string body, string? url,
                        int? userId, long mEmpId)
    {
        NotificationId    = notificationId;
        TitleBlock.Text   = title;
        MessageBlock.Text = body;
        NotificationUrl   = url;
        _userId           = userId;
        _mEmpId           = mEmpId;
    }

    // ── Animations ────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the slide-in animation. Called automatically on Loaded (new cards)
    /// and also fires on every re-attachment to the visual tree (pooled cards).
    /// </summary>
    public void AnimateIn()
    {
        // 400 ms is snappy enough to feel smooth while releasing animation
        // clock objects twice as fast as the previous 1000 ms duration.
        var dur  = new Duration(TimeSpan.FromMilliseconds(400));
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

        SlideTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(350, 0, dur) { EasingFunction = ease });
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, 1, dur) { EasingFunction = ease });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, 1, dur) { EasingFunction = ease });
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(250))));
    }

    /// <summary>
    /// Smoothly repositions the card after the stack shifts vertically.
    /// <paramref name="startOffset"/> is the Y distance to animate FROM (usually negative
    /// when card shifts downward, so it visually appears to glide into its new slot).
    /// </summary>
    public void AnimateRepositionY(double startOffset)
    {
        SlideTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(startOffset, 0, new Duration(TimeSpan.FromMilliseconds(250)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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
    /// Clears event handlers, stops animations, and releases text references.
    /// Called before the card is removed from the visual tree.
    /// </summary>
    public void Cleanup()
    {
        // Stop all running animations to release animation clock resources
        BeginAnimation(OpacityProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        // Clear event handler subscriptions to prevent memory leaks
        DismissRequested = null;

        TitleBlock.Text   = null;
        MessageBlock.Text = null;

        System.Diagnostics.Debug.WriteLine($"[Nesti] NotificationControl.Cleanup: {NotificationId}");
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(NotificationUrl))
            OpenUrl(NotificationUrl);

        CallMarkAsRead("Manual Read");
        DismissRequested?.Invoke(this, EventArgs.Empty);
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
        {
            System.Diagnostics.Debug.WriteLine($"[Nesti] MarkAsRead: {NotificationId} action={actionTaken}");
            _ = NotificationApiService.MarkAsReadAsync(NotificationId, _mEmpId, actionTaken);
        }
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }
}
