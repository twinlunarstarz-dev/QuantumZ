using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.Graphics;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Core.Models.Settings;

namespace QuantumZ.UI.ViewModels;

public sealed partial class SettingsViewModel(ISettingsService settingsService, IDebugLogger logger) : BaseViewModel
{
    private readonly ISettingsService _settingsService = settingsService;
    private readonly IDebugLogger _logger = logger;

    private static readonly Color AccentColor = Color.FromArgb("#FF0000");
    private static readonly Color InactiveColor = Color.FromArgb("#2A2A2A");
    private static readonly Color ActiveTextColor = Colors.White;
    private static readonly Color InactiveTextColor = Color.FromArgb("#888888");

    public static readonly IReadOnlyList<string> AudioOutputOptions =
        ["Auto", "Speaker", "Bluetooth", "Headset"];

    // ── Status ──────────────────────────────────────────────────────────────

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    // ── Voice Assistant / Global Settings ───────────────────────────────────

    private string _triggerPhrase = "hey quantum";
    public string TriggerPhrase { get => _triggerPhrase; set => SetProperty(ref _triggerPhrase, value); }

    private string _systemPrompt = string.Empty;
    public string SystemPrompt { get => _systemPrompt; set => SetProperty(ref _systemPrompt, value); }

    private double _preRollSeconds = 5.0;
    public double PreRollSeconds { get => _preRollSeconds; set => SetProperty(ref _preRollSeconds, value); }

    private double _postSilenceSeconds = 1.2;
    public double PostSilenceSeconds { get => _postSilenceSeconds; set => SetProperty(ref _postSilenceSeconds, value); }

    private double _wakeWordThreshold = 0.85;
    public double WakeWordThreshold { get => _wakeWordThreshold; set => SetProperty(ref _wakeWordThreshold, value); }

    private int _selectedAudioOutput;
    public int SelectedAudioOutput { get => _selectedAudioOutput; set => SetProperty(ref _selectedAudioOutput, value); }

    private double _maxToolCallIterations = 6;
    public double MaxToolCallIterations { get => _maxToolCallIterations; set => SetProperty(ref _maxToolCallIterations, value); }

    // ── Wake Word Stage ──────────────────────────────────────────────────────

    private bool _wakeWordEnabled = true;
    public bool WakeWordEnabled
    {
        get => _wakeWordEnabled;
        set { if (SetProperty(ref _wakeWordEnabled, value)) NotifyWakeWordShow(); }
    }

    private int _wakeWordMode;
    public int WakeWordMode
    {
        get => _wakeWordMode;
        set { if (SetProperty(ref _wakeWordMode, value)) { NotifyWakeWordShow(); NotifyWakeWordColors(); } }
    }

    private string _wakeWordRemoteUrl = string.Empty;
    public string WakeWordRemoteUrl { get => _wakeWordRemoteUrl; set => SetProperty(ref _wakeWordRemoteUrl, value); }
    private string _wakeWordRemoteApiKey = string.Empty;
    public string WakeWordRemoteApiKey { get => _wakeWordRemoteApiKey; set => SetProperty(ref _wakeWordRemoteApiKey, value); }
    private string _wakeWordRemoteModelId = string.Empty;
    public string WakeWordRemoteModelId { get => _wakeWordRemoteModelId; set => SetProperty(ref _wakeWordRemoteModelId, value); }
    private int _wakeWordRemoteTimeout = 30;
    public int WakeWordRemoteTimeout { get => _wakeWordRemoteTimeout; set => SetProperty(ref _wakeWordRemoteTimeout, value); }
    private string _wakeWordLocalModelPath = string.Empty;
    public string WakeWordLocalModelPath { get => _wakeWordLocalModelPath; set => SetProperty(ref _wakeWordLocalModelPath, value); }
    private int _wakeWordLocalServerPort = 8025;
    public int WakeWordLocalServerPort { get => _wakeWordLocalServerPort; set => SetProperty(ref _wakeWordLocalServerPort, value); }
    private string _wakeWordLocalParameters = string.Empty;
    public string WakeWordLocalParameters { get => _wakeWordLocalParameters; set => SetProperty(ref _wakeWordLocalParameters, value); }

