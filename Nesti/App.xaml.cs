using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Nesti.Helpers;
using Nesti.Services;

namespace Nesti;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Software rendering — MUST be before base.OnStartup ────────────────
        // Prevents WPF from creating a D3D device or any VRAM allocations.
        // All render surfaces (window back-buffer, effect targets) live in system RAM.
        // On high-DPI GPU machines this alone cuts VRAM-backed surface cost by ~80%.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[Nesti] App.OnExit: disposing static resources");

        // Dispose all cached GIF players (stops timers, releases bitmaps)
        SharedGifPlayer.ClearAll();
        System.Diagnostics.Debug.WriteLine("[Nesti] SharedGifPlayer.ClearAll complete");

        // Dispose the shared HttpClient used for API calls
        NotificationApiService.Dispose();
        System.Diagnostics.Debug.WriteLine("[Nesti] NotificationApiService disposed");

        base.OnExit(e);
        System.Diagnostics.Debug.WriteLine("[Nesti] App.OnExit: complete");
    }
}
