using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Nesti.Controls;
using Nesti.Helpers;
using Nesti.Models;
using Nesti.Services;

namespace Nesti;

public partial class MainWindow : Window
{
    // ── Win32 constants + P/Invoke ────────────────────────────────────────────
    private const int WM_NCHITTEST     = 0x0084;
    private const int HTTRANSPARENT    = -1;
    private const int HTCLIENT         =  1;
    private const int GWL_EXSTYLE      = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;  // exclude from Alt+Tab / taskbar
    private const int WS_EX_APPWINDOW  = 0x00040000;  // force taskbar presence (we clear this)

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    // ── Services ──────────────────────────────────────────────────────────────
    private IWebSocketSource? _ws;                          // set in Window_Loaded
    private readonly SoundService _sound;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly HashSet<string> _seenIds      = new();
    private readonly Queue<string>   _seenIdsOrder = new();   // insertion-order shadow for eviction
    private const    int             MaxSeenIds    = 500;     // cap — prevents unbounded growth

    private readonly List<(NotificationControl Ctrl, DispatcherTimer Timer)> _active = new();
    private bool _birdHidden;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();

        _sound = new SoundService(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "bird_chirp.mp3"));

        _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _hideControlsTimer.Tick += (_, _) =>
        {
            _hideControlsTimer.Stop();
            ControlButtons.Visibility = Visibility.Collapsed;
        };
    }

    // ── Startup ───────────────────────────────────────────────────────────────
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtBottomRight();
        InstallWndProcHook();

        // Point the bird at the shared WriteableBitmap — no event handler needed.
        BirdImage.Source = SharedGifPlayer.Get("pack://application:,,,/assets/nest_bird.gif").SharedBitmap;

        var sysName = UserHelper.GetSystemUsername();

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
        var hwnd = new WindowInteropHelper(this).Handle;

        // Reliably hide from Alt+Tab and the taskbar on Windows 10/11.
        // ShowInTaskbar="False" alone is not sufficient for WindowStyle="None" windows.
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

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

        foreach (UIElement child in NotificationsPanel.Children)
            if (child is FrameworkElement fe && HitTest(fe, screen)) return true;

        return false;
    }

    private static bool HitTest(FrameworkElement el, Point screen)
    {
        try
        {
            // Use PointToScreen for BOTH corners so the rect is in physical pixels,
            // matching the WM_NCHITTEST lParam. Using new Size(ActualWidth, ActualHeight)
            // is wrong on DPI > 100% because ActualWidth is in DIPs, causing the right
            // portion of the element (where the action buttons live) to be excluded.
            var tl = el.PointToScreen(new Point(0, 0));
            var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            return new Rect(tl, br).Contains(screen);
        }
        catch { return false; }
    }

    // ── Bird hover ────────────────────────────────────────────────────────────
    // Created once and reused — avoids allocating a new DispatcherTimer on every hover-out.
    private readonly DispatcherTimer _hideControlsTimer;

    private void BirdContainer_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hideControlsTimer.Stop();
        ControlButtons.Visibility = Visibility.Visible;
    }

    private void BirdContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Restart the timer each time the cursor leaves. Buttons collapse after 1.2 s
        // of inactivity so the user has time to click them without rushing.
        _hideControlsTimer.Stop();
        _hideControlsTimer.Start();
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

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hideControlsTimer.Stop();
        foreach (var (_, timer) in _active)
            timer.Stop();
        _sound.Dispose();
        _ws?.Dispose();
    }

    // ── Notification handling ─────────────────────────────────────────────────
    private void OnNotificationReceived(object? sender, NotificationMessage msg)
    {
        // Always dispatch to the UI thread
        Dispatcher.Invoke(() =>
        {
            // Deduplicate (capped — oldest IDs evicted once the set reaches MaxSeenIds)
            if (!TryAddSeen(msg.DedupeKey)) return;

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
            ctrl.AnimateOut(() => { ctrl.Cleanup(); NotificationsPanel.Children.Remove(ctrl); });
        else
        {
            ctrl.Cleanup();
            NotificationsPanel.Children.Remove(ctrl);
        }
    }

    // ── WebSocket status ──────────────────────────────────────────────────────
    private void OnStatusChanged(object? sender, string status)
    {
        // Log to debug output; extend here for a UI status indicator if needed
        System.Diagnostics.Debug.WriteLine($"[Nesti WS] {status}");
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    // Returns false if the key was already seen (duplicate). Otherwise records it,
    // evicting the oldest entry when the cap is reached so the set never grows unbounded.
    private bool TryAddSeen(string key)
    {
        if (_seenIds.Contains(key)) return false;

        if (_seenIds.Count >= MaxSeenIds)
        {
            var oldest = _seenIdsOrder.Dequeue();
            _seenIds.Remove(oldest);
        }

        _seenIds.Add(key);
        _seenIdsOrder.Enqueue(key);
        return true;
    }

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
