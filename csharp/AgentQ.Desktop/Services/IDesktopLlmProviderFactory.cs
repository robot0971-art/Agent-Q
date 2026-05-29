using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public interface IDesktopLlmProviderFactory
{
    ILlmProvider CreateProvider(ProviderConfiguration config);
}