    public bool WakeWordShowRemote => WakeWordEnabled && WakeWordMode == 0;
    public bool WakeWordShowLocal => WakeWordEnabled && WakeWordMode == 1;
    public bool WakeWordShowBuiltIn => WakeWordEnabled && WakeWordMode == 2;
    public Color WakeWordRemoteButtonColor => WakeWordMode == 0 ? AccentColor : InactiveColor;
    public Color WakeWordLocalButtonColor => WakeWordMode == 1 ? AccentColor : InactiveColor;
    public Color WakeWordBuiltInButtonColor => WakeWordMode == 2 ? AccentColor : InactiveColor;
    public Color WakeWordRemoteButtonTextColor => WakeWordMode == 0 ? ActiveTextColor : InactiveTextColor;
    public Color WakeWordLocalButtonTextColor => WakeWordMode == 1 ? ActiveTextColor : InactiveTextColor;
    public Color WakeWordBuiltInButtonTextColor => WakeWordMode == 2 ? ActiveTextColor : InactiveTextColor;

    private void NotifyWakeWordShow()
    {
        OnPropertyChanged(nameof(WakeWordShowRemote));
        OnPropertyChanged(nameof(WakeWordShowLocal));
        OnPropertyChanged(nameof(WakeWordShowBuiltIn));
    }

    private void NotifyWakeWordColors()
    {
        OnPropertyChanged(nameof(WakeWordRemoteButtonColor)); OnPropertyChanged(nameof(WakeWordLocalButtonColor)); OnPropertyChanged(nameof(WakeWordBuiltInButtonColor));
        OnPropertyChanged(nameof(WakeWordRemoteButtonTextColor)); OnPropertyChanged(nameof(WakeWordLocalButtonTextColor)); OnPropertyChanged(nameof(WakeWordBuiltInButtonTextColor));
    }

    // ── VAD Stage ────────────────────────────────────────────────────────────

    private bool _vadEnabled = true;
    public bool VadEnabled
    {
        get => _vadEnabled;
        set { if (SetProperty(ref _vadEnabled, value)) NotifyVadShow(); }
    }

    private int _vadMode;
    public int VadMode
    {
        get => _vadMode;
        set { if (SetProperty(ref _vadMode, value)) { NotifyVadShow(); NotifyVadColors(); } }
    }

    private string _vadRemoteUrl = string.Empty;
    public string VadRemoteUrl { get => _vadRemoteUrl; set => SetProperty(ref _vadRemoteUrl, value); }
    private string _vadRemoteApiKey = string.Empty;
    public string VadRemoteApiKey { get => _vadRemoteApiKey; set => SetProperty(ref _vadRemoteApiKey, value); }
    private string _vadRemoteModelId = string.Empty;
    public string VadRemoteModelId { get => _vadRemoteModelId; set => SetProperty(ref _vadRemoteModelId, value); }
    private int _vadRemoteTimeout = 30;
    public int VadRemoteTimeout { get => _vadRemoteTimeout; set => SetProperty(ref _vadRemoteTimeout, value); }
    private string _vadLocalModelPath = string.Empty;
    public string VadLocalModelPath { get => _vadLocalModelPath; set => SetProperty(ref _vadLocalModelPath, value); }
    private int _vadLocalServerPort = 8025;
    public int VadLocalServerPort { get => _vadLocalServerPort; set => SetProperty(ref _vadLocalServerPort, value); }
    private string _vadLocalParameters = string.Empty;
    public string VadLocalParameters { get => _vadLocalParameters; set => SetProperty(ref _vadLocalParameters, value); }

    public bool VadShowRemote => VadEnabled && VadMode == 0;
    public bool VadShowLocal => VadEnabled && VadMode == 1;
    public bool VadShowBuiltIn => VadEnabled && VadMode == 2;
    public Color VadRemoteButtonColor => VadMode == 0 ? AccentColor : InactiveColor;
    public Color VadLocalButtonColor => VadMode == 1 ? AccentColor : InactiveColor;
    public Color VadBuiltInButtonColor => VadMode == 2 ? AccentColor : InactiveColor;
    public Color VadRemoteButtonTextColor => VadMode == 0 ? ActiveTextColor : InactiveTextColor;
    public Color VadLocalButtonTextColor => VadMode == 1 ? ActiveTextColor : InactiveTextColor;
    public Color VadBuiltInButtonTextColor => VadMode == 2 ? ActiveTextColor : InactiveTextColor;

