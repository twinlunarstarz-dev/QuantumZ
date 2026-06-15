using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using DebugLogLevel = QuantumZ.Core.Models.LogLevel;
using QuantumZ.Infrastructure.Services;
using QuantumZ.Android.Audio;

namespace QuantumZ.Android.Services;

[Service(ForegroundServiceType = ForegroundService.TypeMicrophone)]
public class MicrophoneForegroundService : Service
{
    private const string ChannelId = "quantumz_audio_channel";
    private const int NotificationId = 1001;
    private const int SampleRate = 16000;
    private const int BytesPerSample = 2;
    private const int SilenceThresholdMs = 1200;
    private const int InterimTranscriptionIntervalMs = 2500;
    private const int MinInterimTranscriptionMs = 1600;

    private ITtsEngine? _ttsEngine;
    private IProviderRouter? _providerRouter;
    private AIIntegrationService? _aiIntegration;
    private IActivityLogger? _activityLogger;
    private ISettingsService? _settings;
    private IMemoryService? _memoryService;
    private IAudioVisualizer? _audioVisualizer;
    private IDialogService? _dialogService;
    private ISpeechStateService? _speechState;
    private IDebugLogger? _debugLogger;
    private AudioRoutingManager? _routingManager;
    private OnDeviceSpeechRecognizer? _nativeRecognizer;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private int _interimTranscriptionActive;

    public override void OnCreate()
    {
        base.OnCreate();
        global::Android.Util.Log.Info("QuantumZ", "MicrophoneForegroundService OnCreate.");
        _debugLogger?.Log("MicService", "OnCreate called");
        InitializeDependencies();
    }

    private void InitializeDependencies()
    {
        try
        {
            var services = global::QuantumZ.UI.MainApplication.Services
                ?? throw new InvalidOperationException("MAUI service provider unavailable.");

            _settings = services.GetRequiredService<ISettingsService>();
            _aiIntegration = services.GetRequiredService<AIIntegrationService>();
            _activityLogger = services.GetRequiredService<IActivityLogger>();
            _memoryService = services.GetRequiredService<IMemoryService>();
            _audioVisualizer = services.GetService<IAudioVisualizer>();
            _ttsEngine = services.GetRequiredService<ITtsEngine>();
            _providerRouter = services.GetRequiredService<IProviderRouter>();
            _dialogService = services.GetService<IDialogService>();
            _speechState = services.GetService<ISpeechStateService>();
            _debugLogger = services.GetService<IDebugLogger>();

            if (_settings.UseOnDeviceStt)
            {
                _nativeRecognizer = new OnDeviceSpeechRecognizer(this, OnNativeResult);
            }
            _routingManager = new AudioRoutingManager(this, _settings);
            _routingManager.Initialize();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("QuantumZ", $"Service dependency init failed: {ex}");
            _dialogService?.ShowAlertAsync("Service Error", $"Failed to initialize microphone service: {ex.Message}");
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        global::Android.Util.Log.Info("QuantumZ", "MicrophoneForegroundService OnStartCommand.");
        _debugLogger?.Log("MicService", "OnStartCommand called");
        CreateNotificationChannel();

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("QuantumZ Active")
            .SetContentText("Listening for wake-word and logging activity...")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetOngoing(true)
            .Build();

        StartForeground(NotificationId, notification);

        if (intent?.Action == "com.quantumz.assistant.TEST_UTTERANCE")
        {
            var utterance = intent.GetStringExtra("utterance");
            if (!string.IsNullOrEmpty(utterance))
            {
                _cts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessTranscriptAsync(utterance, _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        global::Android.Util.Log.Error("QuantumZ", $"Test utterance error: {ex}");
                        _dialogService?.ShowAlertAsync("Pipeline Error", $"Test pipeline failed: {ex.Message}");
                    }
                });
                return StartCommandResult.Sticky;
            }
        }

