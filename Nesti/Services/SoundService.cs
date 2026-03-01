using System.IO;
using System.Windows.Media;

namespace Nesti.Services;

/// <summary>
/// Plays MP3 notification sounds using WPF's built-in MediaPlayer.
/// Must be created and used on an STA thread (the WPF UI thread).
/// </summary>
public sealed class SoundService
{
    private readonly string _soundPath;
    private MediaPlayer?    _player;

    public SoundService(string soundPath)
    {
        _soundPath = soundPath;
    }

    /// <summary>Plays the notification sound. Silently swallows errors.</summary>
    public void Play()
    {
        try
        {
            if (!File.Exists(_soundPath)) return;

            // Each Play() creates a fresh player to allow overlapping calls
            // and avoid "already opened" state issues.
            _player?.Close();
            _player = new MediaPlayer();
            _player.Open(new Uri(_soundPath, UriKind.Absolute));
            _player.Play();
        }
        catch { /* never crash the app over a sound */ }
    }

    public void Stop() => _player?.Stop();
}
