using System.Buffers;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Nesti.Helpers;
using Nesti.Models;

namespace Nesti.Services;

/// <summary>
/// Manages a persistent WebSocket connection with automatic exponential-backoff reconnection.
/// Reconnection never gives up — after the max-attempt cap it retries every 60 s indefinitely.
/// All events are raised on the thread-pool; marshal to the UI thread as needed.
/// </summary>
public sealed class WebSocketService : IWebSocketSource
{
    // ── Events ────────────────────────────────────────────────────────────────
    public event EventHandler<NotificationMessage>? NotificationReceived;

    /// <summary>Fired with one of: "connecting", "connected", "reconnecting".</summary>
    public event EventHandler<string>? StatusChanged;

    // ── Static ping payload — allocated once, reused every heartbeat tick ────
    // Eliminates JsonSerializer + Encoding.UTF8.GetBytes allocation every 25 s.
    private static readonly byte[] PingPayload =
        Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");

    // ── State ─────────────────────────────────────────────────────────────────
    private ClientWebSocket?         _ws;
    private CancellationTokenSource  _cts     = new();
    private string                   _url     = string.Empty;
    private int                      _attempt;

    // ── Heartbeat ─────────────────────────────────────────────────────────────
    private Timer?          _heartbeatTimer;

    /// <summary>
    /// Set to true when a pong is received.  Checked by WatchForPongAsync after
    /// the timeout delay to decide whether to abort the socket.
    /// Using volatile avoids a separate CancellationTokenSource per heartbeat
    /// and eliminates the race where a fast pong (2-20 ms) cancels the token
    /// before Register() is called, which would fire the abort callback
    /// synchronously even though the pong arrived in time.
    /// </summary>
    private volatile bool _pongReceived;

