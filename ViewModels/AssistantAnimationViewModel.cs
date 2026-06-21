using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Models;
using QuantumZ.UI.Pages;

namespace QuantumZ.UI.ViewModels;

/// <summary>
/// Partial class continuation of <see cref="MainAssistantViewModel"/> containing:
/// orb animation properties, pipeline-state-to-visual mapping, display helper methods,
/// platform interaction methods, and shared HUD display types.
/// </summary>
public partial class MainAssistantViewModel
{
    // ── Animation-driven orb properties ─────────────────────────────────────

    private Color _orbColor = Color.FromArgb("#1E3A5F");
    /// <summary>Fill color of the central orb; changes with each pipeline state.</summary>
    public Color OrbColor
    {
        get => _orbColor;
        set => SetProperty(ref _orbColor, value);
    }

    private double _orbScale = 1.0;
    /// <summary>Nominal scale of the orb. The code-behind pulses ±6 % around this value.</summary>
    public double OrbScale
    {
        get => _orbScale;
        set => SetProperty(ref _orbScale, value);
    }

    private double _orbOpacity = 0.6;
    /// <summary>Opacity of the central orb element.</summary>
    public double OrbOpacity
    {
        get => _orbOpacity;
        set => SetProperty(ref _orbOpacity, value);
    }

    private uint _orbPulseSpeed = 1500;
    /// <summary>
    /// Full pulse period in milliseconds. The code-behind uses half this value for each
    /// ScaleTo leg. Setting to 0 stops active pulsing; idle state is also checked explicitly.
    /// </summary>
    public uint OrbPulseSpeed
    {
        get => _orbPulseSpeed;
        set => SetProperty(ref _orbPulseSpeed, value);
    }

    private string _statusText = "Listening...";
    /// <summary>Short pipeline-state label displayed beneath the orb on the main HUD.</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isAnimating;
    /// <summary>True when the orb pulse animation is active (not Idle).</summary>
    public bool IsAnimating
    {
        get => _isAnimating;
        set => SetProperty(ref _isAnimating, value);
    }

    // ── Pipeline-state → visual mapping ─────────────────────────────────────

    /// <summary>
    /// Updates all orb animation properties to match <paramref name="state"/>.
    /// Must be called on the UI thread; <c>SetProperty</c> raises
    /// <see cref="System.ComponentModel.INotifyPropertyChanged"/> synchronously.
    /// </summary>
    internal void ApplyStateAnimation(PipelineState state)
    {
        switch (state)
        {
            case PipelineState.Idle:
                OrbColor = Color.FromArgb("#2A2A3A");
                OrbScale = 1.0;
                OrbOpacity = 0.3;
                OrbPulseSpeed = 3000;
                StatusText = string.Empty;
                IsAnimating = false;
                break;

            case PipelineState.ListeningForTrigger:
                OrbColor = Color.FromArgb("#1E3A8A");
                OrbScale = 1.0;
                OrbOpacity = 0.6;
                OrbPulseSpeed = 1800;
                StatusText = "Listening...";
                IsAnimating = true;
                break;

            case PipelineState.TriggerDetected:
                OrbColor = Color.FromArgb("#FFFFFF");
                OrbScale = 1.25;
                OrbOpacity = 1.0;
                OrbPulseSpeed = 300;
                StatusText = "Heard you!";
                IsAnimating = true;
                // White flash for 400 ms, then settle to RecordingQuery green
                _ = Task.Delay(400).ContinueWith(_ =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (CurrentState == PipelineState.TriggerDetected)
                            OrbColor = Color.FromArgb("#00C853");
                    }));
                break;

            case PipelineState.RecordingQuery:
                OrbColor = Color.FromArgb("#00C853");
                OrbScale = 1.1;
                OrbOpacity = 0.9;
                OrbPulseSpeed = 400;
                StatusText = "Recording...";
                IsAnimating = true;
                break;

            case PipelineState.ProcessingSTT:
                OrbColor = Color.FromArgb("#FF8F00");
                OrbScale = 1.05;
                OrbOpacity = 0.8;
                OrbPulseSpeed = 600;
                StatusText = "Transcribing...";
                IsAnimating = true;
                break;

            case PipelineState.ProcessingLLM:
                OrbColor = Color.FromArgb("#7B1FA2");
                OrbScale = 1.05;
                OrbOpacity = 0.85;
                OrbPulseSpeed = 900;
                StatusText = "Thinking...";
                IsAnimating = true;
                break;

            case PipelineState.Speaking:
                OrbColor = Color.FromArgb("#FF0000");   // Cyber Red — primary theme accent
                OrbScale = 1.15;
                OrbOpacity = 1.0;
                OrbPulseSpeed = 250;
                StatusText = "Speaking...";
                IsAnimating = true;
                break;

