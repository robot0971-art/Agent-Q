namespace AgentQ.Cli;

public interface IInputFileReader
{
    Task<string> ReadAllTextAsync(string path);
}

public sealed class InputFileReader : IInputFileReader
{
    public Task<string> ReadAllTextAsync(string path)
    {
        return File.ReadAllTextAsync(path);
    }
}
