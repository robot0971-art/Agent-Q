using System.IO;
using System.Text;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;
using AgentQ.Providers.OpenAi;

namespace AgentQ.Desktop.Services;

public sealed class DesktopAgentService
{
    private const string SystemPrompt =
        """
        You are AgentQ Desktop, a Windows desktop coding assistant.
        Answer in Korean by default unless the user asks for another language.
        Assume the user is working on Windows. Prefer safe, concise guidance.
        Tool execution is not enabled in this first desktop MVP, so explain what you would inspect or change when tools are needed.
        """;

    private readonly List<ChatMessage> _messages = [];
    private readonly LinkContentFetcher _linkContentFetcher = new();
    private readonly WorkspaceIndexer _workspaceIndexer = new();

    public async Task<string> SendAsync(
        ProviderConfiguration config,
        string userText,
        IReadOnlyList<DesktopAttachment>? attachments = null,
        Action<string>? onDelta = null,
        CancellationToken ct = default)
    {
        var provider = CreateProvider(config);
        var enrichedUserText = await BuildPromptWithContextAsync(config, userText, ct);
        _messages.Add(await CreateUserMessageAsync(enrichedUserText, attachments ?? [], ct));

        var context = new ChatContext
        {
            Model = config.Model,
            SystemPrompt = SystemPrompt,
            Messages = _messages.ToList(),
            MaxTokens = config.MaxTokens == 0 ? 4096 : config.MaxTokens,
            Stream = true
        };

        var builder = new StringBuilder();
        await foreach (var chunk in provider.GenerateStreamAsync(context, Array.Empty<ToolDefinition>(), ct))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                builder.Append(chunk.TextDelta);
                onDelta?.Invoke(chunk.TextDelta);
            }
        }

        var assistantText = builder.ToString();
        _messages.Add(ChatMessage.AssistantText(assistantText));
        return assistantText;
    }

    public void ClearConversation()
    {
        _messages.Clear();
    }

    private async Task<string> BuildPromptWithContextAsync(ProviderConfiguration config, string userText, CancellationToken ct)
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT") ?? Environment.CurrentDirectory;
        var workspaceContext = await _workspaceIndexer.BuildContextAsync(workspaceRoot, ct);
        var linkedContext = await _linkContentFetcher.BuildContextAsync(userText, ct);

        if (string.IsNullOrWhiteSpace(workspaceContext) && string.IsNullOrWhiteSpace(linkedContext))
        {
            return userText;
        }

        var builder = new StringBuilder();
        builder.AppendLine(userText);
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine("The desktop app attached local context before sending this message.");
        builder.AppendLine("Use the workspace snapshot for repository questions, but say when a file may be missing from the snapshot.");

        if (!string.IsNullOrWhiteSpace(workspaceContext))
        {
            builder.AppendLine();
            builder.AppendLine(workspaceContext);
        }

        if (!string.IsNullOrWhiteSpace(linkedContext))
        {
            builder.AppendLine();
            builder.AppendLine(linkedContext);
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task<ChatMessage> CreateUserMessageAsync(
        string userText,
        IReadOnlyList<DesktopAttachment> attachments,
        CancellationToken ct)
    {
        var content = new List<ChatContent> { ChatContent.CreateText(userText) };
        var videoNotes = new List<string>();

        foreach (var attachment in attachments)
        {
            if (attachment.IsImage)
            {
                var bytes = await File.ReadAllBytesAsync(attachment.Path, ct);
                content.Add(ChatContent.CreateImage(attachment.MediaType, Convert.ToBase64String(bytes)));
            }
            else if (attachment.IsVideo)
            {
                var result = await VideoFrameExtractor.ExtractFramesAsync(attachment.Path, ct);
                if (!result.IsAvailable)
                {
                    videoNotes.Add($"{attachment.FileName}: ffmpeg를 찾지 못해 프레임을 추출하지 못했습니다.");
                    continue;
                }

                if (result.FramePaths.Count == 0)
                {
                    videoNotes.Add($"{attachment.FileName}: 분석할 프레임을 추출하지 못했습니다.");
                    continue;
                }

                videoNotes.Add($"{attachment.FileName}: 동영상에서 대표 프레임 {result.FramePaths.Count}장을 추출해 이미지로 분석합니다.");
                try
                {
                    foreach (var framePath in result.FramePaths)
                    {
                        var bytes = await File.ReadAllBytesAsync(framePath, ct);
                        content.Add(ChatContent.CreateImage("image/jpeg", Convert.ToBase64String(bytes)));
                    }
                }
                finally
                {
                    TryDeleteFrameDirectory(result.FramePaths);
                }
            }
        }

        if (videoNotes.Count > 0)
        {
            content.Add(ChatContent.CreateText(string.Join(Environment.NewLine, videoNotes)));
        }

        return new ChatMessage
        {
            Role = ChatRole.User,
            Content = content
        };
    }

    private static void TryDeleteFrameDirectory(IReadOnlyList<string> framePaths)
    {
        var firstFrame = framePaths.FirstOrDefault();
        if (firstFrame == null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(firstFrame);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Temporary frame cleanup is best-effort.
        }
    }

    private static ILlmProvider CreateProvider(ProviderConfiguration config)
    {
        ILlmProvider provider = config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiCompatibleProvider(config.BaseUrl, config.ApiKey),
            "opencode-go" => new OpenAiCompatibleProvider(config.BaseUrl, config.ApiKey, name: "opencode-go"),
            "anthropic" => new AnthropicProvider(config.BaseUrl, config.ApiKey),
            _ => new OpenAiCompatibleProvider(config.BaseUrl, config.ApiKey, name: config.Provider)
        };

        return new ResilientLlmProvider(provider);
    }
}
