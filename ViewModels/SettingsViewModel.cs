using System.Collections.ObjectModel;
using System.Windows.Input;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Core.Models.Settings;
using QuantumZ.Infrastructure.Services;

namespace QuantumZ.UI.ViewModels;

public class SettingsViewModel(ISettingsService settings, IAIClient aiClient, IDialogService dialogService, WhisperModelDownloader whisperDownloader, IDebugLogger debugLogger) : BaseViewModel
{
    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IAIClient _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
    private readonly IDialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    private readonly WhisperModelDownloader _whisperDownloader = whisperDownloader ?? throw new ArgumentNullException(nameof(whisperDownloader));
    private readonly IDebugLogger _debugLogger = debugLogger ?? throw new ArgumentNullException(nameof(debugLogger));
    private CancellationTokenSource? _modelRefreshDebounce;
    private bool _suppressModelRefresh;

    private bool _showAdvancedNetworkSettings;
    public bool ShowAdvancedNetworkSettings
    {
        get => _showAdvancedNetworkSettings;
        set => SetProperty(ref _showAdvancedNetworkSettings, value);
    }

    private string _vadUrl = string.Empty;
    public string VadUrl
    {
        get => _vadUrl;
        set => SetProperty(ref _vadUrl, value);
    }

    private string _llmUrl = string.Empty;
    public string LlmUrl
    {
        get => _llmUrl;
        set
        {
            if (EqualityComparer<string>.Default.Equals(_llmUrl, value)) return;

            SetProperty(ref _llmUrl, value);

            if (!_suppressModelRefresh)
            {
                _settings.LlmUrl = (value ?? "").Trim();
                ScheduleModelRefresh();
            }
        }
    }

    private string _sttUrl = string.Empty;
    public string SttUrl
    {
        get => _sttUrl;
        set => SetProperty(ref _sttUrl, value);
    }

    private string _ttsUrl = string.Empty;
    public string TtsUrl
    {
        get => _ttsUrl;
        set => SetProperty(ref _ttsUrl, value);
    }

    private string _llamaModelId = string.Empty;
    public string LlamaModelId
    {
        get => _llamaModelId;
        set
        {
            if (EqualityComparer<string>.Default.Equals(_llamaModelId, value)) return;

            SetProperty(ref _llamaModelId, value);

            if (!_suppressModelRefresh)
            {
                var selected = (value ?? "").Trim();
                _settings.LlamaModelId = selected;
                if (!string.IsNullOrWhiteSpace(selected))
                    _settings.SelectedModelName = selected;
            }
        }
    }

    private string _selectedModelName = string.Empty;
    public string SelectedModelName
    {
        get => _selectedModelName;
        set
        {
            if (EqualityComparer<string>.Default.Equals(_selectedModelName, value)) return;

            SetProperty(ref _selectedModelName, value);

            var selected = (value ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _settings.SelectedModelName = selected;
                LlamaModelId = selected;

                if (!_suppressModelRefresh)
                {
                    StatusMessage = $"Selected LLM model: {selected}";
                }
            }
        }
    }

    private string _sttModelId = string.Empty;
    public string SttModelId
    {
        get => _sttModelId;
        set => SetProperty(ref _sttModelId, value);
    }

    private string _ttsModelId = string.Empty;
    public string TtsModelId
    {
        get => _ttsModelId;
        set => SetProperty(ref _ttsModelId, value);
    }

    private string _wakeWordText = string.Empty;
    public string WakeWordText
    {
        get => _wakeWordText;
        set => SetProperty(ref _wakeWordText, value);
    }

    private string _selectedAudioRouting = nameof(AudioRoutingPreference.Default);
    public string SelectedAudioRouting
    {
        get => _selectedAudioRouting;
        set => SetProperty(ref _selectedAudioRouting, value);
    }

    private string _loggingIntervalMinutes = "15";
    public string LoggingIntervalMinutes
    {
        get => _loggingIntervalMinutes;
        set => SetProperty(ref _loggingIntervalMinutes, value);
    }

    private string _summarizationTriggers = string.Empty;
    public string SummarizationTriggers
    {
        get => _summarizationTriggers;
        set => SetProperty(ref _summarizationTriggers, value);
    }