    // ── Shutdown guard ────────────────────────────────────────────────────────
    private bool _isShuttingDown;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    /// <summary>
    /// The mEmpID returned by the API during URL resolution (numeric employee ID).
    /// Used as the userSession field in all notification action API payloads.
    /// Zero if API_BASE_URL is not configured or the call failed.
    /// </summary>
    public long MemberId { get; private set; }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the WebSocket URL:
    ///   1. Reads the Windows login name (Environment.UserName → e.g. "jdoe").
    ///   2. Builds CorpID as "Corp\{username}"  →  "Corp\jdoe".
    ///   3. Calls: GET {API_BASE_URL}?CorpID=Corp\{username}
    ///   4. Reads "mEmpID" from the JSON response.
    ///   5. Returns {WS_URL}/{mEmpID}  so each user connects to their own channel.
    ///   6. Falls back to WS_URL as-is if API_BASE_URL is empty or the call fails.
    /// </summary>
    public async Task<string> ResolveUrlAsync(string username)
    {
        var apiBase = AppConfig.ApiBaseUrl;
        var wsBase  = AppConfig.WsUrl.TrimEnd('/');

        if (!string.IsNullOrEmpty(apiBase))
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var corpId = $"Corp\\{username}";
                var apiUrl = $"{apiBase}?CorpID={Uri.EscapeDataString(corpId)}";
                var raw    = await client.GetStringAsync(apiUrl);

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("mEmpID", out var prop))
                {
                    long mEmpId = 0;
                    if (prop.ValueKind == JsonValueKind.Number)
                        prop.TryGetInt64(out mEmpId);
                    else if (prop.ValueKind == JsonValueKind.String)
                        long.TryParse(prop.GetString(), out mEmpId);

                    if (mEmpId != 0)
                    {
                        MemberId = mEmpId;
                        System.Diagnostics.Debug.WriteLine($"[Nesti WS] mEmpID resolved: {mEmpId}");
                        return $"{wsBase}/{mEmpId}";
                    }
                }
            }
            catch { /* fall through to default URL */ }
        }

        return wsBase;
    }

    /// <summary>Starts the connection loop. Call once.</summary>
    public async Task ConnectAsync(string url)
    {
        _url     = url;
        _attempt = 0;
        await TryConnectAsync();
    }

    public void Dispose()
    {
        _isShuttingDown = true;
        System.Diagnostics.Debug.WriteLine("[Nesti WS] Disposing WebSocketService");
        StopHeartbeat();
        _cts.Cancel();
        _ws?.Dispose();
        System.Diagnostics.Debug.WriteLine("[Nesti WS] WebSocketService disposed");
    }

    // ── Internal connection logic ─────────────────────────────────────────────

    private async Task TryConnectAsync()
    {
        StopHeartbeat();

        // Cancel any previous listen loop and create fresh socket
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _ws?.Dispose();
        _ws = new ClientWebSocket();

        if (_isShuttingDown) return;
        Raise(_attempt == 0 ? "connecting" : "reconnecting");
        System.Diagnostics.Debug.WriteLine($"[Nesti WS] TryConnectAsync attempt={_attempt} url={_url}");

        try
        {
            // 15-second connection timeout linked with the main cancellation token
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

            await _ws.ConnectAsync(new Uri(_url), linkedCts.Token);
            _attempt = 0;
            System.Diagnostics.Debug.WriteLine("[Nesti WS] Connected successfully");
            Raise("connected");
            StartHeartbeat();
            _ = ListenAsync(_cts.Token);   // fire-and-forget
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nesti WS] Connect failed: {ex.Message}");
            await ScheduleReconnectAsync();
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        // Rent from the shared pool so repeated reconnects don't allocate a new
        // 8 KB array each time — the same buffer is reused across reconnect cycles.
        const int BufSize = 8_192;
        var buffer    = ArrayPool<byte>.Shared.Rent(BufSize);
        var sb        = new StringBuilder();
        bool reconnect = false;

        try
        {
            while (_ws!.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer, 0, BufSize), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        System.Diagnostics.Debug.WriteLine("[Nesti WS] Server sent Close frame — will reconnect");
                        reconnect = true;
                        return;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                ParseAndRaise(sb.ToString());
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nesti WS] ListenAsync error: {ex.Message}");
            reconnect = true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);   // always returned, even on exception
        }

        if (reconnect && !ct.IsCancellationRequested && !_isShuttingDown)
            await ScheduleReconnectAsync();
    }

    private void ParseAndRaise(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            // Handle pong — set flag so WatchForPongAsync doesn't abort the socket.
            // Using a volatile flag instead of CancellationTokenSource.Register avoids
            // the race where a fast pong (2-20 ms) cancels the token before Register()
            // is called, which would invoke the abort callback synchronously.
            if (doc.RootElement.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "pong")
            {
                _pongReceived = true;
                System.Diagnostics.Debug.WriteLine("[Nesti WS] Pong received");
                return;
            }

            var msg = JsonSerializer.Deserialize<NotificationMessage>(json);
            if (msg is not null)
                NotificationReceived?.Invoke(this, msg);
        }
        catch { /* ignore malformed JSON */ }
    }

    /// <summary>
    /// Exponential back-off reconnect — never gives up.
    /// After WsReconnectMaxAttempts the delay caps at 60 s and retries indefinitely.
    /// </summary>
    private async Task ScheduleReconnectAsync()
    {
        _attempt++;

        // Cap so the delay calculation saturates at 60 s, but never stop trying.
        int cappedAttempt = Math.Min(_attempt, AppConfig.WsReconnectMaxAttempts);

        var delay = Math.Min(
            AppConfig.WsReconnectBaseMs * (int)Math.Pow(2, cappedAttempt - 1),
            60_000);
        delay += (int)(delay * (Random.Shared.NextDouble() * 0.2 - 0.1));

        Raise("reconnecting");
        System.Diagnostics.Debug.WriteLine($"[Nesti WS] Reconnect attempt={_attempt} (delay={delay}ms)");

        try   { await Task.Delay(delay, _cts.Token); }
        catch (OperationCanceledException) { return; }

        await TryConnectAsync();
    }

    // ── Heartbeat ─────────────────────────────────────────────────────────────

    private void StartHeartbeat()
    {
        var interval = AppConfig.WsHeartbeatIntervalMs;
        _heartbeatTimer = new Timer(OnHeartbeatTick, null, interval, interval);
        System.Diagnostics.Debug.WriteLine($"[Nesti WS] Heartbeat started (interval={interval}ms)");
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        // WatchForPongAsync tasks use _cts.Token, so cancelling _cts (done in
        // TryConnectAsync and Dispose) already terminates any pending watcher.
    }

    private async void OnHeartbeatTick(object? state)
    {
        if (_ws?.State != WebSocketState.Open || _isShuttingDown) return;
        try
        {
            // Reset flag before sending so any arriving pong in the new window
            // is correctly attributed to this heartbeat cycle.
            _pongReceived = false;

            // Static payload — no allocation per tick.
            await _ws.SendAsync(
                new ArraySegment<byte>(PingPayload),
                WebSocketMessageType.Text,
                true,
                _cts.Token);

            // Spawn a lightweight watcher that aborts the socket after the pong
            // timeout only if _pongReceived is still false.
            _ = WatchForPongAsync();

            System.Diagnostics.Debug.WriteLine("[Nesti WS] Ping sent");
        }
        catch { }
    }

    /// <summary>
    /// Waits WsPongTimeoutMs.  If _pongReceived is still false after the wait,
    /// the connection is considered dead and the socket is aborted so ListenAsync
    /// triggers a reconnect.
    ///
    /// Uses the service-level _cts.Token so it is automatically cancelled (and
    /// exits cleanly) when the service is disposed or reconnecting — no separate
    /// CancellationTokenSource is allocated per heartbeat cycle.
    /// </summary>
    private async Task WatchForPongAsync()
    {
        try
        {
            await Task.Delay(AppConfig.WsPongTimeoutMs, _cts.Token);

            // Check flag only after the full timeout — avoids the race where
            // a fast pong sets _pongReceived=true in the window between the
            // delay completing and this check.
            if (!_pongReceived && _ws?.State == WebSocketState.Open && !_isShuttingDown)
            {
                System.Diagnostics.Debug.WriteLine("[Nesti WS] Pong timeout — aborting for reconnect");
                _ws.Abort();   // triggers ListenAsync exception → ScheduleReconnectAsync
            }
        }
        catch (OperationCanceledException) { /* service disposed or reconnecting — normal */ }
    }

    private void Raise(string status) =>
        StatusChanged?.Invoke(this, status);
}
