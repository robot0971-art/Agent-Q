using System.Windows.Threading;
using AgentQ.Core.Models;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public static class DesktopToolCallbacksFactory
{
    public static DesktopToolCallbacks Create(
        MainViewModel viewModel,
        Dispatcher dispatcher,
        Func<string, string> trimForLog,
        Action<UsageStats>? onUsage = null)
    {
        return new DesktopToolCallbacks
        {
            OnRunStep = (state, title, detail) => dispatcher.Invoke(() =>
            {
                viewModel.AddRunStep(state, title, detail);
                viewModel.StatusText = title;
            }),
            OnToolExecution = toolName => dispatcher.Invoke(() =>
            {
                viewModel.StatusText = $"Tool running: {toolName}";
                viewModel.AddLog($"Tool running: {toolName}");
            }),
            OnToolOutput = (toolName, output) => dispatcher.Invoke(() =>
            {
                viewModel.AddLog($"Tool completed: {toolName} ({output.Length} chars)");
            }),
            OnToolError = (toolName, error) => dispatcher.Invoke(() =>
            {
                viewModel.StatusText = $"Tool error: {toolName}";
                viewModel.AddLog($"Tool error: {toolName} - {trimForLog(error)}");
            }),
            OnPermissionDenied = toolName => dispatcher.Invoke(() =>
            {
                viewModel.StatusText = $"Tool denied: {toolName}";
                viewModel.AddLog($"Tool permission denied: {toolName}");
            }),
            OnFileChanged = change => dispatcher.Invoke(() =>
            {
                viewModel.FileChanges.Add(change);
            }),
            OnVerificationPlan = plan => dispatcher.Invoke(() =>
            {
                viewModel.VerificationPlans.Add(plan);
            }),
            OnUsage = usage => dispatcher.Invoke(() =>
            {
                onUsage?.Invoke(usage);
            })
        };
    }
}
