using Nesti.Models;

namespace Nesti.Services;

/// <summary>
/// Common contract for both the real WebSocket connection and the dummy
/// test service. Switch between them with USE_REAL_WEBSOCKET in .env.
/// </summary>
public interface IWebSocketSource : IDisposable
{
    event EventHandler<NotificationMessage>? NotificationReceived;

    /// <summary>Fired with one of: "connecting", "connected", "reconnecting", "failed",
    /// or a custom status string (e.g. "connected (dummy mode)").</summary>
    event EventHandler<string>? StatusChanged;

    Task ConnectAsync(string url);
}
