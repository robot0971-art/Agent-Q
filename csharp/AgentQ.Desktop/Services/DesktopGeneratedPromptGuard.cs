using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

internal static class DesktopGeneratedPromptGuard
{
    public static bool TryReplaceInput(
        MainViewModel viewModel,
        string prompt,
        string source)
    {
        if (!string.IsNullOrWhiteSpace(viewModel.InputText) &&
            !string.Equals(viewModel.InputText.Trim(), prompt.Trim(), StringComparison.Ordinal))
        {
            viewModel.StatusText = $"Send or clear the current draft before using {source}";
            viewModel.AddLog($"{source} blocked because the input box contains a user draft.");
            return false;
        }

        viewModel.InputText = prompt;
        return true;
    }
}
