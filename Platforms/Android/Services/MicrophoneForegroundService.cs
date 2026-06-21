using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Interfaces;
using DebugLogLevel = QuantumZ.Core.Models.LogLevel;

namespace QuantumZ.Android.Services;

/// <summary>
/// Android Foreground Service (microphone type) that owns the service lifecycle.
/// All pipeline logic — audio capture, wake word, VAD, STT, LLM, TTS — is delegated
/// to <see cref="MicrophonePipelineController"/>.
/// </summary>
[Service(ForegroundServiceType = ForegroundService.TypeMicrophone)]
public class MicrophoneForegroundService : Service
{
    private const string ChannelId = "quantumz_audio_channel";
    private const int NotificationId = 1001;

    private MicrophonePipelineController? _controller;
    private CancellationTokenSource? _cts;
    private IDebugLogger? _logger;
    private IDialogService? _dialogService;

    // ── Service lifecycle ─────────────────────────────────────────────────────

    public override void OnCreate()
    {
        base.OnCreate();
        global::Android.Util.Log.Info("QuantumZ", "MicrophoneForegroundService OnCreate");

        var services = global::QuantumZ.UI.MainApplication.Services;
        _logger = services?.GetService<IDebugLogger>();
        _dialogService = services?.GetService<IDialogService>();
        _logger?.Log("MicService", "OnCreate complete");
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        global::Android.Util.Log.Info("QuantumZ", $"MicrophoneForegroundService OnStartCommand action={intent?.Action}");
        _logger?.Log("MicService", $"OnStartCommand: action={intent?.Action}");

        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification());

        if (_cts is not null && !_cts.IsCancellationRequested)
        {
            _logger?.Log("MicService", "Pipeline already running; ignoring duplicate start request");
            return StartCommandResult.Sticky;
        }

        _ = Task.Run(StartPipelineAsync);
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _logger?.Log("MicService", "OnDestroy — stopping pipeline");
        
        // Stop the pipeline synchronously (cancels capture loop, releases AudioRecord)
        _controller?.StopAsync();

        if (_controller is not null)
        {
            // Synchronously wait for async disposal with timeout to ensure wake word provider
            // (ONNX session, NPU handles) is fully released before service destruction.
            // Timeout prevents indefinite blocking if disposal hangs.
            try
            {
                var disposeTask = _controller.DisposeAsync().AsTask();
                if (disposeTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger?.Log("MicService", "Controller disposed successfully");
                }
                else
                {
                    _logger?.Log("MicService", "Controller disposal timed out after 5 seconds", DebugLogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger?.Log("MicService", $"DisposeAsync error: {ex.Message}", DebugLogLevel.Warning);
            }
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _controller = null;

        base.OnDestroy();
        global::Android.Util.Log.Info("QuantumZ", "MicrophoneForegroundService destroyed");
    }

    public override IBinder? OnBind(Intent? intent) => null;

    // ── Pipeline bootstrap ────────────────────────────────────────────────────

    private async Task StartPipelineAsync()
    {
        try
        {
            var services = global::QuantumZ.UI.MainApplication.Services
                ?? throw new InvalidOperationException("MAUI service provider unavailable.");

            _cts = new CancellationTokenSource();
            _controller = services.GetRequiredService<MicrophonePipelineController>();

            _logger?.Log("MicService", "Delegating to MicrophonePipelineController.StartAsync");
            await _controller.StartAsync(_cts.Token);
        }
        catch (System.OperationCanceledException)
        {
            _logger?.Log("MicService", "Pipeline start cancelled");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("QuantumZ", $"MicrophoneForegroundService StartPipelineAsync error: {ex}");
            _logger?.Log("MicService", $"StartPipelineAsync error: {ex.Message}", DebugLogLevel.Error);
            _dialogService?.ShowAlertAsync("Service Error", $"Failed to start voice pipeline: {ex.Message}");
        }
    }

    // ── Notification helpers ──────────────────────────────────────────────────

    private Notification BuildNotification() =>
        new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("QuantumZ Active")
            .SetContentText("Listening for wake-word...")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetOngoing(true)
            .Build()!;

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(ChannelId, "QuantumZ Audio Service", NotificationImportance.Low)
        {
            Description = "Persistent microphone channel for wake-word detection"
        };
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }
}
