using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nesti.Controls;
using Nesti.Helpers;
using Nesti.Models;
using Nesti.Services;

namespace Nesti;

public partial class MainWindow : Window
{
    // ── Win32 hit-test constants ──────────────────────────────────────────────
    private const int WM_NCHITTEST  = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT      =  1;

    // ── Services ──────────────────────────────────────────────────────────────
    private IWebSocketSource? _ws;                          // set in Window_Loaded
    private readonly SoundService _sound;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly HashSet<string> _seenIds = new();
    private readonly List<(NotificationControl Ctrl, DispatcherTimer Timer)> _active = new();
    private bool _birdHidden;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();

        _sound = new SoundService(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "bird_chirp.mp3"));
    }

    // ── Startup ───────────────────────────────────────────────────────────────
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtBottomRight();
        InstallWndProcHook();

        // Resolve display name in the background
        var sysName   = UserHelper.GetSystemUsername();
        var fullName  = await UserHelper.GetFullNameAsync(sysName) ?? sysName;
        var firstName = UserHelper.GetFirstName(fullName);

        GreetingTextBlock.Text = $"{UserHelper.GetGreeting()}, {firstName}!";

        // Pick real WebSocket or dummy test service based on .env flag
        if (AppConfig.UseRealWebSocket)
        {
            var realWs = new WebSocketService();
            _ws = realWs;
            _ws.NotificationReceived += OnNotificationReceived;
            _ws.StatusChanged        += OnStatusChanged;

            var wsUrl = await realWs.ResolveUrlAsync(sysName);
            await _ws.ConnectAsync(wsUrl);
        }
        else
        {
            _ws = new DummyWebSocketService();
            _ws.NotificationReceived += OnNotificationReceived;
            _ws.StatusChanged        += OnStatusChanged;

            await _ws.ConnectAsync(string.Empty); // url unused in dummy mode
        }
    }

    private void PositionAtBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width  - 5;
        Top  = area.Bottom - Height - 5;
    }

    private void InstallWndProcHook()
    {
        var hwnd   = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    // ── Selective click-through via WM_NCHITTEST ──────────────────────────────
    // Empty transparent areas return HTTRANSPARENT so clicks fall through to the
    // desktop. Only the bird and live notification cards are HTCLIENT (clickable).
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
                           ref bool handled)
    {
        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        handled = true;

        // When the bird is hidden, the whole window is transparent
        if (_birdHidden) return (IntPtr)HTTRANSPARENT;

        // Decode screen coordinates from lParam
        int sx = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        int sy = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var screenPt = new Point(sx, sy);

        return IsInteractive(screenPt) ? (IntPtr)HTCLIENT : (IntPtr)HTTRANSPARENT;
    }

    private bool IsInteractive(Point screen)
    {
        if (HitTest(BirdContainer, screen)) return true;

        // GreetingBubble extends left of BirdContainer — check explicitly when visible
        if (GreetingBubble.Visibility == Visibility.Visible && HitTest(GreetingBubble, screen))
            return true;

        foreach (UIElement child in NotificationsPanel.Children)
            if (child is FrameworkElement fe && HitTest(fe, screen)) return true;

        return false;
    }

    private static bool HitTest(FrameworkElement el, Point screen)
    {
        try
        {
            var tl   = el.PointToScreen(new Point(0, 0));
            var rect = new Rect(tl, new Size(el.ActualWidth, el.ActualHeight));
            return rect.Contains(screen);
        }
        catch { return false; }
    }

    // ── Bird hover ────────────────────────────────────────────────────────────
    private void BirdContainer_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ControlButtons.Visibility = Visibility.Visible;
        ShowGreeting();
    }

    private void BirdContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ControlButtons.Visibility = Visibility.Collapsed;
        HideGreeting();
    }

    private void ShowGreeting()
    {
        GreetingBubble.Visibility = Visibility.Visible;
        GreetingBubble.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200))));
    }

    private void HideGreeting()
    {
        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(200)));
        fade.Completed += (_, _) => GreetingBubble.Visibility = Visibility.Collapsed;
        GreetingBubble.BeginAnimation(OpacityProperty, fade);
    }

    // ── Bird click ────────────────────────────────────────────────────────────
    private void Bird_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var url = AppConfig.BirdDefaultUrl;
        if (!string.IsNullOrEmpty(url))
            OpenUrl(url);
    }

    // ── Window controls ───────────────────────────────────────────────────────
    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        _birdHidden = true;
        BirdContainer.Visibility    = Visibility.Collapsed;
        NotificationsPanel.Visibility = Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) =>
        _ws?.Dispose();

    // ── Notification handling ─────────────────────────────────────────────────
    private void OnNotificationReceived(object? sender, NotificationMessage msg)
    {
        // Always dispatch to the UI thread
        Dispatcher.Invoke(() =>
        {
            // Deduplicate
            if (!_seenIds.Add(msg.DedupeKey)) return;

            // Restore bird if the user had hidden it
            if (_birdHidden)
            {
                _birdHidden = false;
                BirdContainer.Visibility     = Visibility.Visible;
                NotificationsPanel.Visibility = Visibility.Visible;
            }

            // Evict oldest if at the limit
            while (_active.Count >= AppConfig.MaxNotifications)
                RemoveNotification(_active[0].Ctrl, animate: false);

            AddNotification(msg);

            if (AppConfig.SoundEnabled)
                _sound.Play();
        });
    }

    private void AddNotification(NotificationMessage msg)
    {
        var ctrl = new NotificationControl();
        ctrl.SetData(msg.Id ?? msg.DedupeKey, msg.Title, msg.Body, msg.Url);
        ctrl.DismissRequested += (_, _) => RemoveNotification(ctrl);
        ctrl.SnoozeRequested  += (_, _) => RemoveNotification(ctrl);

        NotificationsPanel.Children.Add(ctrl);

        // Auto-dismiss timer
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AppConfig.NotificationDurationMs)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RemoveNotification(ctrl);
        };
        timer.Start();

        _active.Add((ctrl, timer));
    }

    private void RemoveNotification(NotificationControl ctrl, bool animate = true)
    {
        var idx = _active.FindIndex(x => x.Ctrl == ctrl);
        if (idx < 0) return;

        _active[idx].Timer.Stop();
        _active.RemoveAt(idx);

        if (animate)
            ctrl.AnimateOut(() => NotificationsPanel.Children.Remove(ctrl));
        else
            NotificationsPanel.Children.Remove(ctrl);
    }

    // ── WebSocket status ──────────────────────────────────────────────────────
    private void OnStatusChanged(object? sender, string status)
    {
        // Log to debug output; extend here for a UI status indicator if needed
        System.Diagnostics.Debug.WriteLine($"[Nesti WS] {status}");
    }

    // ── Utility ───────────────────────────────────────────────────────────────
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
