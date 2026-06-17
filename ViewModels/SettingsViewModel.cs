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

    private bool _useLocalLlm;
    public bool UseLocalLlm
    {
        get => _useLocalLlm;
        set => SetProperty(ref _useLocalLlm, value);
    }

    private bool _useLocalTts;
    public bool UseLocalTts
    {
        get => _useLocalTts;
        set => SetProperty(ref _useLocalTts, value);
    }

    private bool _showAdvancedNetworkSettings;
    public bool ShowAdvancedNetworkSettings
    {
        get => _showAdvancedNetworkSettings;
        set => SetProperty(ref _showAdvancedNetworkSettings, value);
    }

    // Provider Management Properties
    private ServiceProviderSettings _llmSettings = new (ActiveProviderName: "LLM-Primary", Providers: []);
    public ServiceProviderSettings LlmSettings
    {
        get => _llmSettings;
        set => SetProperty(ref _llmSettings, value);
    }

    private ServiceProviderSettings _vadSettings = new (ActiveProviderName: "VAD-Primary", Providers: []);
    public ServiceProviderSettings VadSettings
    {
        get => _vadSettings;
        set => SetProperty(ref _vadSettings, value);
    }

    private ServiceProviderSettings _sttSettings = new (ActiveProviderName: "STT-Primary", Providers: []);
    public ServiceProviderSettings SttSettings
    {
        get => _sttSettings;
        set => SetProperty(ref _sttSettings, value);
    }

    private ServiceProviderSettings _ttsSettings = new (ActiveProviderName: "TTS-Primary", Providers: []);
    public ServiceProviderSettings TtsSettings
    {
        get => _ttsSettings;
        set => SetProperty(ref _ttsSettings, value);
    }

    // Active Provider Helpers for UI Binding
    public ProviderConfig? SelectedLlmProvider => LlmSettings.Providers.FirstOrDefault(p => p.Name == LlmSettings.ActiveProviderName);
    public ProviderConfig? SelectedVadProvider => VadSettings.Providers.FirstOrDefault(p => p.Name == VadSettings.ActiveProviderName);
    public ProviderConfig? SelectedSttProvider => SttSettings.Providers.FirstOrDefault(p => p.Name == SttSettings.ActiveProviderName);
    public ProviderConfig? SelectedTtsProvider => TtsSettings.Providers.FirstOrDefault(p => p.Name == TtsSettings.ActiveProviderName);

    // Helper to update active provider and notify UI
    private void UpdateActiveProvider(string service, string providerName)
    {
        switch (service)
        {
            case "LLM": LlmSettings = LlmSettings with { ActiveProviderName = providerName }; break;
            case "VAD": VadSettings = VadSettings with { ActiveProviderName = providerName }; break;
            case "STT": SttSettings = SttSettings with { ActiveProviderName = providerName }; break;
            case "TTS": TtsSettings = TtsSettings with { ActiveProviderName = providerName }; break;
        }
        OnPropertyChanged(nameof(SelectedLlmProvider));
        OnPropertyChanged(nameof(SelectedVadProvider));
        OnPropertyChanged(nameof(SelectedSttProvider));
        OnPropertyChanged(nameof(SelectedTtsProvider));
    }

    // Properties for editing the currently selected provider's details (used by UI)

    public ObservableCollection<string> Services { get; } = ["LLM", "VAD", "STT", "TTS"];

    private string _selectedService = "LLM";
    public string SelectedService
    {
        get => _selectedService;
        set
        {
            if (SetProperty(ref _selectedService, value))
            {
                OnPropertyChanged(nameof(GetCurrentProvider));
                OnPropertyChanged(nameof(CurrentProviders));
                OnPropertyChanged(nameof(SelectedProviderName));
            }
        }
    }

    public List<string> CurrentProviders => GetCurrentSettings()?.Providers.Select(p => p.Name).ToList() ?? [];

    private string _selectedProviderName = string.Empty;
    public string SelectedProviderName
    {
        get => _selectedProviderName;
        set
        {
            if (SetProperty(ref _selectedProviderName, value))
            {
                UpdateActiveProvider(SelectedService, value);
            }
        }
    }

    private ServiceProviderSettings GetCurrentSettings() => SelectedService switch
    {
        "LLM" => LlmSettings,
        "VAD" => VadSettings,
        "STT" => SttSettings,
        "TTS" => TtsSettings,
        _ => null!
    };

    public string EditingUrl
    {
        get => GetCurrentProvider()?.Url ?? string.Empty;
        set
        {
            UpdateActiveProviderDetails(url: value);
        }
    }

    private string _editingModelId = string.Empty;
    public string EditingModelId
    {
        get => GetCurrentProvider()?.ModelId ?? string.Empty;
        set
        {
            UpdateActiveProviderDetails(modelId: value);
        }
    }

    private ProviderConfig? GetCurrentProvider() => SelectedService switch
    {
        "LLM" => SelectedLlmProvider,
        "VAD" => SelectedVadProvider,
        "STT" => SelectedSttProvider,
        "TTS" => SelectedTtsProvider,
        _ => null
    };

    private void UpdateActiveProviderDetails(string? url = null, string? modelId = null)
    {
        var current = GetCurrentProvider();
        if (current == null) return;

        var updated = current with
        {
            Url = url ?? current.Url,
            ModelId = modelId ?? current.ModelId
        };

        switch (SelectedService)
        {
            case "LLM": LlmSettings = UpdateProviderList(LlmSettings, updated); break;
            case "VAD": VadSettings = UpdateProviderList(VadSettings, updated); break;
            case "STT": SttSettings = UpdateProviderList(SttSettings, updated); break;
            case "TTS": TtsSettings = UpdateProviderList(TtsSettings, updated); break;
        }
    }

    private ServiceProviderSettings UpdateProviderList(ServiceProviderSettings settings, ProviderConfig updated)
    {
        var list = settings.Providers.ToList();
        var idx = list.FindIndex(p => p.Name == updated.Name);
        if (idx >= 0) list[idx] = updated; else list.Add(updated);
        return settings with { Providers = list };
    }

    private void UpdateCurrentProviderUrl(string url) { } // Obsolete, replaced by EditingUrl setter
    private void UpdateCurrentProviderModelId(string modelId) { } // Obsolete

    private double _preRollSeconds;
    public double PreRollSeconds
    {
        get => _preRollSeconds;
        set => SetProperty(ref _preRollSeconds, value);
    }

    private string _customSystemMessage = string.Empty;
    public string CustomSystemMessage
    {
        get => _customSystemMessage;
        set => SetProperty(ref _customSystemMessage, value);
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
        set
        {
            if (_useOnDeviceStt == value) return;

            SetProperty(ref _useOnDeviceStt, value);
            SelectedVoiceInputMode = value ? BuiltInAndroidVoiceMode : AiModelVoiceMode;
            VoiceInputModeDescription = GetVoiceInputModeDescription(value);
        }
    }

    private string _selectedVoiceInputMode = BuiltInAndroidVoiceMode;
    public string SelectedVoiceInputMode
    {
        get => _selectedVoiceInputMode;
        set
        {
            if (string.Equals(_selectedVoiceInputMode, value, StringComparison.Ordinal)) return;

            SetProperty(ref _selectedVoiceInputMode, value);
            var useBuiltIn = string.Equals(value, BuiltInAndroidVoiceMode, StringComparison.Ordinal);
            if (_useOnDeviceStt != useBuiltIn)
                SetProperty(ref _useOnDeviceStt, useBuiltIn, nameof(UseOnDeviceStt));
            VoiceInputModeDescription = GetVoiceInputModeDescription(useBuiltIn);
        }
    }

    private string _voiceInputModeDescription = string.Empty;
    public string VoiceInputModeDescription
    {
        get => _voiceInputModeDescription;
        set => SetProperty(ref _voiceInputModeDescription, value);
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

    private const string BuiltInAndroidVoiceMode = "Built-in Android dictation";
    private const string AiModelVoiceMode = "AI model pipeline (VAD + STT)";

    public ObservableCollection<string> VoiceInputModeOptions { get; } =
    [
        BuiltInAndroidVoiceMode,
        AiModelVoiceMode
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

            if (cancellationToken.IsCancellationRequested || !IsValidAbsoluteUrl(GetCurrentSvcUrl())) return;

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

        var llmUrl = GetCurrentSvcUrl().Trim();
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

            var preferredModel = FirstNonEmpty(SelectedProviderName, _settings.LlmSettings.ActiveProviderName);
            if (!string.IsNullOrWhiteSpace(preferredModel) && !AvailableModels.Contains(preferredModel, StringComparer.OrdinalIgnoreCase))
            {
                AvailableModels.Insert(0, preferredModel);
            }

            if (AvailableModels.Count == 0)
            {
                StatusMessage = "No models were returned by the configured LLM endpoint.";
                return;
            }

            // Model selection is now handled via ProviderConfig in the structured settings

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
            _settings.ShowAdvancedNetworkSettings = ShowAdvancedNetworkSettings;
            _settings.LlmSettings = LlmSettings;
            _settings.VadSettings = VadSettings;
            _settings.SttSettings = SttSettings;
            _settings.TtsSettings = TtsSettings;

            _settings.GlobalSettings = _settings.GlobalSettings with
            {
                UseLocalLlm = UseLocalLlm,
                UseLocalTts = UseLocalTts,
                // PreRoll and CustomSystemMessage are handled below
            };

            _settings.GlobalSettings = _settings.GlobalSettings with
            {
                PreRollSeconds = PreRollSeconds,
                CustomSystemMessage = CustomSystemMessage
            };

            // Other settings remain the same as they weren't part of provider refactor yet in this turn
            if (Enum.TryParse<AudioRoutingPreference>(SelectedAudioRouting, out var routing))
                _settings.AudioRouting = routing;

            if (int.TryParse(LoggingIntervalMinutes, out var minutes) && minutes > 0)
                _settings.LoggingInterval = TimeSpan.FromMinutes(minutes);

            _settings.SummarizationTriggers = SplitList(SummarizationTriggers);
            _settings.EnableActivityLogging = EnableActivityLogging;
            _settings.EnableActivityAnalysis = EnableActivityAnalysis;
            UseOnDeviceStt = string.Equals(SelectedVoiceInputMode, BuiltInAndroidVoiceMode, StringComparison.Ordinal);
            _settings.UseOnDeviceStt = UseOnDeviceStt;
            _settings.WhisperModelPath = (WhisperModelPath ?? "").Trim();
            _settings.ObsidianVaultPath = (ObsidianVaultPath ?? "").Trim();
            _settings.McpServers = [.. McpServers];

            // Obsidian MCP path update logic...
            var obsidianMcp = _settings.McpServers.FirstOrDefault(s => s.Name == "obsidian");
            if (obsidianMcp != null)
            {
                var updatedArgs = new List<string>(obsidianMcp.Args);
                if (updatedArgs.Count > 0) updatedArgs[^1] = (ObsidianMcpPath ?? "").Trim(); else updatedArgs.Add((ObsidianMcpPath ?? "").Trim());
                var list = _settings.McpServers.ToList();
                var idx = list.FindIndex(s => s.Name == "obsidian");
                if (idx >= 0) list[idx] = obsidianMcp with { Args = updatedArgs };
                _settings.McpServers = list;
            }

            StatusMessage = "Configuration saved successfully.";
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
            AvailableModels.Clear();
            var activeLlm = LlmSettings.Providers.FirstOrDefault(p => p.Name == LlmSettings.ActiveProviderName);
            if (activeLlm != null && !string.IsNullOrWhiteSpace(activeLlm.ModelId))
            {
                AvailableModels.Add(activeLlm.ModelId);
            }
            PreRollSeconds = _settings.GlobalSettings.PreRollSeconds;
            CustomSystemMessage = _settings.GlobalSettings.CustomSystemMessage ?? "";
            WakeWordText = _settings.WakeWords?.FirstOrDefault() ?? "hey quantum";
            SelectedAudioRouting = _settings.AudioRouting.ToString();
            LoggingIntervalMinutes = _settings.LoggingInterval.TotalMinutes.ToString("0");
            SummarizationTriggers = _settings.SummarizationTriggers != null ? string.Join(", ", _settings.SummarizationTriggers) : string.Empty;
            EnableActivityLogging = _settings.EnableActivityLogging;
            EnableActivityAnalysis = _settings.EnableActivityAnalysis;
            UseLocalLlm = _settings.GlobalSettings.UseLocalLlm;
            UseLocalTts = _settings.GlobalSettings.UseLocalTts;
            UseOnDeviceStt = _settings.UseOnDeviceStt;
            SelectedVoiceInputMode = UseOnDeviceStt ? BuiltInAndroidVoiceMode : AiModelVoiceMode;
            VoiceInputModeDescription = GetVoiceInputModeDescription(UseOnDeviceStt);
            WhisperModelPath = _settings.WhisperModelPath;
            ObsidianVaultPath = _settings.ObsidianVaultPath;

            McpServers.Clear();
            foreach (var server in _settings.McpServers)
                McpServers.Add(server);

            var obsidianMcp = _settings.McpServers.FirstOrDefault(s => s.Name == "obsidian" && s.Transport == McpTransportType.Stdio);
            ObsidianMcpPath = obsidianMcp?.Args?.Count > 0 ? obsidianMcp.Args[^1] : _settings.ObsidianVaultPath;

            _debugLogger.Log("SettingsViewModel", $"Loaded structured settings: LLMProvider='{LlmSettings.ActiveProviderName}', STTProvider='{SttSettings.ActiveProviderName}'.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsViewModel.Load failed: {ex}");
            StatusMessage = "Failed to load settings.";
        }
        finally
        {
            _suppressModelRefresh = false;
            var activeLlmProvider = LlmSettings.Providers.FirstOrDefault(p => p.Name == LlmSettings.ActiveProviderName);
            if (activeLlmProvider != null && IsValidAbsoluteUrl(activeLlmProvider.Url))
            {
                _ = RefreshModelsAsync();
            }
        }
    }

    private string GetCurrentSvcUrl() => SelectedLlmProvider?.Url ?? "";

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

    private static string GetVoiceInputModeDescription(bool useBuiltIn) => useBuiltIn
        ? "Uses Android SpeechRecognizer for continuous wake-word dictation. It is fast, but not controlled by VAD/STT model settings."
        : "Uses the AI audio pipeline: provider-routed VAD plus the configured STT model/endpoint. This disables Android built-in dictation.";
}