    private void NotifyVadShow()
    {
        OnPropertyChanged(nameof(VadShowRemote));
        OnPropertyChanged(nameof(VadShowLocal));
        OnPropertyChanged(nameof(VadShowBuiltIn));
    }

    private void NotifyVadColors()
    {
        OnPropertyChanged(nameof(VadRemoteButtonColor)); OnPropertyChanged(nameof(VadLocalButtonColor)); OnPropertyChanged(nameof(VadBuiltInButtonColor));
        OnPropertyChanged(nameof(VadRemoteButtonTextColor)); OnPropertyChanged(nameof(VadLocalButtonTextColor)); OnPropertyChanged(nameof(VadBuiltInButtonTextColor));
    }

    // ── STT Stage ────────────────────────────────────────────────────────────

    private bool _sttEnabled = true;
    public bool SttEnabled
    {
        get => _sttEnabled;
        set { if (SetProperty(ref _sttEnabled, value)) NotifySttShow(); }
    }

    private int _sttMode;
    public int SttMode
    {
        get => _sttMode;
        set { if (SetProperty(ref _sttMode, value)) { NotifySttShow(); NotifySttColors(); } }
    }

    private string _sttRemoteUrl = string.Empty;
    public string SttRemoteUrl { get => _sttRemoteUrl; set => SetProperty(ref _sttRemoteUrl, value); }
    private string _sttRemoteApiKey = string.Empty;
    public string SttRemoteApiKey { get => _sttRemoteApiKey; set => SetProperty(ref _sttRemoteApiKey, value); }
    private string _sttRemoteModelId = string.Empty;
    public string SttRemoteModelId { get => _sttRemoteModelId; set => SetProperty(ref _sttRemoteModelId, value); }
    private int _sttRemoteTimeout = 30;
    public int SttRemoteTimeout { get => _sttRemoteTimeout; set => SetProperty(ref _sttRemoteTimeout, value); }
    private string _sttLocalModelPath = string.Empty;
    public string SttLocalModelPath { get => _sttLocalModelPath; set => SetProperty(ref _sttLocalModelPath, value); }
    private int _sttLocalServerPort = 8025;
    public int SttLocalServerPort { get => _sttLocalServerPort; set => SetProperty(ref _sttLocalServerPort, value); }
    private string _sttLocalParameters = string.Empty;
    public string SttLocalParameters { get => _sttLocalParameters; set => SetProperty(ref _sttLocalParameters, value); }

    public bool SttShowRemote => SttEnabled && SttMode == 0;
    public bool SttShowLocal => SttEnabled && SttMode == 1;
    public bool SttShowBuiltIn => SttEnabled && SttMode == 2;
    public Color SttRemoteButtonColor => SttMode == 0 ? AccentColor : InactiveColor;
    public Color SttLocalButtonColor => SttMode == 1 ? AccentColor : InactiveColor;
    public Color SttBuiltInButtonColor => SttMode == 2 ? AccentColor : InactiveColor;
    public Color SttRemoteButtonTextColor => SttMode == 0 ? ActiveTextColor : InactiveTextColor;
    public Color SttLocalButtonTextColor => SttMode == 1 ? ActiveTextColor : InactiveTextColor;
    public Color SttBuiltInButtonTextColor => SttMode == 2 ? ActiveTextColor : InactiveTextColor;

    private void NotifySttShow()
    {
        OnPropertyChanged(nameof(SttShowRemote));
        OnPropertyChanged(nameof(SttShowLocal));
        OnPropertyChanged(nameof(SttShowBuiltIn));
    }

    private void NotifySttColors()
    {
        OnPropertyChanged(nameof(SttRemoteButtonColor)); OnPropertyChanged(nameof(SttLocalButtonColor)); OnPropertyChanged(nameof(SttBuiltInButtonColor));
        OnPropertyChanged(nameof(SttRemoteButtonTextColor)); OnPropertyChanged(nameof(SttLocalButtonTextColor)); OnPropertyChanged(nameof(SttBuiltInButtonTextColor));
    }

    // ── LLM Stage ────────────────────────────────────────────────────────────

    private bool _llmEnabled = true;
    public bool LlmEnabled
    {
        get => _llmEnabled;
        set { if (SetProperty(ref _llmEnabled, value)) NotifyLlmShow(); }
    }

