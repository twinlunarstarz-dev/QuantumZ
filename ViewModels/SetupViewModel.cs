using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Core.Models.Settings;
using QuantumZ.UI.Pages;

namespace QuantumZ.UI.ViewModels;

public sealed class SetupViewModel(ISettingsService settingsService, ILocalSetupService localSetupService, IDebugLogger logger) : BaseViewModel
{
    private const string RemoteProviderName = "Remote Setup";

    public ObservableCollection<SetupChecklistItem> LocalChecklist { get; } = [];

    private string _remoteBaseUrl = "http://localhost:8025";
    public string RemoteBaseUrl
    {
        get => _remoteBaseUrl;
        set => SetProperty(ref _remoteBaseUrl, value);
    }

    private string _remoteModelId = string.Empty;
    public string RemoteModelId
    {
        get => _remoteModelId;
        set => SetProperty(ref _remoteModelId, value);
    }

    private string _remoteApiKey = string.Empty;
    public string RemoteApiKey
    {
        get => _remoteApiKey;
        set => SetProperty(ref _remoteApiKey, value);
    }

    private string _huggingFaceToken = string.Empty;
    public string HuggingFaceToken
    {
        get => _huggingFaceToken;
        set => SetProperty(ref _huggingFaceToken, value);
    }

    private string _statusMessage = "Select a setup mode to continue.";
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

    private ICommand? _useRemoteCommand;
    public ICommand UseRemoteCommand => _useRemoteCommand ??= new AsyncRelayCommand(UseRemoteAsync);

    private ICommand? _useOnDeviceCommand;
    public ICommand UseOnDeviceCommand => _useOnDeviceCommand ??= new AsyncRelayCommand(UseOnDeviceAsync);

    private ICommand? _refreshLocalChecklistCommand;
    public ICommand RefreshLocalChecklistCommand => _refreshLocalChecklistCommand ??= new AsyncRelayCommand(RefreshLocalChecklistAsync);

    private ICommand? _installLocalItemCommand;
    public ICommand InstallLocalItemCommand => _installLocalItemCommand ??= new AsyncRelayCommand<string>(InstallLocalItemAsync);

    private ICommand? _openSettingsCommand;
    public ICommand OpenSettingsCommand => _openSettingsCommand ??= new AsyncRelayCommand(OpenSettingsAsync);

    private async Task UseRemoteAsync()
    {
        if (IsBusy)
            return;

        var endpoint = RemoteBaseUrl.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            StatusMessage = "Enter a remote server URL before continuing.";
            return;
        }

