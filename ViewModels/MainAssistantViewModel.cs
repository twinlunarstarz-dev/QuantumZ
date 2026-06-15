using System.Windows.Input;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Infrastructure.Services;
using QuantumZ.UI.Pages;

namespace QuantumZ.UI.ViewModels;

public class MainAssistantViewModel(
    IFluxAssetService assetService,
    IServiceProvider serviceProvider,
    IAudioVisualizer audioVisualizer,
    ISettingsService settingsService,
    IActivityLogger activityLogger,
    ILogSummaryService logSummaryService,
    IModelRegistry modelRegistry,
    ActivityAnalyzerService activityAnalyzer,
    IDialogService dialogService,
    WhisperModelDownloader whisperDownloader,
    ISpeechStateService speechStateService,
    IThermalMonitor thermalMonitor) : BaseViewModel
{
    private readonly IFluxAssetService _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly IAudioVisualizer _audioVisualizer = audioVisualizer ?? throw new ArgumentNullException(nameof(audioVisualizer));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly IActivityLogger _activityLogger = activityLogger ?? throw new ArgumentNullException(nameof(activityLogger));
    private readonly ILogSummaryService _logSummaryService = logSummaryService ?? throw new ArgumentNullException(nameof(logSummaryService));
    private readonly IModelRegistry _modelRegistry = modelRegistry ?? throw new ArgumentNullException(nameof(modelRegistry));
    private readonly ActivityAnalyzerService _activityAnalyzer = activityAnalyzer ?? throw new ArgumentNullException(nameof(activityAnalyzer));
    private readonly IDialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    private readonly WhisperModelDownloader _whisperDownloader = whisperDownloader ?? throw new ArgumentNullException(nameof(whisperDownloader));
    private readonly ISpeechStateService _speechStateService = speechStateService ?? throw new ArgumentNullException(nameof(speechStateService));
    private readonly IThermalMonitor _thermalMonitor = thermalMonitor ?? throw new ArgumentNullException(nameof(thermalMonitor));

    private string _assistantImageSource = "dotnet_bot.png";
    public string AssistantImageSource
    {
        get => _assistantImageSource;
        set => SetProperty(ref _assistantImageSource, value);
    }

    private bool _isListening;
    public bool IsListening
    {
        get => _isListening;
        set
        {
            if (EqualityComparer<bool>.Default.Equals(_isListening, value)) return;
            SetProperty(ref _isListening, value);
            if (_stopListeningCommand is RelayCommand stopCommand)
                stopCommand.NotifyCanExecuteChanged();
        }
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

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private ICommand? _toggleListeningCommand;
    public ICommand ToggleListeningCommand => _toggleListeningCommand ??= new AsyncRelayCommand(ToggleListeningAsync);

    private ICommand? _stopListeningCommand;
    public ICommand StopListeningCommand => _stopListeningCommand ??= new RelayCommand(StopListening, () => IsListening);

    private ICommand? _navigateToSettingsCommand;
    public ICommand NavigateToSettingsCommand => _navigateToSettingsCommand ??= new AsyncRelayCommand(NavigateToSettingsAsync);

    private ICommand? _navigateToMemoryCommand;
    public ICommand NavigateToMemoryCommand => _navigateToMemoryCommand ??= new AsyncRelayCommand(NavigateToMemoryAsync);

    private bool _isDetailViewOpen;
    public bool IsDetailViewOpen
    {
        get => _isDetailViewOpen;
        set => SetProperty(ref _isDetailViewOpen, value);
    }

    private ICommand? _toggleDetailCommand;
    public ICommand ToggleDetailCommand => _toggleDetailCommand ??= new RelayCommand(ToggleDetail);

    private void ToggleDetail()
    {
        IsDetailViewOpen = !IsDetailViewOpen;
    }

    private ICommand? _summarizeNowCommand;
    public ICommand SummarizeNowCommand => _summarizeNowCommand ??= new AsyncRelayCommand(SummarizeNowAsync);

    private ICommand? _analyzeNowCommand;
    public ICommand AnalyzeNowCommand => _analyzeNowCommand ??= new AsyncRelayCommand(AnalyzeNowAsync);

    private ICommand? _testPipelineCommand;
    public ICommand TestPipelineCommand => _testPipelineCommand ??= new AsyncRelayCommand(TestPipelineAsync);

    public void AttachVisualizerEvents()
    {
        _audioVisualizer.AudioLevelChanged += OnAudioLevelChanged;
        _audioVisualizer.StateChanged += OnStateChanged;
        _audioVisualizer.ActivityDetectedChanged += OnActivityDetectedChanged;
        _speechStateService.TranscriptionChanged += OnTranscriptionChanged;
        _thermalMonitor.StateChanged += OnThermalStateChanged;
        _ = RefreshHudTelemetryAsync();
    }

    public void DetachVisualizerEvents()
    {
        _audioVisualizer.AudioLevelChanged -= OnAudioLevelChanged;
        _audioVisualizer.StateChanged -= OnStateChanged;
        _audioVisualizer.ActivityDetectedChanged -= OnActivityDetectedChanged;
        _speechStateService.TranscriptionChanged -= OnTranscriptionChanged;
        _thermalMonitor.StateChanged -= OnThermalStateChanged;
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

    private async Task ToggleListeningAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            if (!IsListening)
            {
                if (!await EnsureMicrophonePermissionAsync())
                    return;

                if (_settingsService.UseOnDeviceStt && !_whisperDownloader.ModelExists())
                {
                    var download = await _dialogService.ShowConfirmAsync(
                        "Whisper Model Missing",
                        "The on-device Whisper model is not present. Download it now? (~150 MB)");
                    if (download)
                    {
                        AiStatusText = "Downloading model...";
                        StatusColor = "#FFA500";
                        try
                        {
                            var progress = new Progress<double>(p =>
                            {
                                AiStatusText = $"Downloading model... {p:P0}";
                            });
                            await _whisperDownloader.EnsureModelAsync(progress);
                            await _dialogService.ShowAlertAsync("Download Complete", "Whisper model is ready.");
                        }
                        catch (Exception ex)
                        {
                            await _dialogService.ShowAlertAsync("Download Failed", $"Could not download Whisper model: {ex.Message}");
                            AiStatusText = "Ready";
                            StatusColor = "#9A9A9A";
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                StartMicrophoneService();
                IsListening = true;
                AiStatusText = "Listening...";
                StatusColor = "#FF003C";
                TranscriptionStatusText = "STT STREAM ARMED";
                StreamingTranscriptDisplay = "Listening for speech vector...";
            }
            else
            {
                StopListening();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggle listening failed: {ex}");
            AiStatusText = "Error";
            StatusColor = "#FF0000";
            await _dialogService.ShowAlertAsync("Listening Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StopListening()
    {
        if (!IsListening)
            return;

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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation to settings failed: {ex}");
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation to memory failed: {ex}");
        }
    }

    private async Task SummarizeNowAsync()
    {
        IsBusy = true;
        AiStatusText = "Summarizing...";
        try
        {
            var now = DateTime.Now;
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
            var now = DateTime.Now;
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
        try
        {
            var prompt = _assetService.GetAestheticPrompt(AssetType.AssistantRingPulsing);
            AssistantImageSource = await _assetService.GenerateAssetAsync(prompt, "assistant_ring.png");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to generate assistant ring: {ex.Message}");
            AssistantImageSource = "dotnet_bot.png";
            global::Android.Util.Log.Warn("QuantumZ", $"Asset generation skipped: {ex.Message}");
        }

        await RefreshHudTelemetryAsync();
    }

    private async Task RefreshHudTelemetryAsync()
    {
        try
        {
            var models = await _modelRegistry.DiscoverModelsAsync(forceRefresh: false);
            var llmModel = models.FirstOrDefault(model => model.Capability == ProviderCapability.Llm && model.IsAvailable);
            var remoteCount = models.Count(model => model.Location is ProviderLocation.Remote or ProviderLocation.Hybrid);
            var localCount = models.Count(model => model.Location is ProviderLocation.Local or ProviderLocation.BuiltIn);

            ActiveModelName = FirstNonEmpty(_settingsService.SelectedModelName, _settingsService.LlamaModelId, llmModel?.DisplayName, "No LLM model selected");
            ModelEndpointText = FirstNonEmpty(llmModel?.Endpoint, _settingsService.LlmUrl, "No endpoint configured");
            ServerHealthText = remoteCount > 0 ? "REMOTE SERVER ONLINE" : "LOCAL/OFFLINE MODE";
            GpuStatusText = remoteCount > 0 ? "GPU SERVER ROUTE READY" : "LOCAL ACCELERATION ROUTE";

            ReplaceMetrics(ModelStatusItems,
            [
                new HudMetric("VAD", DescribeEndpoint(_settingsService.VadUrl, "Local RMS"), "#00FF88"),
                new HudMetric("STT", FirstNonEmpty(_settingsService.SttModelId, "Provider default"), "#FF003C"),
                new HudMetric("LLM", ActiveModelName, "#FF335F"),
                new HudMetric("TTS", FirstNonEmpty(_settingsService.TtsModelId, "Provider default"), "#FFB300")
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
                new HudMetric("Remote Models", remoteCount.ToString(), remoteCount > 0 ? "#00FF88" : "#FFB300"),
                new HudMetric("Local Models", localCount.ToString(), localCount > 0 ? "#00FF88" : "#AAAAAA"),
                new HudMetric("Endpoint", ShortenEndpoint(_settingsService.LlmUrl), "#FF003C"),
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
                new HudMetric("LLM", FirstNonEmpty(_settingsService.SelectedModelName, _settingsService.LlamaModelId, "Unknown"), "#FFB300"),
                new HudMetric("TTS", "Configured", "#FFB300")
            ]);
        }
    }

    private static void ReplaceMetrics(ObservableCollection<HudMetric> target, IEnumerable<HudMetric> values)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            target.Clear();
            foreach (var value in values)
            {
                target.Add(value);
            }
        });
    }

    private static string DescribeEndpoint(string endpoint, string fallback) =>
        string.IsNullOrWhiteSpace(endpoint) ? fallback : ShortenEndpoint(endpoint);

    private static string ShortenEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return string.IsNullOrWhiteSpace(endpoint) ? "Not set" : endpoint;

        return string.IsNullOrWhiteSpace(uri.PathAndQuery) || uri.PathAndQuery == "/"
            ? uri.Authority
            : $"{uri.Authority}{uri.PathAndQuery}";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static async ValueTask<bool> EnsureMicrophonePermissionAsync()
    {
        try
        {
            var status = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<Microsoft.Maui.ApplicationModel.Permissions.Microphone>();
            if (status == Microsoft.Maui.ApplicationModel.PermissionStatus.Granted)
                return true;

            status = await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<Microsoft.Maui.ApplicationModel.Permissions.Microphone>();
            if (status == Microsoft.Maui.ApplicationModel.PermissionStatus.Granted)
                return true;

            bool openSettings = await Application.Current!.MainPage!.DisplayAlert(
                "Microphone Permission Required",
                "QuantumZ needs microphone access to listen for voice commands and perform speech-to-text. Would you like to enable it in your device settings?",
                "Open Settings", "Cancel");

            if (openSettings)
            {
                AppInfo.Current.ShowSettingsUI();
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Permission check failed: {ex}");
            return false;
        }
    }

    private static void StartMicrophoneService()
    {
        try
        {
#if ANDROID
            global::Android.Util.Log.Info("QuantumZ", "Starting MicrophoneForegroundService from UI.");
            var context = global::Android.App.Application.Context;
            var intent = new global::Android.Content.Intent(context, typeof(global::QuantumZ.Android.Services.MicrophoneForegroundService));
            intent.SetAction(global::QuantumZ.Android.Services.MicrophoneForegroundService.ActionStartListening);
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start MicrophoneForegroundService: {ex}");
        }
    }

    private static void StopMicrophoneService()
    {
#if ANDROID
        global::Android.Util.Log.Info("QuantumZ", "Stopping MicrophoneForegroundService from UI.");
        var context = global::Android.App.Application.Context;
        var intent = new global::Android.Content.Intent(context, typeof(global::QuantumZ.Android.Services.MicrophoneForegroundService));
        intent.SetAction(global::QuantumZ.Android.Services.MicrophoneForegroundService.ActionStopListening);
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
#endif
    }

    private async Task TestPipelineAsync()
    {
        try
        {
#if ANDROID
            var context = global::Android.App.Application.Context;
            var intent = new global::Android.Content.Intent(context, typeof(global::QuantumZ.Android.Services.MicrophoneForegroundService));
            intent.SetAction(global::QuantumZ.Android.Services.MicrophoneForegroundService.ActionTestUtterance);
            intent.PutExtra("utterance", "hey quantum what is the current time");
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
            AiStatusText = "Test pipeline started...";
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Test pipeline failed: {ex}");
            AiStatusText = "Test failed";
        }

        await Task.CompletedTask;
    }
}

public sealed record HudMetric(string Label, string Value, string AccentColor);