    private int _llmMode;
    public int LlmMode
    {
        get => _llmMode;
        set { if (SetProperty(ref _llmMode, value)) { NotifyLlmShow(); NotifyLlmColors(); } }
    }

    private string _llmRemoteUrl = string.Empty;
    public string LlmRemoteUrl { get => _llmRemoteUrl; set => SetProperty(ref _llmRemoteUrl, value); }
    private string _llmRemoteApiKey = string.Empty;
    public string LlmRemoteApiKey { get => _llmRemoteApiKey; set => SetProperty(ref _llmRemoteApiKey, value); }
    private string _llmRemoteModelId = string.Empty;
    public string LlmRemoteModelId { get => _llmRemoteModelId; set => SetProperty(ref _llmRemoteModelId, value); }
    private int _llmRemoteTimeout = 30;
    public int LlmRemoteTimeout { get => _llmRemoteTimeout; set => SetProperty(ref _llmRemoteTimeout, value); }
    private string _llmLocalModelPath = string.Empty;
    public string LlmLocalModelPath { get => _llmLocalModelPath; set => SetProperty(ref _llmLocalModelPath, value); }
    private int _llmLocalServerPort = 8025;
    public int LlmLocalServerPort { get => _llmLocalServerPort; set => SetProperty(ref _llmLocalServerPort, value); }
    private string _llmLocalParameters = string.Empty;
    public string LlmLocalParameters { get => _llmLocalParameters; set => SetProperty(ref _llmLocalParameters, value); }

    public bool LlmShowRemote => LlmEnabled && LlmMode == 0;
    public bool LlmShowLocal => LlmEnabled && LlmMode == 1;
    public Color LlmRemoteButtonColor => LlmMode == 0 ? AccentColor : InactiveColor;
    public Color LlmLocalButtonColor => LlmMode == 1 ? AccentColor : InactiveColor;
    public Color LlmRemoteButtonTextColor => LlmMode == 0 ? ActiveTextColor : InactiveTextColor;
    public Color LlmLocalButtonTextColor => LlmMode == 1 ? ActiveTextColor : InactiveTextColor;

    private void NotifyLlmShow()
    {
        OnPropertyChanged(nameof(LlmShowRemote));
        OnPropertyChanged(nameof(LlmShowLocal));
    }

    private void NotifyLlmColors()
    {
        OnPropertyChanged(nameof(LlmRemoteButtonColor)); OnPropertyChanged(nameof(LlmLocalButtonColor));
        OnPropertyChanged(nameof(LlmRemoteButtonTextColor)); OnPropertyChanged(nameof(LlmLocalButtonTextColor));
    }

    // ── TTS Stage ────────────────────────────────────────────────────────────

    private bool _ttsEnabled = true;
    public bool TtsEnabled
    {
        get => _ttsEnabled;
        set { if (SetProperty(ref _ttsEnabled, value)) NotifyTtsShow(); }
    }

    private int _ttsMode;
    public int TtsMode
    {
        get => _ttsMode;
        set { if (SetProperty(ref _ttsMode, value)) { NotifyTtsShow(); NotifyTtsColors(); } }
    }

    private string _ttsRemoteUrl = string.Empty;
    public string TtsRemoteUrl { get => _ttsRemoteUrl; set => SetProperty(ref _ttsRemoteUrl, value); }
    private string _ttsRemoteApiKey = string.Empty;
    public string TtsRemoteApiKey { get => _ttsRemoteApiKey; set => SetProperty(ref _ttsRemoteApiKey, value); }
    private string _ttsRemoteModelId = string.Empty;
    public string TtsRemoteModelId { get => _ttsRemoteModelId; set => SetProperty(ref _ttsRemoteModelId, value); }
    private int _ttsRemoteTimeout = 30;
    public int TtsRemoteTimeout { get => _ttsRemoteTimeout; set => SetProperty(ref _ttsRemoteTimeout, value); }
    private string _ttsLocalModelPath = string.Empty;
    public string TtsLocalModelPath { get => _ttsLocalModelPath; set => SetProperty(ref _ttsLocalModelPath, value); }
    private int _ttsLocalServerPort = 8025;
    public int TtsLocalServerPort { get => _ttsLocalServerPort; set => SetProperty(ref _ttsLocalServerPort, value); }
    private string _ttsLocalParameters = string.Empty;
    public string TtsLocalParameters { get => _ttsLocalParameters; set => SetProperty(ref _ttsLocalParameters, value); }