        if (_aiIntegration == null || _activityLogger == null || _settings == null)
        {
            global::Android.Util.Log.Error("QuantumZ", "Critical dependencies missing; stopping service.");
            _dialogService?.ShowAlertAsync("Service Error", "AI services are not configured correctly. Please check settings and restart the app.");
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        if (_settings.UseOnDeviceStt)
            StartNativeSpeechRecognizer();
        else
            StartAudioCaptureLoop();

        return StartCommandResult.Sticky;
    }

    private void StartNativeSpeechRecognizer()
    {
        if (_nativeRecognizer == null)
        {
            global::Android.Util.Log.Error("QuantumZ", "Native recognizer not initialized.");
            return;
        }
        _cts = new CancellationTokenSource();
        _nativeRecognizer.StartListening();
        _audioVisualizer?.ReportState(ListeningState.Listening);
        global::Android.Util.Log.Info("QuantumZ", "Native speech recognizer started.");
        _debugLogger?.Log("MicService", "Native speech recognizer started");
    }

    private void OnNativeResult(string text)
    {
        if (_cts?.IsCancellationRequested != false) return;
        _ = ProcessUtteranceTextAsync(text, _cts.Token);
    }

    private void StartAudioCaptureLoop()
    {
        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(async () =>
        {
            AudioRecord? recorder = null;
            try
            {
                if (_providerRouter is null || _aiIntegration is null || _activityLogger is null || _settings is null)
                {
                    global::Android.Util.Log.Error("QuantumZ", "Service dependencies not initialized.");
                    return;
                }

                var vadProvider = await _providerRouter.ResolveVadProviderAsync(_cts.Token);
                vadProvider.ActivityChanged += OnVadActivityChanged;

                var sttProvider = await _providerRouter.ResolveSttProviderAsync(_cts.Token);
                await sttProvider.InitializeAsync(_cts.Token);
                _debugLogger?.Log("MicService", $"Audio pipeline providers selected: VAD={vadProvider.Descriptor.DisplayName}, STT={sttProvider.Descriptor.DisplayName}");

                var minBuffer = AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);
                if (minBuffer <= 0)
                {
                    global::Android.Util.Log.Error("QuantumZ", $"Invalid AudioRecord buffer size: {minBuffer}");
                    return;
                }

                var readBufferSize = Math.Max(minBuffer, SampleRate * BytesPerSample / 2);
                recorder = new AudioRecord(AudioSource.VoiceRecognition, SampleRate, ChannelIn.Mono, Encoding.Pcm16bit, readBufferSize * 2);

                if (recorder.State != State.Initialized)
                {
                    global::Android.Util.Log.Error("QuantumZ", "AudioRecord failed to initialize.");
                    return;
                }

                recorder.StartRecording();
                global::Android.Util.Log.Info("QuantumZ", "Audio capture loop started.");
                _debugLogger?.Log("MicService", "Audio capture loop started");
                _audioVisualizer?.ReportState(ListeningState.Listening);

                var rollingBuffer = new MemoryStream();
                var silenceSamples = 0;
                var isSpeaking = false;
                var buffer = new byte[readBufferSize];
                var lastInterimTranscription = DateTimeOffset.MinValue;

                while (!_cts.Token.IsCancellationRequested)
                {
                    var read = recorder.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        await Task.Delay(50, _cts.Token);
                        continue;
                    }

                    // Recording Verification: Ensure we are actually receiving data (not just zeros/silence from a dead mic)
                    bool isCapturingData = false;
                    for (int i = 0; i < read; i++) { if (buffer[i] != 0) { isCapturingData = true; break; } }

                    if (!isCapturingData)
                    {
                        _debugLogger?.Log("MicService", "Recording verification: Buffer contains only zeros.");
                        // We don't necessarily stop, but we can track if this persists.
                    }
                    var vadResult = await vadProvider.DetectSpeechAsync(buffer.AsMemory(0, read), SampleRate, _cts.Token);
                    var hasVoice = vadResult.IsSpeechDetected;
                    _audioVisualizer?.ReportAudioLevel(vadResult.Confidence);

                    // Log RMS occasionally to avoid flooding but provide visibility into levels
                    if (DateTime.Now.Millisecond % 500 < 20) // Roughly every 500ms if loop is fast
                    {
                        _debugLogger?.Log("MicService", $"VAD RMS: {vadResult.Rms:F4}, confidence: {vadResult.Confidence:F2}, state: {vadResult.ActivityState}");
                    }

                    if (hasVoice)
                    {
                        if (!isSpeaking)
                        {
                            isSpeaking = true;
                            _debugLogger?.Log("MicService", $"VAD activity started (RMS: {vadResult.Rms:F4}, confidence: {vadResult.Confidence:F2})");
                            rollingBuffer.SetLength(0);
                            silenceSamples = 0;
                            _speechState?.ClearTranscription();
                            _audioVisualizer?.ReportState(ListeningState.Listening);
                            lastInterimTranscription = DateTimeOffset.UtcNow;
                        }
                        rollingBuffer.Write(buffer, 0, read);
                        silenceSamples = 0;

                        var utteranceMs = rollingBuffer.Length * 1000.0 / (SampleRate * BytesPerSample);
                        if (utteranceMs >= MinInterimTranscriptionMs &&
                            DateTimeOffset.UtcNow - lastInterimTranscription >= TimeSpan.FromMilliseconds(InterimTranscriptionIntervalMs))
                        {
                            lastInterimTranscription = DateTimeOffset.UtcNow;
                            TryStartInterimTranscription(rollingBuffer.ToArray(), _cts.Token);
                        }
                    }
                    else if (isSpeaking)
                    {
                        rollingBuffer.Write(buffer, 0, read);
                        silenceSamples += read / BytesPerSample;
                        var silenceMs = silenceSamples * 1000.0 / SampleRate;

                        if (silenceMs >= SilenceThresholdMs)
                        {
                            isSpeaking = false;
                            _debugLogger?.Log("MicService", $"VAD activity ended after {silenceMs:F0}ms silence; processing utterance");
                            var utteranceAudio = rollingBuffer.ToArray();
                            rollingBuffer.SetLength(0);
                            silenceSamples = 0;

                            _ = ProcessUtteranceAsync(utteranceAudio, _cts.Token);
                        }
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
                global::Android.Util.Log.Info("QuantumZ", "Audio capture loop cancelled.");
                _debugLogger?.Log("MicService", "Capture loop cancelled");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("QuantumZ", $"Capture loop error: {ex}");
                _debugLogger?.Log("MicService", $"Capture loop error: {ex.Message}", DebugLogLevel.Error);
            }
            finally
            {
                if (_providerRouter is not null)
                {
                    try
                    {
                        var vadProvider = await _providerRouter.ResolveVadProviderAsync(CancellationToken.None);
                        vadProvider.ActivityChanged -= OnVadActivityChanged;
                    }
                    catch
                    {
                        // Ignore cleanup failures during service shutdown.
                    }
                }

                recorder?.Stop();
                recorder?.Release();
                recorder?.Dispose();
                _audioVisualizer?.ReportState(ListeningState.Idle);
            }
        }, _cts.Token);
    }

