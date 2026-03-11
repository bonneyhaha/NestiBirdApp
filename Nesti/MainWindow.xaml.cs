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
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW  = 0x00040000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    // ── Services ──────────────────────────────────────────────────────────────
    private IWebSocketSource? _ws;
    private readonly SoundService _sound;
    private HwndSource? _hwndSource;

    // mEmpID returned by the user-ID API — used as userSession in every API payload.
    private long _mEmpId;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly HashSet<string> _seenIds      = new();
    private readonly Queue<string>   _seenIdsOrder = new();
    private const    int             MaxSeenIds    = 500;

    // Visible stack — index 0 = newest (top of screen), last = oldest (near bird)
    private readonly List<(NotificationControl Ctrl, DispatcherTimer Timer)> _active   = new();
    // Overflow: queued when stack is full, dequeued as slots open
    private readonly Queue<NotificationMessage>                               _overflow = new();
    // Object pool: reuse card controls to avoid repeated construction / GC pressure
    private readonly Stack<NotificationControl>                               _pool     = new();
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

    // Proper async pattern: event handler delegates to an async Task method
    // so exceptions are logged rather than silently swallowed on async void.
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = Window_LoadedAsync();
    }

    private async Task Window_LoadedAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[Nesti] Window_LoadedAsync: starting");

            PositionAtBottomRight();
            InstallWndProcHook();

            BirdImage.Source = SharedGifPlayer.Get("pack://application:,,,/assets/nest_bird.gif").SharedBitmap;
            System.Diagnostics.Debug.WriteLine("[Nesti] Bird GIF loaded");

            var sysName = UserHelper.GetSystemUsername();
            System.Diagnostics.Debug.WriteLine($"[Nesti] System username: {sysName}");

            if (AppConfig.UseRealWebSocket)
            {
                System.Diagnostics.Debug.WriteLine("[Nesti] Real WebSocket mode");
                var realWs = new WebSocketService();
                _ws = realWs;
                _ws.NotificationReceived += OnNotificationReceived;
                _ws.StatusChanged        += OnStatusChanged;

                System.Diagnostics.Debug.WriteLine("[Nesti] Resolving WebSocket URL...");
                var wsUrl = await realWs.ResolveUrlAsync(sysName);
                _mEmpId = realWs.MemberId;
                System.Diagnostics.Debug.WriteLine($"[Nesti] mEmpId={_mEmpId}, wsUrl={wsUrl}");

                await _ws.ConnectAsync(wsUrl);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Nesti] Dummy WebSocket mode");
                _ws = new DummyWebSocketService();
                _ws.NotificationReceived += OnNotificationReceived;
                _ws.StatusChanged        += OnStatusChanged;

                await _ws.ConnectAsync(string.Empty);
            }

            System.Diagnostics.Debug.WriteLine("[Nesti] Window_LoadedAsync: complete");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nesti] Window_LoadedAsync ERROR: {ex}");
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

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
    }

    // ── Selective click-through via WM_NCHITTEST ──────────────────────────────
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
                           ref bool handled)
    {
        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        handled = true;

        if (_birdHidden) return (IntPtr)HTTRANSPARENT;

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
            var tl = el.PointToScreen(new Point(0, 0));
            var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            return new Rect(tl, br).Contains(screen);
        }
        catch { return false; }
    }

    // ── Bird hover ────────────────────────────────────────────────────────────
    private readonly DispatcherTimer _hideControlsTimer;

    private void BirdContainer_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hideControlsTimer.Stop();
        ControlButtons.Visibility = Visibility.Visible;
    }

    private void BirdContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
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
        BirdContainer.Visibility      = Visibility.Collapsed;
        NotificationsPanel.Visibility = Visibility.Collapsed;
        // Stop the GIF timer while the bird is invisible — saves CPU and
        // avoids redundant WriteableBitmap updates every ~100 ms.
        SharedGifPlayer.Get("pack://application:,,,/assets/nest_bird.gif").Pause();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[Nesti] Window_Closing: starting cleanup");

        // Stop hover timer
        _hideControlsTimer.Stop();

        // Unsubscribe WebSocket events before disposing to prevent late callbacks
        if (_ws is not null)
        {
            _ws.NotificationReceived -= OnNotificationReceived;
            _ws.StatusChanged        -= OnStatusChanged;
            System.Diagnostics.Debug.WriteLine("[Nesti] WebSocket events unsubscribed");
        }

        // Stop and clear all notification timers, clean up each card
        foreach (var (ctrl, timer) in _active)
        {
            timer.Stop();
            ctrl.Cleanup();
        }
        _active.Clear();
        _overflow.Clear();
        _pool.Clear();
        NotificationsPanel.Children.Clear();
        System.Diagnostics.Debug.WriteLine("[Nesti] Notification timers cleared");

        // Remove WndProc hook
        _hwndSource?.RemoveHook(WndProc);
        System.Diagnostics.Debug.WriteLine("[Nesti] WndProc hook removed");

        // Release bird bitmap reference
        BirdImage.Source = null;

        // Dispose services
        _sound.Dispose();
        _ws?.Dispose();

        System.Diagnostics.Debug.WriteLine("[Nesti] Window_Closing: complete");
    }

    // ── Notification handling ─────────────────────────────────────────────────
    private void OnNotificationReceived(object? sender, NotificationMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            if (!TryAddSeen(msg.DedupeKey)) return;

            System.Diagnostics.Debug.WriteLine($"[Nesti] Notification received: {msg.Title} (id={msg.InstanceId})");

            if (_birdHidden)
            {
                _birdHidden = false;
                BirdContainer.Visibility      = Visibility.Visible;
                NotificationsPanel.Visibility = Visibility.Visible;
                SharedGifPlayer.Get("pack://application:,,,/assets/nest_bird.gif").Resume();
            }

            // Stack full → queue for later; slot opens when any card is dismissed.
            // Cap at 50 to prevent unbounded memory growth during server bursts.
            if (_active.Count >= AppConfig.MaxNotifications)
            {
                if (_overflow.Count < 50)
                {
                    _overflow.Enqueue(msg);
                    System.Diagnostics.Debug.WriteLine($"[Nesti] Queued (overflow={_overflow.Count}): {msg.Title}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Nesti] Overflow full (50), dropping: {msg.Title}");
                }
                return;
            }

            AddNotification(msg);

            if (AppConfig.SoundEnabled)
                _sound.Play();
        });
    }

    private void AddNotification(NotificationMessage msg)
    {
        var ctrl = AcquireCard(msg);

        // Insert at index 0 so the newest card appears at the TOP of the stack.
        // With VerticalAlignment=Bottom on the panel, existing cards keep their
        // screen positions — the panel simply grows upward.
        NotificationsPanel.Children.Insert(0, ctrl);
        AdjustWindowHeight();

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AppConfig.NotificationDurationMs)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (AppConfig.UseRealWebSocket && msg.UserId != -1 &&
                !string.IsNullOrEmpty(msg.InstanceId))
            {
                System.Diagnostics.Debug.WriteLine($"[Nesti] Auto-dismiss MarkAsRead: {msg.InstanceId}");
                _ = NotificationApiService.MarkAsReadAsync(msg.InstanceId, _mEmpId, "Automatic Read");
            }

            RemoveNotification(ctrl);
        };
        timer.Start();

        _active.Add((ctrl, timer));
    }

    // ── Object pool helpers ───────────────────────────────────────────────────

    private NotificationControl AcquireCard(NotificationMessage msg)
    {
        var ctrl = _pool.Count > 0 ? _pool.Pop() : new NotificationControl();
        ctrl.SetData(msg.Id ?? msg.DedupeKey, msg.Title, msg.Body, msg.Url,
                     msg.UserId, _mEmpId);
        ctrl.DismissRequested += (_, _) => RemoveNotification(ctrl);
        return ctrl;
    }

    private void ReleaseCard(NotificationControl ctrl)
    {
        ctrl.Cleanup(); // clears animations, text, and DismissRequested
        if (_pool.Count < AppConfig.MaxNotifications)
            _pool.Push(ctrl);
    }

    private void RemoveNotification(NotificationControl ctrl, bool animate = true)
    {
        var idx = _active.FindIndex(x => x.Ctrl == ctrl);
        if (idx < 0) return;

        _active[idx].Timer.Stop();
        _active.RemoveAt(idx);

        // Snapshot screen-Y of all cards that sit ABOVE the removed card in the
        // StackPanel (lower index = newer = higher on screen). When the card
        // disappears the panel shrinks and those cards shift downward; we animate
        // them back from their old positions so the shift looks smooth.
        var panelIdx = NotificationsPanel.Children.IndexOf(ctrl);
        var toReposition = new List<(NotificationControl Card, double OldY)>(panelIdx);
        for (int i = 0; i < panelIdx; i++)
        {
            if (NotificationsPanel.Children[i] is NotificationControl nc)
            {
                try { toReposition.Add((nc, nc.PointToScreen(new Point(0, 0)).Y)); }
                catch { /* element not yet rendered */ }
            }
        }

        void FinishRemoval()
        {
            ReleaseCard(ctrl);
            NotificationsPanel.Children.Remove(ctrl);

            // AdjustWindowHeight calls UpdateLayout — reuse that single pass
            // to compute new screen positions for the reposition animation.
            AdjustWindowHeight();

            foreach (var (card, oldY) in toReposition)
            {
                try
                {
                    double newY  = card.PointToScreen(new Point(0, 0)).Y;
                    double delta = oldY - newY; // negative → card moved down
                    if (Math.Abs(delta) > 0.5)
                        card.AnimateRepositionY(delta);
                }
                catch { }
            }

            DequeueNext();
        }

        if (animate)
            ctrl.AnimateOut(FinishRemoval);
        else
            FinishRemoval();
    }

    /// <summary>
    /// Pops the next queued notification into the visible stack if a slot is free.
    /// </summary>
    private void DequeueNext()
    {
        if (_overflow.Count > 0 && _active.Count < AppConfig.MaxNotifications)
        {
            var next = _overflow.Dequeue();
            System.Diagnostics.Debug.WriteLine($"[Nesti] Dequeued (remaining={_overflow.Count}): {next.Title}");
            AddNotification(next);
        }
    }

    /// <summary>
    /// Resizes the window to match the current notification stack height,
    /// keeping the bottom-right corner pinned to the work area.
    /// </summary>
    private void AdjustWindowHeight()
    {
        NotificationsPanel.UpdateLayout();
        double stackH = NotificationsPanel.ActualHeight;
        // 178 = bird zone (158) + bottom margin (5) + gap above bird (10) + small pad (5)
        double newH = Math.Max(175.0, 178.0 + stackH);
        if (Math.Abs(Height - newH) < 0.5) return;

        double oldBottom = Top + Height;
        Height = newH;
        Top    = oldBottom - Height;
    }

    // ── WebSocket status ──────────────────────────────────────────────────────
    private void OnStatusChanged(object? sender, string status)
    {
        System.Diagnostics.Debug.WriteLine($"[Nesti WS] Status: {status}");
    }

    // ── Utility ───────────────────────────────────────────────────────────────
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
