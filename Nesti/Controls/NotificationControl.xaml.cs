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
        NotificationId  = notificationId;
        TitleBlock.Text   = title;
        MessageBlock.Text = body;
        NotificationUrl   = url;
    }

    // ── Animations ────────────────────────────────────────────────────────────
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Slide in from the right
        var slide = new DoubleAnimation(400, 0, new Duration(TimeSpan.FromMilliseconds(350)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, slide);

        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(250)));
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Animates the card out to the right, then calls <paramref name="onComplete"/>.</summary>
    public void AnimateOut(Action? onComplete = null)
    {
        var slide = new DoubleAnimation(0, 400, new Duration(TimeSpan.FromMilliseconds(300)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(300)));
        fade.Completed += (_, _) => onComplete?.Invoke();

        SlideTransform.BeginAnimation(TranslateTransform.XProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }

    // ── Event handlers ────────────────────────────────────────────────────────
    private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(NotificationUrl))
            OpenUrl(NotificationUrl);
    }

    private async void Snooze_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrEmpty(NotificationId))
            await NotificationApiService.SnoozeAsync(NotificationId);

        SnoozeRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrEmpty(NotificationId))
            await NotificationApiService.MarkAsReadAsync(NotificationId);

        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SnoozeBtn.Opacity  = 0.75;
        DismissBtn.Opacity = 0.75;
    }

    private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SnoozeBtn.Opacity  = 0;
        DismissBtn.Opacity = 0;
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