    private async Task ProcessUtteranceAsync(byte[] audio, CancellationToken ct)
    {
        try
        {
            _audioVisualizer?.ReportState(ListeningState.Processing);
            _debugLogger?.Log("MicService", $"STT transcription started ({audio.Length} bytes)");
            var progress = new Progress<string>(partialText =>
            {
                if (!string.IsNullOrWhiteSpace(partialText))
                    _speechState?.UpdateTranscription(partialText);
            });
            var text = await _providerRouter!.TranscribeAsync(audio, progress, ct);
            _debugLogger?.Log("MicService", $"STT transcription result: {text}");
            if (!string.IsNullOrWhiteSpace(text))
            {
                global::Android.Util.Log.Info("QuantumZ", $"STT: {text}");
                _speechState?.UpdateTranscription(text);
                await ProcessTranscriptAsync(text, ct);
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("QuantumZ", $"ProcessUtterance error: {ex}");
        }
        finally
        {
            _audioVisualizer?.ReportState(ListeningState.Listening);
        }
    }

    private void TryStartInterimTranscription(byte[] audio, CancellationToken ct)
    {
        if (_providerRouter is null || audio.Length == 0)
            return;

        if (Interlocked.CompareExchange(ref _interimTranscriptionActive, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<string>(partialText =>
                {
                    if (!string.IsNullOrWhiteSpace(partialText))
                        _speechState?.UpdateTranscription(partialText);
                });

                var text = await _providerRouter.TranscribeAsync(audio, progress, ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _debugLogger?.Log("MicService", $"Interim STT update: {text}", DebugLogLevel.Trace);
                    _speechState?.UpdateTranscription(text);
                }
            }
            catch (System.OperationCanceledException)
            {
                // Service is stopping or a newer utterance is taking over.
            }
            catch (Exception ex)
            {
                _debugLogger?.Log("MicService", $"Interim STT failed: {ex.Message}", DebugLogLevel.Warning);
            }
            finally
            {
                Interlocked.Exchange(ref _interimTranscriptionActive, 0);
            }
        }, ct);
    }

