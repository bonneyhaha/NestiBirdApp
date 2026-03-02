using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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
}
