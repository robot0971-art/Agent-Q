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
                viewModel.SelectedFileChange = change;
            }),
            OnVerificationPlan = plan => dispatcher.Invoke(() =>
            {
                viewModel.VerificationPlans.Add(plan);
            }),
            OnVerificationResult = result => dispatcher.Invoke(() =>
            {
                viewModel.AddVerificationResult(result);
            }),
            OnUsage = usage => dispatcher.Invoke(() =>
            {
                onUsage?.Invoke(usage);
            }),
            OnRequestExtendSteps = currentLimit => dispatcher.Invoke(() =>
            {
                var result = System.Windows.MessageBox.Show(
                    $"AgentQ가 최대 실행 단계({currentLimit}단계)에 거의 도달했습니다.\n" +
                    "작업을 완료하기 위해 단계를 30단계 더 연장하시겠습니까?\n\n" +
                    "아니오(No)를 누르면 에이전트 루프가 중단됩니다.",
                    "실행 단계 한도 경고",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                return result == System.Windows.MessageBoxResult.Yes;
            })
        };
    }
}
