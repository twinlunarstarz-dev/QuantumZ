using System.Windows.Input;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Infrastructure.Services;
using QuantumZ.UI.Pages;

namespace QuantumZ.UI.ViewModels;

public partial class MainAssistantViewModel(
    IFluxAssetService assetService,
    IServiceProvider serviceProvider,
    IAudioVisualizer audioVisualizer,
    ISettingsService settingsService,
    IActivityLogger activityLogger,
    ILogSummaryService logSummaryService,
    IModelRegistry modelRegistry,
    ILlamaLocalManager llamaLocalManager,
    IAIClient aiClient,
    IProviderRouter providerRouter,
    IMcpOrchestrator mcpOrchestrator,
    IMemoryService memoryService,
    ActivityAnalyzerService activityAnalyzer,
    IDialogService dialogService,
    WhisperModelDownloader whisperDownloader,
    ISpeechStateService speechStateService,
    IThermalMonitor thermalMonitor,
    IDebugLogger debugLogger,
    IPipelineStateService pipelineStateService) : BaseViewModel
{
    private readonly IFluxAssetService _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly IAudioVisualizer _audioVisualizer = audioVisualizer ?? throw new ArgumentNullException(nameof(audioVisualizer));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly IActivityLogger _activityLogger = activityLogger ?? throw new ArgumentNullException(nameof(activityLogger));
    private readonly ILogSummaryService _logSummaryService = logSummaryService ?? throw new ArgumentNullException(nameof(logSummaryService));
    private readonly IModelRegistry _modelRegistry = modelRegistry ?? throw new ArgumentNullException(nameof(modelRegistry));
    private readonly ILlamaLocalManager _llamaLocalManager = llamaLocalManager ?? throw new ArgumentNullException(nameof(llamaLocalManager));
    private readonly IAIClient _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
    private readonly IProviderRouter _providerRouter = providerRouter ?? throw new ArgumentNullException(nameof(providerRouter));
    private readonly IMcpOrchestrator _mcpOrchestrator = mcpOrchestrator ?? throw new ArgumentNullException(nameof(mcpOrchestrator));
    private readonly IMemoryService _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
    private readonly ActivityAnalyzerService _activityAnalyzer = activityAnalyzer ?? throw new ArgumentNullException(nameof(activityAnalyzer));
    private readonly IDialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    private readonly WhisperModelDownloader _whisperDownloader = whisperDownloader ?? throw new ArgumentNullException(nameof(whisperDownloader));
    private readonly ISpeechStateService _speechStateService = speechStateService ?? throw new ArgumentNullException(nameof(speechStateService));
    private readonly IThermalMonitor _thermalMonitor = thermalMonitor ?? throw new ArgumentNullException(nameof(thermalMonitor));
    private readonly IDebugLogger _debugLogger = debugLogger ?? throw new ArgumentNullException(nameof(debugLogger));
    private readonly IPipelineStateService _pipelineStateService = pipelineStateService ?? throw new ArgumentNullException(nameof(pipelineStateService));
    private DateTime _lastServiceHealthRefreshUtc = DateTime.MinValue;
    private bool _serviceHealthRefreshInFlight;

    private string _assistantImageSource = string.Empty;
    public string AssistantImageSource
    {
        get => _assistantImageSource;
        set => SetProperty(ref _assistantImageSource, value);
    }

    private bool _isListening;
    public bool IsListening
    {
        get => _isListening;
        set => SetProperty(ref _isListening, value);
    }

    private bool _isDetectingActivity;
    public bool IsDetectingActivity
    {
        get => _isDetectingActivity;
        set => SetProperty(ref _isDetectingActivity, value);
    }

    private double _audioLevel;
    public double AudioLevel
    {
        get => _audioLevel;
        set => SetProperty(ref _audioLevel, value);
    }

    private string _aiStatusText = "Ready";
    public string AiStatusText
    {
        get => _aiStatusText;
        set => SetProperty(ref _aiStatusText, value);
    }

    private string _statusColor = "#9A9A9A";
    public string StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    private string _currentTranscription = string.Empty;
    public string CurrentTranscription
    {
        get => _currentTranscription;
        set => SetProperty(ref _currentTranscription, value);
    }

    private string _lastTranscription = string.Empty;
    public string LastTranscription
    {
        get => _lastTranscription;
        set => SetProperty(ref _lastTranscription, value);
    }

    private string _lastResponse = string.Empty;
    public string LastResponse
    {
        get => _lastResponse;
        set => SetProperty(ref _lastResponse, value);
    }

    private string _streamingTranscriptDisplay = "Awaiting voice stream...";
    public string StreamingTranscriptDisplay
    {
        get => _streamingTranscriptDisplay;
        set => SetProperty(ref _streamingTranscriptDisplay, value);
    }

    private string _transcriptionStatusText = "STT STREAM IDLE";
    public string TranscriptionStatusText
    {
        get => _transcriptionStatusText;
        set => SetProperty(ref _transcriptionStatusText, value);
    }

    private string _activeModelName = "Discovering model graph...";
    public string ActiveModelName
    {
        get => _activeModelName;
        set => SetProperty(ref _activeModelName, value);
    }

    private string _modelEndpointText = "Endpoint telemetry pending";
    public string ModelEndpointText
    {
        get => _modelEndpointText;
        set => SetProperty(ref _modelEndpointText, value);
    }

    private string _serverHealthText = "SERVER LINK STANDBY";
    public string ServerHealthText
    {
        get => _serverHealthText;
        set => SetProperty(ref _serverHealthText, value);
    }

    private string _gpuStatusText = "GPU TELEMETRY PENDING";
    public string GpuStatusText
    {
        get => _gpuStatusText;
        set => SetProperty(ref _gpuStatusText, value);
    }

    public ObservableCollection<HudMetric> ModelStatusItems { get; } = [];

    public ObservableCollection<HudMetric> ServerTelemetryItems { get; } = [];

    public ObservableCollection<ServiceHealthMetric> ServiceHealthItems { get; } = [];

    public ObservableCollection<HudMetric> MemoryStatusItems { get; } = [];

    private string _serviceHealthSummaryText = "Health checks pending";
    public string ServiceHealthSummaryText
    {
        get => _serviceHealthSummaryText;
        set => SetProperty(ref _serviceHealthSummaryText, value);
    }

    private string _panelTitle = string.Empty;
    public string PanelTitle
    {
        get => _panelTitle;
        set => SetProperty(ref _panelTitle, value);
    }

    private string _panelSubtitle = string.Empty;
    public string PanelSubtitle
    {
        get => _panelSubtitle;
        set => SetProperty(ref _panelSubtitle, value);
    }

    private bool _isAnyPanelOpen;
    public bool IsAnyPanelOpen
    {
        get => _isAnyPanelOpen;
        set => SetProperty(ref _isAnyPanelOpen, value);
    }

    private string _memorySummaryCountText = "0 ITEMS";
    public string MemorySummaryCountText
    {
        get => _memorySummaryCountText;
        set => SetProperty(ref _memorySummaryCountText, value);
    }

    private string _memoryVaultPathText = "Vault path pending";
    public string MemoryVaultPathText
    {
        get => _memoryVaultPathText;
        set => SetProperty(ref _memoryVaultPathText, value);
    }

    private string _lastMemorySummaryText = "No memory summaries loaded yet.";
    public string LastMemorySummaryText
    {
        get => _lastMemorySummaryText;
        set => SetProperty(ref _lastMemorySummaryText, value);
    }

    private string _configVoiceRouteText = "Voice route telemetry pending.";
    public string ConfigVoiceRouteText
    {
        get => _configVoiceRouteText;
        set => SetProperty(ref _configVoiceRouteText, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private bool _isDetailViewOpen;
    public bool IsDetailViewOpen
    {
        get => _isDetailViewOpen;
        set => SetProperty(ref _isDetailViewOpen, value);
    }

    private bool _isMemoryViewOpen;
    public bool IsMemoryViewOpen
    {
        get => _isMemoryViewOpen;
        set => SetProperty(ref _isMemoryViewOpen, value);
    }

    private bool _isConfigViewOpen;
    public bool IsConfigViewOpen
    {
        get => _isConfigViewOpen;
        set => SetProperty(ref _isConfigViewOpen, value);
    }

    private PipelineState _currentState;
    /// <summary>Current voice-pipeline state; updated by <see cref="OnPipelineStateChanged"/>.</summary>
    public PipelineState CurrentState
    {
        get => _currentState;
        set => SetProperty(ref _currentState, value);
    }

    private ICommand? _toggleListeningCommand;
    public ICommand ToggleListeningCommand => _toggleListeningCommand ??= new AsyncRelayCommand(ToggleListeningAsync);

    private ICommand? _stopListeningCommand;
    public ICommand StopListeningCommand => _stopListeningCommand ??= new RelayCommand(StopListening);

    private ICommand? _navigateToSettingsCommand;
    public ICommand NavigateToSettingsCommand => _navigateToSettingsCommand ??= new AsyncRelayCommand(NavigateToSettingsAsync);

    private ICommand? _navigateToMemoryCommand;
    public ICommand NavigateToMemoryCommand => _navigateToMemoryCommand ??= new AsyncRelayCommand(NavigateToMemoryAsync);

    private ICommand? _navigateToDebugCommand;
    public ICommand NavigateToDebugCommand => _navigateToDebugCommand ??= new AsyncRelayCommand(NavigateToDebugAsync);

    private ICommand? _toggleDetailCommand;
    public ICommand ToggleDetailCommand => _toggleDetailCommand ??= new RelayCommand(ToggleDetail);

    private ICommand? _toggleMemoryCommand;
    public ICommand ToggleMemoryCommand => _toggleMemoryCommand ??= new RelayCommand(ToggleMemory);

    private ICommand? _toggleConfigCommand;
    public ICommand ToggleConfigCommand => _toggleConfigCommand ??= new RelayCommand(ToggleConfig);

    private ICommand? _closePanelCommand;
    public ICommand ClosePanelCommand => _closePanelCommand ??= new RelayCommand(() => ClosePanels());

    private ICommand? _refreshServiceHealthCommand;
    public ICommand RefreshServiceHealthCommand => _refreshServiceHealthCommand ??= new AsyncRelayCommand(RefreshServiceHealthAsync);

    private ICommand? _summarizeNowCommand;
    public ICommand SummarizeNowCommand => _summarizeNowCommand ??= new AsyncRelayCommand(SummarizeNowAsync);

    private ICommand? _analyzeNowCommand;
    public ICommand AnalyzeNowCommand => _analyzeNowCommand ??= new AsyncRelayCommand(AnalyzeNowAsync);

    private ICommand? _testPipelineCommand;
    public ICommand TestPipelineCommand => _testPipelineCommand ??= new AsyncRelayCommand(TestPipelineAsync);

    private void ToggleDetail()
    {
        IsDetailViewOpen = !IsDetailViewOpen;
        if (IsDetailViewOpen)
        {
            IsMemoryViewOpen = false;
            IsConfigViewOpen = false;
            PanelTitle = "Assistant Details";
            PanelSubtitle = "Transcript, model routing, and live service verification.";
            IsAnyPanelOpen = true;
            _ = RefreshServiceHealthAsync(force: false);
        }
        else
        {
            UpdatePanelState();
        }
    }

    private void ToggleMemory()
    {
        IsMemoryViewOpen = !IsMemoryViewOpen;
        if (IsMemoryViewOpen)
        {
            IsDetailViewOpen = false;
            IsConfigViewOpen = false;
            PanelTitle = "Memory Vault";
            PanelSubtitle = "Recent summaries, local vault status, and memory routing.";
            IsAnyPanelOpen = true;
            _ = RefreshMemoryTelemetryAsync();
        }
        else
        {
            UpdatePanelState();
        }
    }

    private void ToggleConfig()
    {
        IsConfigViewOpen = !IsConfigViewOpen;
        if (IsConfigViewOpen)
        {
            IsDetailViewOpen = false;
            IsMemoryViewOpen = false;
            PanelTitle = "Assistant Config";
            PanelSubtitle = "Provider routes, active model, audio path, and MCP fabric.";
            IsAnyPanelOpen = true;
            _ = RefreshHudTelemetryAsync();
        }
        else
        {
            UpdatePanelState();
        }
    }

    public bool ClosePanels()
    {
        if (!IsAnyPanelOpen && !IsDetailViewOpen && !IsMemoryViewOpen && !IsConfigViewOpen)
            return false;

        IsDetailViewOpen = false;
        IsMemoryViewOpen = false;
        IsConfigViewOpen = false;
        UpdatePanelState();
        return true;
    }

    private void UpdatePanelState()
    {
        IsAnyPanelOpen = IsDetailViewOpen || IsMemoryViewOpen || IsConfigViewOpen;
        if (!IsAnyPanelOpen)
        {
            PanelTitle = string.Empty;
            PanelSubtitle = string.Empty;
        }
    }

    public void AttachVisualizerEvents()
    {
        _audioVisualizer.AudioLevelChanged += OnAudioLevelChanged;
        _audioVisualizer.StateChanged += OnStateChanged;
        _audioVisualizer.ActivityDetectedChanged += OnActivityDetectedChanged;
        _speechStateService.TranscriptionChanged += OnTranscriptionChanged;
        _thermalMonitor.StateChanged += OnThermalStateChanged;
        _pipelineStateService.StateChanged += OnPipelineStateChanged;
        ApplyStateAnimation(_pipelineStateService.CurrentState);
        _ = RefreshHudTelemetryAsync();
        _ = RefreshMemoryTelemetryAsync();
        _ = RefreshServiceHealthAsync(force: false);
    }

    public void DetachVisualizerEvents()
    {
        _audioVisualizer.AudioLevelChanged -= OnAudioLevelChanged;
        _audioVisualizer.StateChanged -= OnStateChanged;
        _audioVisualizer.ActivityDetectedChanged -= OnActivityDetectedChanged;
        _speechStateService.TranscriptionChanged -= OnTranscriptionChanged;
        _thermalMonitor.StateChanged -= OnThermalStateChanged;
        _pipelineStateService.StateChanged -= OnPipelineStateChanged;
    }

    private void OnAudioLevelChanged(object? sender, double level)
    {
        MainThread.BeginInvokeOnMainThread(() => AudioLevel = level);
    }

    private void OnStateChanged(object? sender, ListeningState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (state)
            {
                case ListeningState.Idle:
                    AiStatusText = "Ready";
                    StatusColor = "#9A9A9A";
                    IsListening = false;
                    break;
                case ListeningState.Listening:
                    AiStatusText = "Listening...";
                    StatusColor = "#FF003C";
                    IsListening = true;
                    break;
                case ListeningState.Processing:
                    AiStatusText = "Processing...";
                    StatusColor = "#FFA500";
                    break;
                case ListeningState.Speaking:
                    AiStatusText = "Speaking...";
                    StatusColor = "#00BFFF";
                    break;
            }
        });
    }

    private void OnActivityDetectedChanged(object? sender, bool detected)
    {
        MainThread.BeginInvokeOnMainThread(() => IsDetectingActivity = detected);
    }

    private void OnThermalStateChanged(object? sender, ThermalState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update the HUD metrics when thermal state changes in real-time
            _ = RefreshHudTelemetryAsync();
        });
    }

    private void OnTranscriptionChanged(object? sender, string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentTranscription = text;
            if (string.IsNullOrWhiteSpace(text))
            {
                TranscriptionStatusText = IsListening ? "STT STREAM ARMED" : "STT STREAM IDLE";
                StreamingTranscriptDisplay = IsListening ? "Listening for speech vector..." : "Awaiting voice stream...";
                return;
            }

            LastTranscription = text;
            TranscriptionStatusText = "LIVE STT STREAM";
            StreamingTranscriptDisplay = $"{text.Trim()} ▌";
        });
    }

    /// <summary>Reacts to pipeline state changes; updates <see cref="CurrentState"/> and triggers orb animation.</summary>
    private void OnPipelineStateChanged(object? sender, PipelineStateChangedArgs args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentState = args.Current;
            ApplyStateAnimation(args.Current);
        });
    }

    private async Task ToggleListeningAsync() => await SetListeningAsync(!IsListening);

    public async Task SetListeningAsync(bool shouldListen)
    {
        if (IsBusy || shouldListen == IsListening)
            return;

        IsBusy = true;
        try
        {
            if (shouldListen)
                await StartListeningAsync();
            else
                StopListeningCore();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggle listening failed: {ex}");
            _debugLogger.Log("MainAssistant", $"Monitoring toggle failed: {ex.Message}", LogLevel.Error);
            AiStatusText = "Error";
            StatusColor = "#FF0000";
            await _dialogService.ShowAlertAsync("Listening Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartListeningAsync()
    {
        if (!await EnsureSetupCompletedAsync())
            return;

        if (!await EnsureMicrophonePermissionAsync())
            return;

        if (_settingsService.UseOnDeviceStt && !_whisperDownloader.ModelExists())
        {
            _debugLogger.Log("MainAssistant", "On-device Whisper model is missing; Android SpeechRecognizer fallback remains available.", LogLevel.Warning);
        }

        StartMicrophoneService();
        IsListening = true;
        AiStatusText = "Listening...";
        StatusColor = "#FF003C";
        TranscriptionStatusText = "STT STREAM ARMED";
        StreamingTranscriptDisplay = "Listening for wake word and live dictation...";
    }

    private void StopListening()
    {
        StopListeningCore();
    }

    private void StopListeningCore()
    {
        StopMicrophoneService();
        IsListening = false;
        AiStatusText = "Ready";
        StatusColor = "#9A9A9A";
        AudioLevel = 0;
        TranscriptionStatusText = "STT STREAM IDLE";
        StreamingTranscriptDisplay = "Awaiting voice stream...";
    }

    private async Task NavigateToSettingsAsync()
    {
        try
        {
            var page = _serviceProvider.GetRequiredService<SettingsPage>();
            if (Application.Current?.MainPage?.Navigation != null)
            {
                await Application.Current.MainPage.Navigation.PushAsync(page);
            }
            else
            {
                throw new InvalidOperationException("MainPage navigation is not available.");
            }
        }
        catch (Exception ex)
        {
            _debugLogger.Log("MainAssistant", $"Navigation to settings failed: {ex.Message}", LogLevel.Error);
            await _dialogService.ShowAlertAsync("Navigation Error", "Unable to open settings page.");
        }
    }

    private async Task NavigateToMemoryAsync()
    {
        try
        {
            var page = _serviceProvider.GetRequiredService<MemoryPage>();
            if (Application.Current?.MainPage?.Navigation != null)
            {
                await Application.Current.MainPage.Navigation.PushAsync(page);
            }
            else
            {
                throw new InvalidOperationException("MainPage navigation is not available.");
            }
        }
        catch (Exception ex)
        {
            _debugLogger.Log("MainAssistant", $"Navigation to memory failed: {ex.Message}", LogLevel.Error);
            await _dialogService.ShowAlertAsync("Navigation Error", "Unable to open memory page.");
        }
    }

    private async Task NavigateToDebugAsync()
    {
        try
        {
            var page = _serviceProvider.GetRequiredService<DebugOverlayPage>();
            if (Application.Current?.MainPage?.Navigation != null)
            {
                await Application.Current.MainPage.Navigation.PushAsync(page);
            }
            else
            {
                throw new InvalidOperationException("MainPage navigation is not available.");
            }
        }
        catch (Exception ex)
        {
            _debugLogger.Log("MainAssistant", $"Navigation to debug failed: {ex.Message}", LogLevel.Error);
            await _dialogService.ShowAlertAsync("Navigation Error", "Unable to open debug overlay.");
        }
    }

    private async Task SummarizeNowAsync()
    {
        IsBusy = true;
        AiStatusText = "Summarizing...";
        try
        {
            var now = DateTime.UtcNow;
            var start = now.Date;
            var summary = await _logSummaryService.SummarizePeriodAsync(start, now);
            LastResponse = $"Summary saved: {summary.Date:yyyy-MM-dd}\n{summary.SummaryText}";
        }
        catch (Exception ex)
        {
            LastResponse = $"Summarization failed: {ex.Message}";
            await _dialogService.ShowAlertAsync("Summarize Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
            AiStatusText = IsListening ? "Listening..." : "Ready";
        }
    }

    private async Task AnalyzeNowAsync()
    {
        IsBusy = true;
        AiStatusText = "Analyzing...";
        try
        {
            var now = DateTime.UtcNow;
            var start = now.AddHours(-24);
            var result = await _activityAnalyzer.AnalyzeRecentPeriodAsync(start, now);
            if (result is null)
            {
                LastResponse = "Activity analysis is disabled or no logs available.";
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Mood: {result.Mood}");
                if (result.Suggestions.Count > 0)
                {
                    sb.AppendLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        sb.AppendLine($"- {s}");
                }
                if (result.Warnings.Count > 0)
                {
                    sb.AppendLine("Warnings:");
                    foreach (var w in result.Warnings)
                        sb.AppendLine($"- {w}");
                }
                LastResponse = sb.ToString();
            }
        }
        catch (Exception ex)
        {
            LastResponse = $"Analysis failed: {ex.Message}";
            await _dialogService.ShowAlertAsync("Analysis Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
            AiStatusText = IsListening ? "Listening..." : "Ready";
        }
    }

    public async Task InitializeAssetsAsync()
    {
        AssistantImageSource = string.Empty;

        await RefreshHudTelemetryAsync(force: true);
    }

    private async Task RefreshHudTelemetryAsync(bool force = false)
    {
        try
        {
            var models = await _modelRegistry.DiscoverModelsAsync(forceRefresh: force);
            var llmModel = models.FirstOrDefault(model => model.Capability == ProviderCapability.Llm && model.IsAvailable);
            var remoteCount = models.Count(model => model.Location is ProviderLocation.Remote or ProviderLocation.Hybrid);
            var localCount = models.Count(model => model.Location is ProviderLocation.Local or ProviderLocation.BuiltIn);
            var localServerOnline = await _llamaLocalManager.CheckHealthAsync();
            var localBinaryAvailable = _llamaLocalManager.IsBinaryAvailable();
            var selectedModel = _settingsService.GetActiveProvider("LLM")?.ModelId ?? "";
            var selectedProfile = models.FirstOrDefault(model => !string.IsNullOrWhiteSpace(selectedModel) && ModelMatchesSelection(model, selectedModel));

            ActiveModelName = FirstNonEmpty(selectedProfile?.DisplayName, llmModel?.DisplayName, selectedModel, "No reachable LLM model");
            ModelEndpointText = FirstNonEmpty(selectedProfile?.Endpoint, llmModel?.Endpoint, _settingsService.GetActiveProvider("LLM")?.Url ?? "", localServerOnline ? ModelRegistry.LocalLlamaBaseUrl : "No reachable endpoint");
            ServerHealthText = remoteCount > 0 ? "REMOTE SERVER ONLINE" : localServerOnline ? "LOCAL LLAMA SERVER ONLINE" : "LLM SERVER OFFLINE";
            GpuStatusText = remoteCount > 0 ? "GPU SERVER ROUTE READY" : localServerOnline ? "LOCAL CPU ROUTE READY" : localBinaryAvailable ? "LOCAL BINARY READY / SERVER STOPPED" : "LOCAL BINARY MISSING";
            ConfigVoiceRouteText = $"Trigger phrase: {FirstNonEmpty(_settingsService.VoiceAssistantSettings.TriggerPhrase, "hey quantum")} • Audio: {_settingsService.AudioRouting} • STT: {(_settingsService.UseOnDeviceStt ? "On-device Whisper" : ShortenEndpoint(_settingsService.GetActiveProvider("STT")?.Url ?? ""))}";

            ReplaceMetrics(ModelStatusItems,
            [
                new HudMetric("VAD", DescribeEndpoint(_settingsService.GetActiveProvider("VAD")?.Url ?? "", "Local RMS"), "#00FF88"),
                new HudMetric("STT", FirstNonEmpty(_settingsService.GetActiveProvider("STT")?.ModelId, "Provider default"), "#FF003C"),
                new HudMetric("LLM", ActiveModelName, "#FF335F"),
                new HudMetric("TTS", FirstNonEmpty(_settingsService.GetActiveProvider("TTS")?.ModelId, "Provider default"), "#FFB300")
            ]);

            var thermal = _thermalMonitor.CurrentState;
            string thermalColor = thermal.Level switch
            {
                ThermalLevel.Normal => "#00FF88",
                ThermalLevel.Warning => "#FFA500",
                ThermalLevel.Critical => "#FF003C",
                _ => "#9A9A9A"
            };

            ReplaceMetrics(ServerTelemetryItems,
            [
                new HudMetric("Remote", remoteCount > 0 ? $"{remoteCount} model(s)" : "Offline", remoteCount > 0 ? "#00FF88" : "#FFB300"),
                new HudMetric("Local Server", localServerOnline ? "Online" : "Offline", localServerOnline ? "#00FF88" : "#FFB300"),
                new HudMetric("llama.cpp", localBinaryAvailable ? "Binary installed" : "Binary missing", localBinaryAvailable ? "#00FF88" : "#FF003C"),
                new HudMetric("Thermal", $"{thermal.Level} ({thermal.BatteryTemperatureC:F1}°C)", thermalColor)
            ]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HUD telemetry refresh failed: {ex}");
            ServerHealthText = "TELEMETRY DEGRADED";
            GpuStatusText = "GPU STATUS UNKNOWN";
            ReplaceMetrics(ModelStatusItems,
            [
                new HudMetric("VAD", "Configured", "#FFB300"),
                new HudMetric("STT", "Configured", "#FFB300"),
                new HudMetric("LLM", FirstNonEmpty(_settingsService.GetActiveProvider("LLM")?.ModelId, "Unknown"), "#FFB300"),
                new HudMetric("TTS", "Configured", "#FFB300")
            ]);
        }
    }

    private async Task RefreshServiceHealthAsync() => await RefreshServiceHealthAsync(force: true);

    private async Task RefreshServiceHealthAsync(bool force)
    {
        if (_serviceHealthRefreshInFlight)
            return;

        if (!force && DateTime.UtcNow - _lastServiceHealthRefreshUtc < TimeSpan.FromMinutes(2))
            return;

        _serviceHealthRefreshInFlight = true;
        ServiceHealthSummaryText = "Running service smoke tests...";
        ReplaceServiceHealth([
            ServiceHealthMetric.Checking("LLM", "Testing server and response path"),
            ServiceHealthMetric.Checking("STT", "Checking selected speech-to-text provider"),
            ServiceHealthMetric.Checking("TTS", "Checking selected speech output provider"),
            ServiceHealthMetric.Checking("VAD", "Checking voice activity detector"),
            ServiceHealthMetric.Checking("MCP", "Discovering agent tools"),
            ServiceHealthMetric.Checking("Memory", "Reading memory vault")
        ]);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var checks = await Task.WhenAll(
                CheckLlmHealthAsync(timeout.Token),
                CheckProviderHealthAsync("STT", ProviderCapability.Stt, provider => $"{provider.Descriptor.DisplayName} available", timeout.Token),
                CheckProviderHealthAsync("TTS", ProviderCapability.Tts, provider => $"{provider.Descriptor.DisplayName} available", timeout.Token),
                CheckVadHealthAsync(timeout.Token),
                CheckMcpHealthAsync(timeout.Token),
                CheckMemoryHealthAsync(timeout.Token));

            ReplaceServiceHealth(checks);
            _lastServiceHealthRefreshUtc = DateTime.UtcNow;

            var passCount = checks.Count(check => check.State == ServiceHealthState.Ready);
            var partialCount = checks.Count(check => check.State == ServiceHealthState.Partial);
            var llm = checks.FirstOrDefault(check => check.Label == "LLM");
            if (llm is not null)
            {
                StatusColor = llm.State switch
                {
                    ServiceHealthState.Ready => "#00FF88",
                    ServiceHealthState.Partial => "#FFB300",
                    ServiceHealthState.Failed => "#FF003C",
                    _ => StatusColor
                };

                AiStatusText = llm.State switch
                {
                    ServiceHealthState.Ready => "LLM Verified",
                    ServiceHealthState.Partial => "LLM Link Ready",
                    ServiceHealthState.Failed => "LLM Offline",
                    _ => AiStatusText
                };
            }

            ServiceHealthSummaryText = partialCount > 0
                ? $"{passCount}/{checks.Length} verified, {partialCount} awaiting functional response"
                : $"{passCount}/{checks.Length} services verified";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Service health refresh failed: {ex}");
            ServiceHealthSummaryText = "Health checks degraded";
            ReplaceServiceHealth([
                ServiceHealthMetric.Failed("Health", $"Smoke tests failed: {ex.Message}")
            ]);
        }
        finally
        {
            _serviceHealthRefreshInFlight = false;
            await RefreshHudTelemetryAsync(force: true);
        }
    }

    private async Task<ServiceHealthMetric> CheckLlmHealthAsync(CancellationToken ct)
    {
        try
        {
            var models = await _modelRegistry.GetModelsAsync(ProviderCapability.Llm, ct);
            var selectedModel = _settingsService.GetActiveProvider("LLM")?.ModelId ?? "";
            var candidate = models.FirstOrDefault(model => model.IsAvailable && (string.IsNullOrWhiteSpace(selectedModel)
                || string.Equals(model.Id, selectedModel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(model.DisplayName, selectedModel, StringComparison.OrdinalIgnoreCase)))
                ?? models.FirstOrDefault(model => model.IsAvailable);

            if (candidate is null)
                return ServiceHealthMetric.Failed("LLM", "No reachable model list from configured endpoint");

            var request = new AiRequest(
                "this is a test message respond \"ok\".",
                Temperature: 0,
                MaxTokens: 16,
                EnableToolCalling: false);
            var response = await _aiClient.SendPromptAsync(request, ct);

            return string.IsNullOrWhiteSpace(response.Content)
                ? ServiceHealthMetric.Partial("LLM", $"Models loaded from {ShortenEndpoint(candidate.Endpoint)}, response pending")
                : ServiceHealthMetric.Ready("LLM", $"Response verified: {TrimForMetric(response.Content)}");
        }
        catch (OperationCanceledException)
        {
            return ServiceHealthMetric.Partial("LLM", "Model endpoint reachable check timed out before response");
        }
        catch (Exception ex)
        {
            return ServiceHealthMetric.Failed("LLM", TrimForMetric(ex.Message));
        }
    }

    private async Task<ServiceHealthMetric> CheckProviderHealthAsync(string label, ProviderCapability capability, Func<IProvider, string> successMessage, CancellationToken ct)
    {
        try
        {
            IProvider provider = capability switch
            {
                ProviderCapability.Stt => await _providerRouter.ResolveSttProviderAsync(ct),
                ProviderCapability.Tts => await _providerRouter.ResolveTtsProviderAsync(ct),
                _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null)
            };

            return ServiceHealthMetric.Ready(label, successMessage(provider));
        }
        catch (OperationCanceledException)
        {
            return ServiceHealthMetric.Partial(label, "Provider availability check timed out");
        }
        catch (Exception ex)
        {
            return ServiceHealthMetric.Failed(label, TrimForMetric(ex.Message));
        }
    }

    private async Task<ServiceHealthMetric> CheckVadHealthAsync(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[3200];
            var result = await _providerRouter.DetectSpeechAsync(buffer, 16000, ct);
            return ServiceHealthMetric.Ready("VAD", $"Smoke test returned {result.ActivityState}");
        }
        catch (OperationCanceledException)
        {
            return ServiceHealthMetric.Partial("VAD", "Detector check timed out");
        }
        catch (Exception ex)
        {
            return ServiceHealthMetric.Failed("VAD", TrimForMetric(ex.Message));
        }
    }

    private async Task<ServiceHealthMetric> CheckMcpHealthAsync(CancellationToken ct)
    {
        try
        {
            if (_settingsService.McpServers.Count == 0)
                return ServiceHealthMetric.Unconfigured("MCP", "No MCP servers configured");

            var tools = await _mcpOrchestrator.DiscoverToolsAsync(ct);
            return ServiceHealthMetric.Ready("MCP", $"{tools.Count} tool(s) discovered");
        }
        catch (OperationCanceledException)
        {
            return ServiceHealthMetric.Partial("MCP", "Tool discovery timed out");
        }
        catch (Exception ex)
        {
            return ServiceHealthMetric.Failed("MCP", TrimForMetric(ex.Message));
        }
    }

    private async Task<ServiceHealthMetric> CheckMemoryHealthAsync(CancellationToken ct)
    {
        try
        {
            var end = DateTime.UtcNow;
            var summaries = await _memoryService.GetSummariesAsync(end.AddDays(-7), end);
            var vault = string.IsNullOrWhiteSpace(_memoryService.LocalVaultPath) ? "vault path not configured" : ShortenPath(_memoryService.LocalVaultPath);
            return ServiceHealthMetric.Ready("Memory", $"{summaries.Count} summaries; {vault}");
        }
        catch (OperationCanceledException)
        {
            return ServiceHealthMetric.Partial("Memory", "Vault read timed out");
        }
        catch (Exception ex)
        {
            return ServiceHealthMetric.Failed("Memory", TrimForMetric(ex.Message));
        }
    }

    private async Task RefreshMemoryTelemetryAsync()
    {
        try
        {
            var end = DateTime.UtcNow;
            var start = end.AddDays(-7);
            var summaries = await _memoryService.GetSummariesAsync(start, end);
            var latest = summaries.OrderByDescending(summary => summary.Date).FirstOrDefault();

            MemorySummaryCountText = $"{summaries.Count} ITEMS";
            MemoryVaultPathText = string.IsNullOrWhiteSpace(_memoryService.LocalVaultPath)
                ? "Vault path not configured"
                : _memoryService.LocalVaultPath;
            LastMemorySummaryText = latest is null
                ? "No recent summaries found in the local memory vault."
                : $"{latest.Date:yyyy-MM-dd}: {latest.SummaryText}";

            ReplaceMetrics(MemoryStatusItems,
            [
                new HudMetric("Vault", string.IsNullOrWhiteSpace(_memoryService.LocalVaultPath) ? "Not configured" : "Configured", string.IsNullOrWhiteSpace(_memoryService.LocalVaultPath) ? "#FFB300" : "#00FF88"),
                new HudMetric("Window", "Last 7 days", "#FF335F"),
                new HudMetric("Summaries", summaries.Count.ToString(), summaries.Count > 0 ? "#00FF88" : "#AAAAAA"),
                new HudMetric("Logging", _settingsService.EnableActivityLogging ? "Enabled" : "Disabled", _settingsService.EnableActivityLogging ? "#00FF88" : "#FFB300")
            ]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Memory telemetry refresh failed: {ex}");
            MemorySummaryCountText = "DEGRADED";
            LastMemorySummaryText = "Memory telemetry is unavailable. Open the memory vault for full diagnostics.";
            ReplaceMetrics(MemoryStatusItems,
            [
                new HudMetric("Vault", "Unavailable", "#FFB300"),
                new HudMetric("Logging", _settingsService.EnableActivityLogging ? "Enabled" : "Disabled", "#FFB300")
            ]);
        }
    }

    private static void ReplaceMetrics(ObservableCollection<HudMetric> target, IEnumerable<HudMetric> values)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            target.Clear();
            foreach (var value in values)
                target.Add(value);
        });
    }

    private void ReplaceServiceHealth(IEnumerable<ServiceHealthMetric> values)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ServiceHealthItems.Clear();
            foreach (var value in values)
                ServiceHealthItems.Add(value);
        });
    }
}
