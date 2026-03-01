using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nesti;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Force software (CPU) rendering — disables DirectX/GPU for all WPF elements.
        // Eliminates GPU memory usage from DropShadowEffects and hardware composition.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // AppConfig's static constructor loads .env automatically on first access.
        // Nothing else needed here — MainWindow is launched via StartupUri.
        base.OnStartup(e);
    }
}
