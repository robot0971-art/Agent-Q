using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace AgentQ.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddAgentQDesktop();

        _serviceProvider = services.BuildServiceProvider();
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();
    }

    private async void App_OnExit(object sender, ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }
}
