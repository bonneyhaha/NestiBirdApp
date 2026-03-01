using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Nesti.Helpers;
using Nesti.Models;

namespace Nesti.Services;

/// <summary>
/// Manages a persistent WebSocket connection with automatic exponential-backoff reconnection.
/// All events are raised on the thread-pool; marshal to the UI thread as needed.
/// </summary>
public sealed class WebSocketService : IWebSocketSource
{
    // ── Events ────────────────────────────────────────────────────────────────
    public event EventHandler<NotificationMessage>? NotificationReceived;

    /// <summary>Fired with one of: "connecting", "connected", "reconnecting", "failed".</summary>
    public event EventHandler<string>? StatusChanged;

    // ── State ─────────────────────────────────────────────────────────────────
    private ClientWebSocket?           _ws;
    private CancellationTokenSource    _cts  = new();
    private string                     _url  = string.Empty;
    private int                        _attempt;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the WebSocket URL:
    ///   1. If API_GET_WS_URL_PATH is configured, calls the API.
    ///   2. Otherwise falls back to WS_URL from .env.
    /// </summary>
    public async Task<string> ResolveUrlAsync(string username)
    {
        var path    = AppConfig.ApiGetWsUrlPath;
        var baseUrl = AppConfig.ApiBaseUrl;

        if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(baseUrl))
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var apiUrl = $"{baseUrl.TrimEnd('/')}{path}?username={Uri.EscapeDataString(username)}";
                var json   = await client.GetStringAsync(apiUrl);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("wsUrl", out var prop) &&
                    prop.GetString() is { Length: > 0 } resolved)
                    return resolved;
            }
            catch { /* fall through */ }
        }

        return AppConfig.WsUrl;
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
        _cts.Cancel();
        _ws?.Dispose();
    }

    // ── Internal connection logic ─────────────────────────────────────────────

    private async Task TryConnectAsync()
    {
        // Cancel any previous listen loop
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _ws?.Dispose();
        _ws = new ClientWebSocket();

        Raise(_attempt == 0 ? "connecting" : "reconnecting");

        try
        {
            await _ws.ConnectAsync(new Uri(_url), _cts.Token);
            _attempt = 0;
            Raise("connected");
            _ = ListenAsync(_cts.Token);   // fire-and-forget
        }
        catch (Exception)
        {
            await ScheduleReconnectAsync();
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var buffer = new byte[8_192];
        var sb     = new StringBuilder();

        try
        {
            while (_ws!.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                        goto disconnected;

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                ParseAndRaise(sb.ToString());
            }
        }
        catch (OperationCanceledException) { return; }
        catch { /* socket dropped */ }

        disconnected:
        if (!ct.IsCancellationRequested)
            await ScheduleReconnectAsync();
    }

    private void ParseAndRaise(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<NotificationMessage>(json);
            if (msg is not null)
                NotificationReceived?.Invoke(this, msg);
        }
        catch { /* ignore malformed JSON */ }
    }

    private async Task ScheduleReconnectAsync()
    {
        _attempt++;
        if (_attempt > AppConfig.WsReconnectMaxAttempts)
        {
            Raise("failed");
            return;
        }

        // Exponential back-off capped at 60 seconds
        var delay = Math.Min(
            AppConfig.WsReconnectBaseMs * (int)Math.Pow(2, _attempt - 1),
            60_000);

        Raise("reconnecting");

        try   { await Task.Delay(delay, _cts.Token); }
        catch (OperationCanceledException) { return; }

        await TryConnectAsync();
    }

    private void Raise(string status) =>
        StatusChanged?.Invoke(this, status);
}
