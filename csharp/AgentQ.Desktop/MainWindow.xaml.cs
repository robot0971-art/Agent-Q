using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AgentQ.Core.Providers;
using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;
using Microsoft.Win32;

namespace AgentQ.Desktop;

public partial class MainWindow : Window
{
    private static readonly string[] SupportedAttachmentExtensions =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif",
        ".mp4", ".mov", ".avi", ".mkv", ".webm"
    ];

    private readonly MainViewModel _viewModel = new();
    private readonly DesktopConfigService _configService = new();
    private readonly DesktopAgentService _agentService = new();
    private readonly List<DesktopAttachment> _attachments = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += MainWindow_OnLoaded;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var saved = await _configService.LoadAsync();
        if (saved != null)
        {
            _viewModel.ApplyConfiguration(saved);
            ApiKeyBox.Password = saved.ApiKey;
            _viewModel.StatusText = $"설정을 불러왔습니다: {_configService.ConfigPath}";
        }
        else
        {
            _viewModel.ApplyConfiguration(new ProviderConfiguration
            {
                Provider = "opencode-go",
                Model = "kimi-k2.6",
                BaseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
                TimeoutSeconds = 0,
                MaxTokens = 4096
            });
            _viewModel.StatusText = "첫 실행입니다. API key를 입력하고 설정을 저장하세요.";
        }

        _viewModel.AddLog("AgentQ Desktop 시작");
    }

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.ApiKey = ApiKeyBox.Password;
    }

    private async void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _configService.SaveAsync(_viewModel.ToConfiguration());
            _viewModel.StatusText = "설정을 저장했습니다.";
            _viewModel.AddLog("설정 저장 완료");
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"설정 저장 실패: {ex.Message}";
            _viewModel.AddLog($"설정 저장 실패: {ex.Message}");
        }
    }

    private void BrowseWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "프로젝트 폴더를 선택하세요",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(_viewModel.WorkspaceRoot)
                ? Environment.CurrentDirectory
                : _viewModel.WorkspaceRoot
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _viewModel.WorkspaceRoot = dialog.SelectedPath;
            Environment.SetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT", dialog.SelectedPath);
            _viewModel.AddLog($"작업 폴더 선택: {dialog.SelectedPath}");
        }
    }

    private void AttachFiles_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "이미지 또는 동영상 선택",
            Multiselect = true,
            Filter = "이미지/동영상|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.mp4;*.mov;*.avi;*.mkv;*.webm|모든 파일|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!SupportedAttachmentExtensions.Contains(extension))
            {
                _viewModel.AddLog($"지원하지 않는 첨부 형식: {Path.GetFileName(path)}");
                continue;
            }

            if (_attachments.Any(attachment => string.Equals(attachment.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _attachments.Add(new DesktopAttachment
            {
                Path = path,
                FileName = Path.GetFileName(path),
                MediaType = GetMediaType(extension)
            });
            _viewModel.Attachments.Add(Path.GetFileName(path));
        }

        _viewModel.StatusText = _attachments.Count == 0
            ? "첨부된 파일이 없습니다."
            : $"첨부 파일 {_attachments.Count}개가 선택되었습니다. 이미지는 그대로 전송되고, 동영상은 ffmpeg가 있으면 대표 프레임으로 분석됩니다.";
    }

    private void ClearAttachments_OnClick(object sender, RoutedEventArgs e)
    {
        _attachments.Clear();
        _viewModel.Attachments.Clear();
        _viewModel.StatusText = "첨부 파일을 지웠습니다.";
    }

    private void InputBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        _ = SendCurrentMessageAsync();
    }

    private async void Send_OnClick(object sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async Task SendCurrentMessageAsync()
    {
        var prompt = _viewModel.InputText.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || _viewModel.IsBusy)
        {
            return;
        }

        _viewModel.InputText = string.Empty;
        var attachmentsForRequest = _attachments.ToList();
        var messageAttachments = attachmentsForRequest.Select(ToAttachmentViewModel).ToList();
        _viewModel.Messages.Add(new ChatMessageViewModel
        {
            Role = "사용자",
            Content = prompt,
            Attachments = messageAttachments
        });
        var assistantMessage = new ChatMessageViewModel { Role = "AgentQ", Content = string.Empty };
        _viewModel.Messages.Add(assistantMessage);
        var assistantIndex = _viewModel.Messages.Count - 1;
        _viewModel.IsBusy = true;
        _viewModel.StatusText = "응답을 생성하는 중입니다.";
        _viewModel.AddLog("모델 호출 시작");

        try
        {
            Environment.SetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT", _viewModel.WorkspaceRoot);
            using var cts = CreateTimeout(_viewModel.TimeoutSeconds);
            var fullText = await _agentService.SendAsync(
                _viewModel.ToConfiguration(),
                prompt,
                attachmentsForRequest,
                delta =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (assistantIndex >= 0 && assistantIndex < _viewModel.Messages.Count)
                        {
                            _viewModel.Messages[assistantIndex] = new ChatMessageViewModel
                            {
                                Role = assistantMessage.Role,
                                Content = _viewModel.Messages[assistantIndex].Content + delta
                            };
                        }
                    });
                },
                cts?.Token ?? CancellationToken.None);

            if (string.IsNullOrWhiteSpace(fullText) &&
                assistantIndex >= 0 &&
                assistantIndex < _viewModel.Messages.Count)
            {
                _viewModel.Messages[assistantIndex] = new ChatMessageViewModel
                {
                    Role = "AgentQ",
                    Content = "(빈 응답)"
                };
            }

            _viewModel.StatusText = "응답 완료";
            _viewModel.AddLog("모델 호출 완료");
            if (_attachments.Count > 0)
            {
                _attachments.Clear();
                _viewModel.Attachments.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            _viewModel.StatusText = "요청이 취소되었거나 시간 초과되었습니다.";
            _viewModel.AddLog("요청 취소 또는 시간 초과");
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"오류: {ex.Message}";
            _viewModel.AddLog($"오류: {ex.Message}");
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private static ChatAttachmentViewModel ToAttachmentViewModel(DesktopAttachment attachment)
    {
        return new ChatAttachmentViewModel
        {
            FileName = attachment.FileName,
            Kind = attachment.IsImage ? "이미지" : "동영상",
            Path = attachment.Path
        };
    }

    private void ClearConversation_OnClick(object sender, RoutedEventArgs e)
    {
        _agentService.ClearConversation();
        _viewModel.Messages.Clear();
        _viewModel.AddLog("대화 초기화");
        _viewModel.StatusText = "대화를 초기화했습니다.";
    }

    private static CancellationTokenSource? CreateTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? null
            : new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static string GetMediaType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
    }
}
