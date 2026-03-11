using System.IO;
using System.Windows.Media;

namespace Nesti.Services;

/// <summary>
/// Plays MP3 notification sounds using WPF's built-in MediaPlayer.
/// Must be created and used on an STA thread (the WPF UI thread).
/// Implements IDisposable — call Dispose() on window close.
/// </summary>
public sealed class SoundService : IDisposable
{
    private readonly string _soundPath;
    private MediaPlayer?    _player;
    private bool            _disposed;

    public SoundService(string soundPath) => _soundPath = soundPath;

    /// <summary>Plays the notification sound. Silently swallows errors.</summary>
    public void Play()
    {
        if (_disposed) return;
        try
        {
            if (!File.Exists(_soundPath)) return;

            // If a sound is already playing, skip rather than creating another
            // WMF COM graph.  Avoids 100 MediaPlayer allocations in burst scenarios.
            if (_player is not null) return;

            _player = new MediaPlayer();
            _player.MediaEnded += OnMediaEnded;   // auto-close when playback finishes
            _player.Open(new Uri(_soundPath, UriKind.Absolute));
            _player.Play();
        }
        catch { /* never crash the app over a sound */ }
    }

    public void Stop() => _player?.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposePlayer();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void OnMediaEnded(object? sender, EventArgs e) => DisposePlayer();

    private void DisposePlayer()
    {
        if (_player is null) return;
        _player.MediaEnded -= OnMediaEnded;
        _player.Stop();
        _player.Close();   // releases the WMF COM graph immediately
        _player = null;
    }
}
