using System.Windows;

namespace Nesti;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // AppConfig's static constructor loads .env automatically on first access.
        // Nothing else needed here — MainWindow is launched via StartupUri.
        base.OnStartup(e);
    }
}