    public bool TtsShowRemote => TtsEnabled && TtsMode == 0;
    public bool TtsShowLocal => TtsEnabled && TtsMode == 1;
    public bool TtsShowBuiltIn => TtsEnabled && TtsMode == 2;
    public Color TtsRemoteButtonColor => TtsMode == 0 ? AccentColor : InactiveColor;
    public Color TtsLocalButtonColor => TtsMode == 1 ? AccentColor : InactiveColor;
    public Color TtsBuiltInButtonColor => TtsMode == 2 ? AccentColor : InactiveColor;
    public Color TtsRemoteButtonTextColor => TtsMode == 0 ? ActiveTextColor : InactiveTextColor;
    public Color TtsLocalButtonTextColor => TtsMode == 1 ? ActiveTextColor : InactiveTextColor;
    public Color TtsBuiltInButtonTextColor => TtsMode == 2 ? ActiveTextColor : InactiveTextColor;

    private void NotifyTtsShow()
    {
        OnPropertyChanged(nameof(TtsShowRemote));
        OnPropertyChanged(nameof(TtsShowLocal));
        OnPropertyChanged(nameof(TtsShowBuiltIn));
    }

    private void NotifyTtsColors()
    {
        OnPropertyChanged(nameof(TtsRemoteButtonColor)); OnPropertyChanged(nameof(TtsLocalButtonColor)); OnPropertyChanged(nameof(TtsBuiltInButtonColor));
        OnPropertyChanged(nameof(TtsRemoteButtonTextColor)); OnPropertyChanged(nameof(TtsLocalButtonTextColor)); OnPropertyChanged(nameof(TtsBuiltInButtonTextColor));
    }

    // ── MCP Servers ──────────────────────────────────────────────────────────

    public ObservableCollection<McpServerConfig> McpServers { get; } = [];

    private string _mcpNameInput = string.Empty;
    public string McpNameInput { get => _mcpNameInput; set => SetProperty(ref _mcpNameInput, value); }

    private string _mcpEndpointInput = string.Empty;
    public string McpEndpointInput { get => _mcpEndpointInput; set => SetProperty(ref _mcpEndpointInput, value); }

    private string _mcpApiKeyInput = string.Empty;
    public string McpApiKeyInput { get => _mcpApiKeyInput; set => SetProperty(ref _mcpApiKeyInput, value); }

    // ── Commands ─────────────────────────────────────────────────────────────

    private ICommand? _saveSettingsCommand;
    public ICommand SaveSettingsCommand => _saveSettingsCommand ??= new AsyncRelayCommand(SaveSettingsAsync);

    private ICommand? _setStageModeCommand;
    public ICommand SetStageModeCommand => _setStageModeCommand ??= new RelayCommand<string>(SetStageMode);

    private ICommand? _addMcpServerCommand;
    public ICommand AddMcpServerCommand => _addMcpServerCommand ??= new RelayCommand(AddMcpServer);

    private ICommand? _removeMcpServerCommand;
    public ICommand RemoveMcpServerCommand => _removeMcpServerCommand ??= new RelayCommand<McpServerConfig>(RemoveMcpServer);

    // ── Load / Save ──────────────────────────────────────────────────────────