    private bool _enableActivityLogging = true;
    public bool EnableActivityLogging
    {
        get => _enableActivityLogging;
        set => SetProperty(ref _enableActivityLogging, value);
    }

    private bool _enableActivityAnalysis;
    public bool EnableActivityAnalysis
    {
        get => _enableActivityAnalysis;
        set => SetProperty(ref _enableActivityAnalysis, value);
    }

    private bool _useOnDeviceStt;
    public bool UseOnDeviceStt
    {
        get => _useOnDeviceStt;
        set => SetProperty(ref _useOnDeviceStt, value);
    }

    private string _whisperModelPath = string.Empty;
    public string WhisperModelPath
    {
        get => _whisperModelPath;
        set => SetProperty(ref _whisperModelPath, value);
    }

    private string _obsidianVaultPath = string.Empty;
    public string ObsidianVaultPath
    {
        get => _obsidianVaultPath;
        set => SetProperty(ref _obsidianVaultPath, value);
    }

    private string _obsidianMcpPath = string.Empty;
    public string ObsidianMcpPath
    {
        get => _obsidianMcpPath;
        set => SetProperty(ref _obsidianMcpPath, value);
    }

    private double _whisperDownloadProgress;
    public double WhisperDownloadProgress
    {
        get => _whisperDownloadProgress;
        set => SetProperty(ref _whisperDownloadProgress, value);
    }

    private bool _isWhisperDownloading;
    public bool IsWhisperDownloading
    {
        get => _isWhisperDownloading;
        set => SetProperty(ref _isWhisperDownloading, value);
    }

