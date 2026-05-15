namespace AgentQ.Desktop.Services;

public sealed class GitCommandResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public bool Succeeded => ExitCode == 0;

    public string DisplayOutput
    {
        get
        {
            var output = StandardOutput.Trim();
            var error = StandardError.Trim();

            if (Succeeded)
            {
                return string.IsNullOrWhiteSpace(output) ? "No changes." : output;
            }

            return string.IsNullOrWhiteSpace(error)
                ? $"git exited with code {ExitCode}."
                : error;
        }
    }
}
