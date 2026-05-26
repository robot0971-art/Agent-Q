using System.Windows;
using AgentQ.Desktop.ViewModels;
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
        ApplyStartupWorkspace(_serviceProvider.GetRequiredService<MainViewModel>(), e.Args);
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();
    }

    private static void ApplyStartupWorkspace(MainViewModel viewModel, IReadOnlyList<string> args)
    {
        var workspace = args.FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg))
            ?? Environment.GetEnvironmentVariable("AGENTQ_DESKTOP_WORKSPACE");
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return;
        }

        viewModel.WorkspaceRoot = workspace;
    }

    private async void App_OnExit(object sender, ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }
}