    private bool _isDiscoveringModels;
    public bool IsDiscoveringModels
    {
        get => _isDiscoveringModels;
        set => SetProperty(ref _isDiscoveringModels, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private string _mcpNameInput = string.Empty;
    public string McpNameInput
    {
        get => _mcpNameInput;
        set => SetProperty(ref _mcpNameInput, value);
    }

    private string _mcpEndpointInput = string.Empty;
    public string McpEndpointInput
    {
        get => _mcpEndpointInput;
        set => SetProperty(ref _mcpEndpointInput, value);
    }

    private string _mcpApiKeyInput = string.Empty;
    public string McpApiKeyInput
    {
        get => _mcpApiKeyInput;
        set => SetProperty(ref _mcpApiKeyInput, value);
    }

    public ObservableCollection<string> AudioRoutingOptions { get; } =
    [
        nameof(AudioRoutingPreference.Default),
        nameof(AudioRoutingPreference.AlwaysSpeaker),
        nameof(AudioRoutingPreference.AlwaysHeadset),
        nameof(AudioRoutingPreference.Dynamic)
    ];

    public ObservableCollection<string> AvailableModels { get; } = [];

    public ObservableCollection<McpServerConfig> McpServers { get; } = [];

    private ICommand? _saveSettingsCommand;
    public ICommand SaveSettingsCommand => _saveSettingsCommand ??= new AsyncRelayCommand(SaveSettingsAsync);

    private ICommand? _addMcpServerCommand;
    public ICommand AddMcpServerCommand => _addMcpServerCommand ??= new RelayCommand(AddMcpServer);

    private ICommand? _removeMcpServerCommand;
    public ICommand RemoveMcpServerCommand => _removeMcpServerCommand ??= new RelayCommand<McpServerConfig>(RemoveMcpServer);

    private ICommand? _downloadWhisperCommand;
    public ICommand DownloadWhisperCommand => _downloadWhisperCommand ??= new AsyncRelayCommand(DownloadWhisperAsync);

    private ICommand? _addObsidianMcpCommand;
    public ICommand AddObsidianMcpCommand => _addObsidianMcpCommand ??= new RelayCommand(AddObsidianMcpServer);

    private ICommand? _refreshModelsCommand;
    public ICommand RefreshModelsCommand => _refreshModelsCommand ??= new AsyncRelayCommand(RefreshModelsAsync);

    private void ScheduleModelRefresh()
    {
        _modelRefreshDebounce?.Cancel();
        _modelRefreshDebounce?.Dispose();
        _modelRefreshDebounce = new CancellationTokenSource();

        var token = _modelRefreshDebounce.Token;
        _ = RefreshModelsAfterDelayAsync(token);
    }

    private async Task RefreshModelsAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);

            if (cancellationToken.IsCancellationRequested || !IsValidAbsoluteUrl(LlmUrl)) return;

            await RefreshModelsAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected when the user keeps typing.
        }
    }

    private async Task RefreshModelsAsync()
    {
        if (IsDiscoveringModels) return;

        var llmUrl = LlmUrl.Trim();
        if (string.IsNullOrWhiteSpace(llmUrl))
        {
            StatusMessage = "Enter an LLM endpoint URL before refreshing models.";
            return;
        }

        if (!IsValidAbsoluteUrl(llmUrl))
        {
            StatusMessage = "Enter a valid absolute LLM endpoint URL before refreshing models.";
            return;
        }

        _modelRefreshDebounce?.Cancel();
        _settings.LlmUrl = llmUrl;
        IsDiscoveringModels = true;
        StatusMessage = "Discovering LLM models...";

        try
        {
            var models = await _aiClient.GetAvailableModelsAsync();

            AvailableModels.Clear();
            foreach (var model in models.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AvailableModels.Add(model);
            }

            var preferredModel = FirstNonEmpty(SelectedModelName, _settings.SelectedModelName, LlamaModelId, _settings.LlamaModelId);
            if (!string.IsNullOrWhiteSpace(preferredModel) && !AvailableModels.Contains(preferredModel, StringComparer.OrdinalIgnoreCase))
            {
                AvailableModels.Insert(0, preferredModel);
            }

            if (AvailableModels.Count == 0)
            {
                StatusMessage = "No models were returned by the configured LLM endpoint.";
                return;
            }

            SelectedModelName = !string.IsNullOrWhiteSpace(preferredModel)
                ? AvailableModels.First(m => string.Equals(m, preferredModel, StringComparison.OrdinalIgnoreCase))
                : AvailableModels[0];

            StatusMessage = $"Discovered {AvailableModels.Count} LLM model(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Model discovery failed: {ex.Message}";
        }
        finally
        {
            IsDiscoveringModels = false;
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(LlmUrl) || string.IsNullOrWhiteSpace(SttUrl) || string.IsNullOrWhiteSpace(TtsUrl))
            {
                await _dialogService.ShowAlertAsync("Validation Error", "All server endpoints must be configured.");
                return;
            }

            if (!Uri.TryCreate(LlmUrl, UriKind.Absolute, out _) ||
                !Uri.TryCreate(SttUrl, UriKind.Absolute, out _) ||
                !Uri.TryCreate(TtsUrl, UriKind.Absolute, out _))
            {
                await _dialogService.ShowAlertAsync("Validation Error", "One or more server endpoints have an invalid URL.");
                return;
            }

            _settings.ShowAdvancedNetworkSettings = ShowAdvancedNetworkSettings;
            _settings.VadUrl = (VadUrl ?? "").Trim();
            _settings.LlmUrl = (LlmUrl ?? "").Trim();
            _settings.SttUrl = (SttUrl ?? "").Trim();
            _settings.TtsUrl = (TtsUrl ?? "").Trim();
            var selectedLlmModel = FirstNonEmpty(SelectedModelName, LlamaModelId);
            _settings.LlamaModelId = selectedLlmModel;
            _settings.SelectedModelName = selectedLlmModel;
            LlamaModelId = selectedLlmModel;
            SelectedModelName = selectedLlmModel;
            _settings.SttModelId = (SttModelId ?? "").Trim();
            _settings.TtsModelId = (TtsModelId ?? "").Trim();
            _settings.WakeWords = [(WakeWordText ?? "").Trim()];

            if (Enum.TryParse<AudioRoutingPreference>(SelectedAudioRouting, out var routing))
            {
                _settings.AudioRouting = routing;
            }

            if (int.TryParse(LoggingIntervalMinutes, out var minutes) && minutes > 0)
            {
                _settings.LoggingInterval = TimeSpan.FromMinutes(minutes);
            }
            else
            {
                await _dialogService.ShowAlertAsync("Validation Error", "Logging interval must be a positive number.");
                return;
            }

            _settings.SummarizationTriggers = SplitList(SummarizationTriggers);
            _settings.EnableActivityLogging = EnableActivityLogging;
            _settings.EnableActivityAnalysis = EnableActivityAnalysis;
            _settings.UseOnDeviceStt = UseOnDeviceStt;
            _settings.WhisperModelPath = (WhisperModelPath ?? "").Trim();
            _settings.ObsidianVaultPath = (ObsidianVaultPath ?? "").Trim();
            _settings.McpServers = [.. McpServers];

            // Update the Obsidian MCP server path if one exists
            var obsidianMcp = _settings.McpServers.FirstOrDefault(s => s.Name == "obsidian");
            if (obsidianMcp != null)
            {
                var updatedArgs = new List<string>(obsidianMcp.Args);
                if (updatedArgs.Count > 0)
                {
                    updatedArgs[^1] = (ObsidianMcpPath ?? "").Trim();
                }
                else
                {
                    updatedArgs.Add((ObsidianMcpPath ?? "").Trim());
                }
                var updated = obsidianMcp with { Args = updatedArgs };
                var list = _settings.McpServers.ToList();
                var idx = list.FindIndex(s => s.Name == "obsidian");
                if (idx >= 0) list[idx] = updated;
                _settings.McpServers = list;
            }

            StatusMessage = "Configuration saved successfully.";
            _debugLogger.Log("SettingsViewModel", $"Persisted settings: LlmUrl='{_settings.LlmUrl}', SelectedModelName='{_settings.SelectedModelName}', LlamaModelId='{_settings.LlamaModelId}', UseOnDeviceStt='{_settings.UseOnDeviceStt}', WhisperModelPath='{_settings.WhisperModelPath}'.", LogLevel.Info);
            await _dialogService.ShowAlertAsync("Saved", "Configuration saved successfully.");

            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            await _dialogService.ShowAlertAsync("Save Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddMcpServer()
    {
        if (string.IsNullOrWhiteSpace(McpNameInput) || string.IsNullOrWhiteSpace(McpEndpointInput))
        {
            StatusMessage = "MCP server name and endpoint are required.";
            return;
        }

        if (!Uri.TryCreate(McpEndpointInput, UriKind.Absolute, out _))
        {
            StatusMessage = "Invalid MCP endpoint URL.";
            return;
        }

        var server = new McpServerConfig(
            McpNameInput.Trim(),
            McpEndpointInput.Trim(),
            string.IsNullOrWhiteSpace(McpApiKeyInput) ? null : McpApiKeyInput.Trim()
        );

        McpServers.Add(server);
        McpNameInput = string.Empty;
        McpEndpointInput = string.Empty;
        McpApiKeyInput = string.Empty;
        OnPropertyChanged(nameof(McpNameInput));
        OnPropertyChanged(nameof(McpEndpointInput));
        OnPropertyChanged(nameof(McpApiKeyInput));
        StatusMessage = "MCP server added. Remember to save.";
    }

    private void RemoveMcpServer(McpServerConfig? server)
    {
        if (server is null) return;
        McpServers.Remove(server);
        StatusMessage = "MCP server removed. Remember to save.";
    }

    private async Task DownloadWhisperAsync()
    {
        if (IsWhisperDownloading) return;
        IsWhisperDownloading = true;
        StatusMessage = "Downloading Whisper model...";
        try
        {
            var progress = new Progress<double>(p =>
            {
                WhisperDownloadProgress = p;
                StatusMessage = $"Downloading... {p:P0}";
            });
            var success = await _whisperDownloader.EnsureModelAsync(progress);
            if (success)
            {
                WhisperModelPath = _whisperDownloader.GetEffectiveModelPath();
                _settings.WhisperModelPath = WhisperModelPath;
                StatusMessage = "Whisper model ready.";
                await _dialogService.ShowAlertAsync("Download Complete", "Whisper model downloaded successfully.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
            await _dialogService.ShowAlertAsync("Download Failed", ex.Message);
        }
        finally
        {
            IsWhisperDownloading = false;
            WhisperDownloadProgress = 0;
        }
    }

    private void AddObsidianMcpServer()
    {
        var path = ObsidianMcpPath?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Obsidian vault path is required for the MCP server.";
            return;
        }

        var existing = McpServers.FirstOrDefault(s => s.Name == "obsidian");
        if (existing != null)
        {
            McpServers.Remove(existing);
        }

        var server = new McpServerConfig(
            Name: "obsidian",
            Endpoint: "stdio",
            Transport: McpTransportType.Stdio,
            Command: "npx",
            Args: ["-y", "@bitbonsai/mcpvault@latest", path],
            Env: new Dictionary<string, string>
            {
                ["MCP_MODE"] = "stdio",
                ["DISABLE_CONSOLE_OUTPUT"] = "true"
            },
            Disabled: false,
            AlwaysAllow: [
                "search_notes",
                "read_note",
                "patch_note",
                "write_note",
                "list_directory",
                "move_note",
                "read_multiple_notes",
                "move_file",
                "update_frontmatter",
                "get_notes_info",
                "get_frontmatter",
                "manage_tags",
                "get_vault_stats",
                "list_all_tags"
            ]
        );

        McpServers.Add(server);
        StatusMessage = "Obsidian MCP server added with full permissions. Remember to save.";
    }

    public void Load()
    {
        try
        {
            _suppressModelRefresh = true;

            ShowAdvancedNetworkSettings = _settings.ShowAdvancedNetworkSettings;
            VadUrl = _settings.VadUrl;
            LlmUrl = _settings.LlmUrl;
            SttUrl = _settings.SttUrl;
            TtsUrl = _settings.TtsUrl;
            LlamaModelId = _settings.LlamaModelId;
            SelectedModelName = FirstNonEmpty(_settings.SelectedModelName, _settings.LlamaModelId);
            AvailableModels.Clear();
            if (!string.IsNullOrWhiteSpace(SelectedModelName))
            {
                AvailableModels.Add(SelectedModelName);
            }
            SttModelId = _settings.SttModelId;
            TtsModelId = _settings.TtsModelId;
            WakeWordText = _settings.WakeWords?.FirstOrDefault() ?? "hey quantum";
            SelectedAudioRouting = _settings.AudioRouting.ToString();
            LoggingIntervalMinutes = _settings.LoggingInterval.TotalMinutes.ToString("0");
            SummarizationTriggers = _settings.SummarizationTriggers != null ? string.Join(", ", _settings.SummarizationTriggers) : string.Empty;
            EnableActivityLogging = _settings.EnableActivityLogging;
            EnableActivityAnalysis = _settings.EnableActivityAnalysis;
            UseOnDeviceStt = _settings.UseOnDeviceStt;
            WhisperModelPath = _settings.WhisperModelPath;
            ObsidianVaultPath = _settings.ObsidianVaultPath;

            McpServers.Clear();
            foreach (var server in _settings.McpServers)
            {
                McpServers.Add(server);
            }

            // Derive Obsidian MCP path from existing stdio config
            var obsidianMcp = _settings.McpServers.FirstOrDefault(s => s.Name == "obsidian" && s.Transport == McpTransportType.Stdio);
            if (obsidianMcp?.Args?.Count > 0)
            {
                ObsidianMcpPath = obsidianMcp.Args[^1];
            }
            else
            {
                ObsidianMcpPath = _settings.ObsidianVaultPath;
            }

            _debugLogger.Log("SettingsViewModel", $"Loaded settings: LlmUrl='{LlmUrl}', SelectedModelName='{SelectedModelName}', LlamaModelId='{LlamaModelId}', UseOnDeviceStt='{UseOnDeviceStt}', WhisperModelPath='{WhisperModelPath}'.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsViewModel.Load failed: {ex}");
            StatusMessage = "Failed to load settings.";
        }
        finally
        {
            _suppressModelRefresh = false;
        }
    }

    private static List<string> SplitList(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static bool IsValidAbsoluteUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

public class RelayCommand<T>(Action<T?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute((T?)parameter);
}
