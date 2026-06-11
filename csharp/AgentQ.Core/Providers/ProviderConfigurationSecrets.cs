using System.Security.Cryptography;
using System.Text;

namespace AgentQ.Core.Providers;

public static class ProviderConfigurationSecrets
{
    public const string ProtectedPrefix = "dpapi:v1:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AgentQ.ProviderConfiguration.v1");

    public static ProviderConfiguration ProtectForStorage(ProviderConfiguration config)
    {
        var copy = Copy(config);
        copy.ApiKey = ProtectSecret(copy.ApiKey);
        copy.EmbeddingApiKey = ProtectSecret(copy.EmbeddingApiKey);
        return copy;
    }

    public static ProviderConfiguration UnprotectFromStorage(ProviderConfiguration config)
    {
        var copy = Copy(config);
        copy.ApiKey = UnprotectSecret(copy.ApiKey);
        copy.EmbeddingApiKey = UnprotectSecret(copy.EmbeddingApiKey);
        return copy;
    }

    public static string ProtectSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || IsProtected(secret) || !OperatingSystem.IsWindows())
        {
            return secret;
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            Entropy,
            DataProtectionScope.CurrentUser);

        return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
    }

    public static string UnprotectSecret(string secret)
    {
        if (!IsProtected(secret))
        {
            return secret;
        }

        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(secret[ProtectedPrefix.Length..]);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool IsProtected(string secret)
    {
        return secret.StartsWith(ProtectedPrefix, StringComparison.Ordinal);
    }

    private static ProviderConfiguration Copy(ProviderConfiguration config)
    {
        var copy = new ProviderConfiguration
        {
            Provider = config.Provider,
            Model = config.Model,
            BaseUrl = config.BaseUrl,
            ApiKey = config.ApiKey,
            EmbeddingProvider = config.EmbeddingProvider,
            EmbeddingModel = config.EmbeddingModel,
            EmbeddingBaseUrl = config.EmbeddingBaseUrl,
            EmbeddingApiKey = config.EmbeddingApiKey,
            TimeoutSeconds = config.TimeoutSeconds,
            MaxTokens = config.MaxTokens,
            DesktopFontSize = config.DesktopFontSize,
            DesktopAutoAttachWorkspaceContext = config.DesktopAutoAttachWorkspaceContext,
            DesktopAutoFetchLinks = config.DesktopAutoFetchLinks,
            DesktopEnableScreenshotLlmVisionReview = config.DesktopEnableScreenshotLlmVisionReview,
            DesktopWorkMode = config.DesktopWorkMode,
            DesktopMaxToolSteps = config.DesktopMaxToolSteps,
            DesktopUiLanguage = config.DesktopUiLanguage,
            Prompt = config.Prompt,
            ReadPromptFromStdin = config.ReadPromptFromStdin,
            InputFilePath = config.InputFilePath,
            JsonOutput = config.JsonOutput,
            AllowToolsWithoutPrompt = config.AllowToolsWithoutPrompt
        };

        copy.AllowedToolNames.AddRange(config.AllowedToolNames);
        copy.DeniedToolNames.AddRange(config.DeniedToolNames);

        return copy;
    }
}