    private async Task ProcessUtteranceTextAsync(string text, CancellationToken ct)
    {
        try
        {
            _audioVisualizer?.ReportState(ListeningState.Processing);
            if (!string.IsNullOrWhiteSpace(text))
            {
                global::Android.Util.Log.Info("QuantumZ", $"Native STT: {text}");
                _debugLogger?.Log("MicService", $"Native STT transcription result: {text}");
                _speechState?.UpdateTranscription(text);
                await ProcessTranscriptAsync(text, ct);
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("QuantumZ", $"ProcessUtteranceText error: {ex}");
        }
        finally
        {
            _audioVisualizer?.ReportState(ListeningState.Listening);
        }
    }

    private async Task ProcessTranscriptAsync(string text, CancellationToken ct)
    {
        var wakeWords = _settings!.WakeWords;
        var containsWake = wakeWords.Any(w => !string.IsNullOrWhiteSpace(w) && text.Contains(w, StringComparison.OrdinalIgnoreCase));

        if (containsWake)
        {
            var matchedWakeWords = wakeWords
                .Where(w => !string.IsNullOrWhiteSpace(w) && text.Contains(w, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _debugLogger?.Log("MicService", $"Wake word match: {string.Join(", ", matchedWakeWords)}");

            // Trigger Word Detected: Play the validation beep only after a wake word is matched.
            PlayValidationBeep();

            var prompt = StripWakeWord(text, wakeWords);
            if (string.IsNullOrWhiteSpace(prompt))
                prompt = "Respond briefly that QuantumZ is online and ready.";

            _debugLogger?.Log("MicService", $"LLM request: {prompt}");

            await _activityLogger!.LogFragmentAsync($"[WAKE] {text}", "user-command");

            try
            {
                var aiResponseContent = await _aiIntegration!.ExecutePromptAsync(new AiRequest(prompt, MaxTokens: 512), ct);
                var aiResponse = new AiResponse(aiResponseContent, null, null, 0); // Simplified for TTS compatibility
                global::Android.Util.Log.Info("QuantumZ", $"LLM: {aiResponse.Content}");
                _debugLogger?.Log("MicService", $"LLM response: {aiResponse.Content}");
                await _activityLogger.LogFragmentAsync($"[RESPONSE] {aiResponse.Content}", "assistant-response");

                _debugLogger?.Log("MicService", "TTS synthesis requested");
                var ttsAudio = await _ttsEngine!.SynthesizeAsync(aiResponse.Content, ct);
                _debugLogger?.Log("MicService", $"TTS synthesis completed ({ttsAudio.Length} bytes)");
                await PlayTtsAudioAsync(ttsAudio, ct);
            }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error("QuantumZ", $"AI Pipeline failed: {ex}");
                    _debugLogger?.Log("MicService", $"AI Pipeline Error: {ex.Message}", DebugLogLevel.Error);
                    _dialogService?.ShowAlertAsync("Assistant Error", $"An error occurred while processing your request: {ex.Message}");
                }
            }
        else
        {
            _debugLogger?.Log("MicService", "Wake word not matched; treating transcript as ambient activity");
            if (_settings.EnableActivityLogging)
            {
                await _activityLogger!.LogFragmentAsync(text, "ambient");
            }
        }
    }

    private void OnVadActivityChanged(object? sender, VadActivityEventArgs args)
    {
        switch (args.State)
        {
            case VadActivityState.SpeechStarted:
                _audioVisualizer?.ReportActivityDetected(true);
                break;
            case VadActivityState.SpeechEnded:
                _audioVisualizer?.ReportActivityDetected(false);
                break;
        }
    }

    private static string StripWakeWord(string text, IEnumerable<string> wakeWords)
    {
        var prompt = text;
        foreach (var word in wakeWords.Where(w => !string.IsNullOrWhiteSpace(w)))
        {
            prompt = prompt.Replace(word, string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        return prompt.Trim([' ', ',', '.', ':', ';', '-', '\'', '"']);
    }

    private async ValueTask PlayTtsAudioAsync(byte[] audioData, CancellationToken ct)
    {
        _audioVisualizer?.ReportState(ListeningState.Speaking);
        string? filePath = null;
        try
        {
            if (audioData.Length == 0)
            {
                // Native TTS engine already played audio directly; no file playback needed.
                global::Android.Util.Log.Info("QuantumZ", "PlayTtsAudioAsync: native TTS path (empty audio), waiting 500ms.");
                _debugLogger?.Log("MicService", "TTS Playback started via native engine");
                await Task.Delay(500, ct);
                _debugLogger?.Log("MicService", "TTS Playback completed via native engine");
                return;
            }

            var cacheDir = CacheDir?.AbsolutePath;
            if (string.IsNullOrEmpty(cacheDir))
                cacheDir = FileSystem.CacheDirectory;

            filePath = Path.Combine(cacheDir, $"quantumz-tts-{Guid.NewGuid():N}.wav");
            await File.WriteAllBytesAsync(filePath, audioData, ct);

            using var player = new MediaPlayer();
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            player.Completion += (_, _) => tcs.TrySetResult();
            player.Error += (_, args) =>
            {
                global::Android.Util.Log.Error("QuantumZ", $"MediaPlayer error: What={args.What}, Extra={args.Extra}");
                tcs.TrySetException(new InvalidOperationException($"MediaPlayer error: {args.What}/{args.Extra}"));
                args.Handled = true;
            };

            player.SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.AssistanceAccessibility)
                .SetContentType(AudioContentType.Speech)
                .Build());
            player.SetDataSource(filePath);
            player.Prepare();
            _routingManager?.ApplyRoutingPreference();
            player.Start();
            _debugLogger?.Log("MicService", "TTS Playback started");

            await tcs.Task.WaitAsync(ct);
            _debugLogger?.Log("MicService", "TTS Playback completed");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("QuantumZ", $"TTS playback failed: {ex}");
            _debugLogger?.Log("MicService", $"TTS Playback Error: {ex.Message}", DebugLogLevel.Error);
        }
        finally
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                try { File.Delete(filePath); } catch { /* ignored */ }
            }
            _audioVisualizer?.ReportState(ListeningState.Listening);
        }
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var channel = new NotificationChannel(ChannelId, "QuantumZ Audio Service", NotificationImportance.Low);
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }

    private void PlayValidationBeep()
    {
        try
        {
            var notificationUri = global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Notification);
            if (notificationUri == null) return;

            using var player = new MediaPlayer();
            player.SetDataSource(this, notificationUri);
            player.Prepare();
            player.Start();
            _debugLogger?.Log("MicService", "Played wake-word validation beep");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("QuantumZ", $"Failed to play validation beep: {ex}");
        }
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        _nativeRecognizer?.Destroy();
        _routingManager?.Dispose();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;
}