            case PipelineState.Error:
                OrbColor = Color.FromArgb("#D50000");
                OrbScale = 1.0;
                OrbOpacity = 0.9;
                OrbPulseSpeed = 200;
                StatusText = "Error — retrying...";
                IsAnimating = true;
                break;
        }
    }

    // ── Display string helpers (moved here to keep MainAssistantViewModel.cs < 1 000 lines) ──

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

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Not set";

        var normalized = path.Trim().Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? normalized : $".../{parts[^2]}/{parts[^1]}";
    }

    private static string TrimForMetric(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "No detail returned" : value.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= 80 ? trimmed : $"{trimmed[..77]}...";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool ModelMatchesSelection(ModelProfile model, string selection) =>
        string.Equals(model.Id, selection, StringComparison.OrdinalIgnoreCase)
        || string.Equals(model.DisplayName, selection, StringComparison.OrdinalIgnoreCase)
        || string.Equals($"{model.Provider}:{model.Id}", selection, StringComparison.OrdinalIgnoreCase);

    // ── Platform interaction methods ─────────────────────────────────────────

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
                AppInfo.Current.ShowSettingsUI();

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Permission check failed: {ex}");
            return false;
        }
    }

    private async ValueTask<bool> EnsureSetupCompletedAsync()
    {
        if (_settingsService.SetupSettings.IsCompleted)
            return true;

        IsListening = false;
        AiStatusText = "Setup Required";
        StatusColor = "#FFB300";
        TranscriptionStatusText = "SETUP REQUIRED";
        StreamingTranscriptDisplay = "Complete QuantumZ setup before enabling listening.";
        _debugLogger.Log("MainAssistant", "Listening blocked because first-run setup is incomplete.", LogLevel.Warning);

        await _dialogService.ShowAlertAsync("Setup Required", "Complete QuantumZ setup before starting the microphone service.");
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = _serviceProvider.GetRequiredService<SetupPage>();
            var navigation = Application.Current?.MainPage?.Navigation;
            if (navigation is not null)
            {
                if (!navigation.NavigationStack.Any(item => item is SetupPage))
                    await navigation.PushAsync(page);
            }
            else if (Application.Current is not null)
            {
                Application.Current.MainPage = new NavigationPage(page);
            }
        });

        return false;
    }

    private void StartMicrophoneService()
    {
        try
        {
#if ANDROID
            _debugLogger.Log("MainAssistant", "Starting MicrophoneForegroundService from UI.");
            var context = global::Android.App.Application.Context;
            var intent = new global::Android.Content.Intent(context, typeof(global::QuantumZ.Android.Services.MicrophoneForegroundService));
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start MicrophoneForegroundService: {ex}");
            _debugLogger.Log("MainAssistant", $"Failed to start microphone service: {ex.Message}", LogLevel.Error);
        }
    }

    private void StopMicrophoneService()
    {
#if ANDROID
        _debugLogger.Log("MainAssistant", "Stopping MicrophoneForegroundService from UI.");
        var context = global::Android.App.Application.Context;
        var intent = new global::Android.Content.Intent(context, typeof(global::QuantumZ.Android.Services.MicrophoneForegroundService));
        context.StopService(intent);
#endif
    }

}

// ── Shared HUD display types ─────────────────────────────────────────────────
// Defined at namespace level so they are accessible from XAML DataTemplates
// and from both partial class files without circular dependencies.

/// <summary>A single key-value metric displayed in the HUD panels.</summary>
public sealed record HudMetric(string Label, string Value, string AccentColor);

/// <summary>Completion states for a service health check.</summary>
public enum ServiceHealthState
{
    Unknown,
    Checking,
    Unconfigured,
    Partial,
    Ready,
    Failed
}

/// <summary>Result of a single service smoke test, with factory helpers for each state.</summary>
public sealed record ServiceHealthMetric(string Label, string Value, string AccentColor, ServiceHealthState State)
{
    /// <summary>True while the check is still in progress or partially satisfied.</summary>
    public bool IsPulsing => State is ServiceHealthState.Checking or ServiceHealthState.Partial;

    public static ServiceHealthMetric Checking(string label, string value) =>
        new(label, value, "#FFB300", ServiceHealthState.Checking);

    public static ServiceHealthMetric Unconfigured(string label, string value) =>
        new(label, value, "#9A9A9A", ServiceHealthState.Unconfigured);

    public static ServiceHealthMetric Partial(string label, string value) =>
        new(label, value, "#FFB300", ServiceHealthState.Partial);

    public static ServiceHealthMetric Ready(string label, string value) =>
        new(label, value, "#00FF88", ServiceHealthState.Ready);

    public static ServiceHealthMetric Failed(string label, string value) =>
        new(label, value, "#FF003C", ServiceHealthState.Failed);
}
