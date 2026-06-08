using System.Windows;
using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AgentQ.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private DesktopDiagnosticsService? _diagnosticsService;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddAgentQDesktop();

        _serviceProvider = services.BuildServiceProvider();
        _diagnosticsService = _serviceProvider.GetRequiredService<DesktopDiagnosticsService>();
        RegisterDiagnosticsHandlers(_diagnosticsService);
        ApplyStartupWorkspace(_serviceProvider.GetRequiredService<MainViewModel>(), e.Args);
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();
        _diagnosticsService.Record("app_started", "Main window shown.");
    }

    private void RegisterDiagnosticsHandlers(DesktopDiagnosticsService diagnosticsService)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            diagnosticsService.RecordSync(
                "app_dispatcher_unhandled_exception",
                "Unhandled WPF dispatcher exception.",
                exception: args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            diagnosticsService.RecordSync(
                "app_domain_unhandled_exception",
                $"isTerminating={args.IsTerminating}",
                exception: args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            diagnosticsService.RecordSync(
                "app_unobserved_task_exception",
                "Unobserved task exception.",
                exception: args.Exception);
        };
    }

    private void ApplyStartupWorkspace(MainViewModel viewModel, IReadOnlyList<string> args)
    {
        var workspace = args.FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg))
            ?? Environment.GetEnvironmentVariable("AGENTQ_DESKTOP_WORKSPACE");
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return;
        }

        viewModel.WorkspaceRoot = workspace;
        _diagnosticsService?.SetActiveWorkspace(workspace);
    }

    private async void App_OnExit(object sender, ExitEventArgs e)
    {
        _diagnosticsService?.RecordSync("app_exiting", $"exitCode={e.ApplicationExitCode}");
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }
}