    /// <summary>Populates all flat bindable properties from the settings service. Must be called after construction.</summary>
    public void LoadSettings()
    {
        try
        {
            var pipeline = _settingsService.PipelineSettings;
            var voice = _settingsService.VoiceAssistantSettings;

            TriggerPhrase = voice.TriggerPhrase;
            SystemPrompt = voice.SystemPrompt;
            PreRollSeconds = voice.PreRollSeconds;
            PostSilenceSeconds = voice.PostSilenceSeconds;
            WakeWordThreshold = voice.WakeWordThreshold;
            SelectedAudioOutput = (int)voice.AudioOutput;
            MaxToolCallIterations = voice.MaxToolCallIterations;

            WakeWordEnabled = pipeline.WakeWord.Enabled;
            WakeWordMode = (int)pipeline.WakeWord.Mode;
            WakeWordRemoteUrl = pipeline.WakeWord.Remote?.Url ?? string.Empty;
            WakeWordRemoteApiKey = pipeline.WakeWord.Remote?.ApiKey ?? string.Empty;
            WakeWordRemoteModelId = pipeline.WakeWord.Remote?.ModelId ?? string.Empty;
            WakeWordRemoteTimeout = pipeline.WakeWord.Remote?.TimeoutSeconds ?? 30;
            WakeWordLocalModelPath = pipeline.WakeWord.Local?.ModelPath ?? string.Empty;
            WakeWordLocalServerPort = pipeline.WakeWord.Local?.ServerPort ?? 8025;
            WakeWordLocalParameters = pipeline.WakeWord.Local?.AdditionalParameters ?? string.Empty;

            VadEnabled = pipeline.Vad.Enabled;
            VadMode = (int)pipeline.Vad.Mode;
            VadRemoteUrl = pipeline.Vad.Remote?.Url ?? string.Empty;
            VadRemoteApiKey = pipeline.Vad.Remote?.ApiKey ?? string.Empty;
            VadRemoteModelId = pipeline.Vad.Remote?.ModelId ?? string.Empty;
            VadRemoteTimeout = pipeline.Vad.Remote?.TimeoutSeconds ?? 30;
            VadLocalModelPath = pipeline.Vad.Local?.ModelPath ?? string.Empty;
            VadLocalServerPort = pipeline.Vad.Local?.ServerPort ?? 8025;
            VadLocalParameters = pipeline.Vad.Local?.AdditionalParameters ?? string.Empty;

            SttEnabled = pipeline.Stt.Enabled;
            SttMode = (int)pipeline.Stt.Mode;
            SttRemoteUrl = pipeline.Stt.Remote?.Url ?? string.Empty;
            SttRemoteApiKey = pipeline.Stt.Remote?.ApiKey ?? string.Empty;
            SttRemoteModelId = pipeline.Stt.Remote?.ModelId ?? string.Empty;
            SttRemoteTimeout = pipeline.Stt.Remote?.TimeoutSeconds ?? 30;
            SttLocalModelPath = pipeline.Stt.Local?.ModelPath ?? string.Empty;
            SttLocalServerPort = pipeline.Stt.Local?.ServerPort ?? 8025;
            SttLocalParameters = pipeline.Stt.Local?.AdditionalParameters ?? string.Empty;

            LlmEnabled = pipeline.Llm.Enabled;
            LlmMode = (int)pipeline.Llm.Mode;
            LlmRemoteUrl = pipeline.Llm.Remote?.Url ?? string.Empty;
            LlmRemoteApiKey = pipeline.Llm.Remote?.ApiKey ?? string.Empty;
            LlmRemoteModelId = pipeline.Llm.Remote?.ModelId ?? string.Empty;
            LlmRemoteTimeout = pipeline.Llm.Remote?.TimeoutSeconds ?? 30;
            LlmLocalModelPath = pipeline.Llm.Local?.ModelPath ?? string.Empty;
            LlmLocalServerPort = pipeline.Llm.Local?.ServerPort ?? 8025;
            LlmLocalParameters = pipeline.Llm.Local?.AdditionalParameters ?? string.Empty;

            TtsEnabled = pipeline.Tts.Enabled;
            TtsMode = (int)pipeline.Tts.Mode;
            TtsRemoteUrl = pipeline.Tts.Remote?.Url ?? string.Empty;
            TtsRemoteApiKey = pipeline.Tts.Remote?.ApiKey ?? string.Empty;
            TtsRemoteModelId = pipeline.Tts.Remote?.ModelId ?? string.Empty;
            TtsRemoteTimeout = pipeline.Tts.Remote?.TimeoutSeconds ?? 30;
            TtsLocalModelPath = pipeline.Tts.Local?.ModelPath ?? string.Empty;
            TtsLocalServerPort = pipeline.Tts.Local?.ServerPort ?? 8025;
            TtsLocalParameters = pipeline.Tts.Local?.AdditionalParameters ?? string.Empty;

            McpServers.Clear();
            foreach (var srv in _settingsService.McpServers)
                McpServers.Add(srv);

            _logger.Log("SettingsViewModel", "Settings loaded.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            _logger.Log("SettingsViewModel", $"Load failed: {ex.Message}", LogLevel.Error);
            StatusMessage = "Failed to load settings.";
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            _settingsService.VoiceAssistantSettings = new VoiceAssistantSettings
            {
                TriggerPhrase = TriggerPhrase,
                SystemPrompt = SystemPrompt,
                PreRollSeconds = (float)PreRollSeconds,
                PostSilenceSeconds = (float)PostSilenceSeconds,
                WakeWordThreshold = (float)WakeWordThreshold,
                AudioOutput = (AudioOutputMode)SelectedAudioOutput,
                MaxToolCallIterations = (int)MaxToolCallIterations,
            };

            _settingsService.PipelineSettings = new PipelineSettings
            {
                WakeWord = BuildStage(WakeWordEnabled, WakeWordMode, WakeWordRemoteUrl, WakeWordRemoteApiKey, WakeWordRemoteModelId, WakeWordRemoteTimeout, WakeWordLocalModelPath, WakeWordLocalServerPort, WakeWordLocalParameters),
                Vad = BuildStage(VadEnabled, VadMode, VadRemoteUrl, VadRemoteApiKey, VadRemoteModelId, VadRemoteTimeout, VadLocalModelPath, VadLocalServerPort, VadLocalParameters),
                Stt = BuildStage(SttEnabled, SttMode, SttRemoteUrl, SttRemoteApiKey, SttRemoteModelId, SttRemoteTimeout, SttLocalModelPath, SttLocalServerPort, SttLocalParameters),
                Llm = BuildStage(LlmEnabled, LlmMode, LlmRemoteUrl, LlmRemoteApiKey, LlmRemoteModelId, LlmRemoteTimeout, LlmLocalModelPath, LlmLocalServerPort, LlmLocalParameters),
                Tts = BuildStage(TtsEnabled, TtsMode, TtsRemoteUrl, TtsRemoteApiKey, TtsRemoteModelId, TtsRemoteTimeout, TtsLocalModelPath, TtsLocalServerPort, TtsLocalParameters),
            };

            _settingsService.McpServers = [.. McpServers];

            StatusMessage = "Settings saved.";
            _logger.Log("SettingsViewModel", "Settings saved.", LogLevel.Info);
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Log("SettingsViewModel", $"Save failed: {ex.Message}", LogLevel.Error);
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>Constructs a <see cref="StageSettings"/> record from flat ViewModel properties.</summary>
    private static StageSettings BuildStage(
        bool enabled, int mode,
        string url, string apiKey, string modelId, int timeout,
        string localPath, int localPort, string localParams) => new()
    {
        Enabled = enabled,
        Mode = (ModelMode)mode,
        Remote = mode == 0 ? new RemoteEndpointConfig
        {
            Url = url,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId,
            TimeoutSeconds = timeout,
        } : null,
        Local = mode == 1 ? new LocalModelConfig
        {
            ModelPath = localPath,
            ServerPort = localPort,
            AdditionalParameters = string.IsNullOrWhiteSpace(localParams) ? null : localParams,
        } : null,
    };

    /// <summary>Parses "StageName:modeIndex" from button CommandParameter and updates the appropriate mode property.</summary>
    private void SetStageMode(string? param)
    {
        if (param is null) return;
        var idx = param.IndexOf(':');
        if (idx < 0 || !int.TryParse(param.AsSpan(idx + 1), out var mode)) return;
        switch (param[..idx])
        {
            case "WakeWord": WakeWordMode = mode; break;
            case "Vad": VadMode = mode; break;
            case "Stt": SttMode = mode; break;
            case "Llm": LlmMode = mode; break;
            case "Tts": TtsMode = mode; break;
        }
    }

    private void AddMcpServer()
    {
        if (string.IsNullOrWhiteSpace(McpNameInput) || string.IsNullOrWhiteSpace(McpEndpointInput))
        {
            StatusMessage = "Name and endpoint are required.";
            return;
        }
        McpServers.Add(new McpServerConfig(
            McpNameInput.Trim(),
            McpEndpointInput.Trim(),
            string.IsNullOrWhiteSpace(McpApiKeyInput) ? null : McpApiKeyInput.Trim()
        ));
        McpNameInput = string.Empty;
        McpEndpointInput = string.Empty;
        McpApiKeyInput = string.Empty;
        StatusMessage = "MCP server added. Remember to save.";
    }

    private void RemoveMcpServer(McpServerConfig? server)
    {
        if (server is null) return;
        McpServers.Remove(server);
        StatusMessage = "MCP server removed.";
    }
}