        IsBusy = true;
        try
        {
            var apiKey = string.IsNullOrWhiteSpace(RemoteApiKey) ? null : RemoteApiKey.Trim();
            var llmModelId = string.IsNullOrWhiteSpace(RemoteModelId) ? null : RemoteModelId.Trim();
            var currentPipeline = settingsService.PipelineSettings;

            settingsService.LlmSettings = BuildProviderSettings(endpoint, apiKey, llmModelId);
            settingsService.SttSettings = BuildProviderSettings(endpoint, apiKey, settingsService.GetActiveProvider("STT")?.ModelId);
            settingsService.TtsSettings = BuildProviderSettings(endpoint, apiKey, settingsService.GetActiveProvider("TTS")?.ModelId);
            settingsService.UseOnDeviceStt = false;
            settingsService.UseLocalTts = false;
            settingsService.PipelineSettings = currentPipeline with
            {
                Llm = BuildRemoteStage(endpoint, apiKey, llmModelId),
                Stt = BuildRemoteStage(endpoint, apiKey, settingsService.GetActiveProvider("STT")?.ModelId),
                Tts = BuildRemoteStage(endpoint, apiKey, settingsService.GetActiveProvider("TTS")?.ModelId),
            };
            settingsService.SetupSettings = new SetupSettings
            {
                IsCompleted = true,
                Mode = SetupMode.Remote,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                SelectedLlmModelId = llmModelId,
                SelectedSttModelId = settingsService.GetActiveProvider("STT")?.ModelId,
                SelectedVadModelId = settingsService.GetActiveProvider("VAD")?.ModelId,
                SelectedTtsModelId = settingsService.GetActiveProvider("TTS")?.ModelId,
                LocalAssetsVerified = false,
            };

            StatusMessage = "Remote setup saved. Opening assistant...";
            logger.Log("Setup", $"Remote setup completed for endpoint {endpoint}.", LogLevel.Info);
            await ReplaceRootWithMainAssistantAsync();
        }
        catch (Exception ex)
        {
            logger.Log("Setup", $"Remote setup failed: {ex.Message}", LogLevel.Error);
            settingsService.SetupSettings = settingsService.SetupSettings with { LastSetupError = ex.Message };
            StatusMessage = $"Remote setup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UseOnDeviceAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        var setup = settingsService.SetupSettings;
        try
        {
            EnsureBuiltInLocalFallbacksSelected();
            await RefreshLocalChecklistCoreAsync();
            if (await localSetupService.AreRequiredAssetsReadyAsync())
            {
                settingsService.SetupSettings = setup with
                {
                    IsCompleted = true,
                    Mode = SetupMode.OnDevice,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    SelectedLlmModelId = settingsService.GetActiveProvider("LLM")?.ModelId,
                    SelectedSttModelId = settingsService.GetActiveProvider("STT")?.ModelId,
                    SelectedVadModelId = settingsService.GetActiveProvider("VAD")?.ModelId,
                    SelectedTtsModelId = settingsService.GetActiveProvider("TTS")?.ModelId,
                    LocalAssetsVerified = true,
                    LastSetupError = null,
                };
                StatusMessage = "Required local assets and packaged runtimes are verified. Opening assistant...";
                await ReplaceRootWithMainAssistantAsync();
                return;
            }

            const string message = "Required local assets or packaged native runtimes are missing. Install checklist items, select built-in fallbacks where available, or use Remote mode.";
            StatusMessage = message;
            settingsService.SetupSettings = setup with
            {
                IsCompleted = false,
                Mode = SetupMode.Unset,
                LocalAssetsVerified = false,
                LastSetupError = message,
            };
            logger.Log("Setup", "On-device setup refused because required local assets or runtimes are missing.", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            logger.Log("Setup", $"On-device readiness check failed: {ex.Message}", LogLevel.Error);
            settingsService.SetupSettings = setup with { IsCompleted = false, LocalAssetsVerified = false, LastSetupError = ex.Message };
            StatusMessage = $"On-device readiness check failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshLocalChecklistAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await RefreshLocalChecklistCoreAsync();
            StatusMessage = "Local setup checklist refreshed. On-device mode completes only after required assets and packaged runtimes are verified.";
        }
        catch (Exception ex)
        {
            logger.Log("Setup", $"Local checklist refresh failed: {ex.Message}", LogLevel.Error);
            StatusMessage = $"Local checklist refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallLocalItemAsync(string? itemId)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(itemId))
            return;

        IsBusy = true;
        try
        {
            var normalizedItemId = itemId.Trim();
            UpdateChecklistItem(normalizedItemId, item => item with
            {
                Status = ModelInstallStatus.Downloading,
                StatusText = "Installing...",
                Progress = 0.0d
            });

            var progress = new Progress<double>(value =>
                UpdateChecklistItem(normalizedItemId, item => item with
                {
                    Status = ModelInstallStatus.Downloading,
                    StatusText = $"Downloading {value:P0}...",
                    Progress = value
                }));

            var token = string.IsNullOrWhiteSpace(HuggingFaceToken) ? null : HuggingFaceToken.Trim();
            var result = await localSetupService.InstallAsync(normalizedItemId, token, progress);
            StatusMessage = result.Message;

            await RefreshLocalChecklistCoreAsync();
            if (!result.Success)
            {
                UpdateChecklistItem(normalizedItemId, item => item with
                {
                    Status = result.Status,
                    StatusText = result.Message,
                    Progress = result.Status == ModelInstallStatus.RequiresRuntime ? 1.0d : item.Progress
                });
            }
        }
        catch (Exception ex)
        {
            logger.Log("Setup", $"Local item install failed: {ex.Message}", LogLevel.Error);
            StatusMessage = $"Local item install failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshLocalChecklistCoreAsync()
    {
        var items = await localSetupService.GetChecklistAsync();
        LocalChecklist.Clear();
        foreach (var item in items)
            LocalChecklist.Add(item);
    }

    private void EnsureBuiltInLocalFallbacksSelected()
    {
        var pipeline = settingsService.PipelineSettings;
        settingsService.VadSettings = BuildFallbackProviderSettings("Built-In RMS VAD", "rms-vad-built-in");
        settingsService.TtsSettings = BuildFallbackProviderSettings("Android Built-In TTS", "builtin.android-tts");
        settingsService.UseLocalTts = false;
        settingsService.PipelineSettings = pipeline with
        {
            Vad = new StageSettings { Enabled = true, Mode = ModelMode.BuiltIn },
            Tts = new StageSettings { Enabled = true, Mode = ModelMode.BuiltIn }
        };
    }

    private void UpdateChecklistItem(string itemId, Func<SetupChecklistItem, SetupChecklistItem> update)
    {
        for (var i = 0; i < LocalChecklist.Count; i++)
        {
            if (!string.Equals(LocalChecklist[i].Id, itemId, StringComparison.OrdinalIgnoreCase))
                continue;

            LocalChecklist[i] = update(LocalChecklist[i]);
            return;
        }
    }

    private async Task OpenSettingsAsync()
    {
        try
        {
            var page = ResolvePage<SettingsPage>();
            var navigation = Application.Current?.MainPage?.Navigation;
            if (navigation is not null)
                await navigation.PushAsync(page);
            else if (Application.Current is not null)
                Application.Current.MainPage = new NavigationPage(page);
        }
        catch (Exception ex)
        {
            logger.Log("Setup", $"Navigation to settings failed: {ex.Message}", LogLevel.Error);
            StatusMessage = "Unable to open settings.";
        }
    }

    private static ServiceProviderSettings BuildProviderSettings(string endpoint, string? apiKey, string? modelId) => new(
        ActiveProviderName: RemoteProviderName,
        Providers: [new ProviderConfig(RemoteProviderName, endpoint, string.IsNullOrWhiteSpace(modelId) ? null : modelId, BuildParameters(apiKey))]);

    private static ServiceProviderSettings BuildFallbackProviderSettings(string providerName, string modelId) => new(
        ActiveProviderName: providerName,
        Providers: [new ProviderConfig(providerName, string.Empty, modelId)]);

    private static StageSettings BuildRemoteStage(string endpoint, string? apiKey, string? modelId) => new()
    {
        Enabled = true,
        Mode = ModelMode.Remote,
        Remote = new RemoteEndpointConfig
        {
            Url = endpoint,
            ApiKey = apiKey,
            ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId,
            TimeoutSeconds = 30,
        },
    };

    private static Dictionary<string, string> BuildParameters(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? [] : new Dictionary<string, string> { ["ApiKey"] = apiKey };

    private static TPage ResolvePage<TPage>() where TPage : Page
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        return services?.GetRequiredService<TPage>()
            ?? throw new InvalidOperationException("MAUI service provider is not available.");
    }

    private static Task ReplaceRootWithMainAssistantAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var page = ResolvePage<MainAssistantPage>();
            if (Application.Current is null)
                throw new InvalidOperationException("Application is not available.");

            Application.Current.MainPage = new NavigationPage(page);
        });
    }
}
