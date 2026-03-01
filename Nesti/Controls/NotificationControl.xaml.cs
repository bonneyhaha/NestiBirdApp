using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nesti.Services;

namespace Nesti.Controls;

public partial class NotificationControl : UserControl
{
    // ── Public surface ────────────────────────────────────────────────────────
    public string? NotificationId  { get; private set; }
    public string? NotificationUrl { get; private set; }

    /// <summary>Raised when the user clicks the dismiss (×) button.</summary>
    public event EventHandler? DismissRequested;

    /// <summary>Raised when the user clicks the snooze button.</summary>
    public event EventHandler? SnoozeRequested;

    // ── Constructor ───────────────────────────────────────────────────────────
    public NotificationControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void SetData(string notificationId, string title, string body, string? url)
    {
        NotificationId    = notificationId;
        TitleBlock.Text   = title;
        MessageBlock.Text = body;
        NotificationUrl   = url;
    }

    // ── Animations ────────────────────────────────────────────────────────────
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var dur  = new Duration(TimeSpan.FromMilliseconds(1000));
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

        var slide  = new DoubleAnimation(400, 0, dur) { EasingFunction = ease };
        var scaleX = new DoubleAnimation(0.9, 1, dur) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.9, 1, dur) { EasingFunction = ease };

        SlideTransform.BeginAnimation(TranslateTransform.XProperty, slide);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)));
        BeginAnimation(OpacityProperty, fade);
    }

    public void AnimateOut(Action? onComplete = null)
    {
        var dur  = new Duration(TimeSpan.FromMilliseconds(500));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var slide  = new DoubleAnimation(0, 400, dur) { EasingFunction = ease };
        var scaleX = new DoubleAnimation(1, 0.9, dur);
        var scaleY = new DoubleAnimation(1, 0.9, dur);
        var fade   = new DoubleAnimation(1, 0, dur);
        fade.Completed += (_, _) => onComplete?.Invoke();

        SlideTransform.BeginAnimation(TranslateTransform.XProperty, slide);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        BeginAnimation(OpacityProperty, fade);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    // Card body click — opens URL, marks as read, then animates the card out.
    // SnoozeBtn/DismissBtn capture the mouse on MouseDown and set e.Handled=true,
    // so this handler is never reached when an action button is clicked.
    private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(NotificationUrl))
            OpenUrl(NotificationUrl);

        if (!string.IsNullOrEmpty(NotificationId))
            _ = NotificationApiService.MarkAsReadAsync(NotificationId); // fire-and-forget

        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    // Snooze — MouseDown stops bubbling to Card_Click and captures the mouse so
    // MouseUp is guaranteed to fire on this element even if the cursor drifts.
    private void Snooze_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        (sender as System.Windows.UIElement)?.CaptureMouse();
    }

    // Snooze — MouseUp calls the snooze API (fire-and-forget) and raises the event.
    private void Snooze_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        (sender as System.Windows.UIElement)?.ReleaseMouseCapture();

        if (!string.IsNullOrEmpty(NotificationId))
            _ = NotificationApiService.SnoozeAsync(NotificationId);   // fire-and-forget

        SnoozeRequested?.Invoke(this, EventArgs.Empty);
    }

    // Dismiss — MouseDown stops bubbling and captures the mouse.
    private void Dismiss_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        (sender as System.Windows.UIElement)?.CaptureMouse();
    }

    // Dismiss — MouseUp marks as read (fire-and-forget) and raises the event.
    private void Dismiss_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        (sender as System.Windows.UIElement)?.ReleaseMouseCapture();

        if (!string.IsNullOrEmpty(NotificationId))
            _ = NotificationApiService.MarkAsReadAsync(NotificationId); // fire-and-forget

        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
