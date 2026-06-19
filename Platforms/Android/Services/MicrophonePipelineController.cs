using Android.Media;
using QuantumZ.Android.Audio;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Core.Models.Settings;
using DebugLogLevel = QuantumZ.Core.Models.LogLevel;

namespace QuantumZ.Android.Services;

/// <summary>
/// Owns the complete always-active voice assistant pipeline:
/// ring buffer → wake word (ONNX) → VAD end-of-speech → STT → LLM → TTS.
/// Instantiated and lifetime-managed by <see cref="MicrophoneForegroundService"/>.
/// </summary>
internal sealed class MicrophonePipelineController : IAsyncDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────
    /// <summary>80 ms at 16 kHz — matches the wake word model's expected chunk size.</summary>
    private const int ChunkSamples = 1280;
    /// <summary>Bytes per PCM16 chunk (2 bytes per sample).</summary>
    private const int ChunkBytes = ChunkSamples * 2;
    private const int SampleRate = 16_000;

    // ── Injected dependencies ─────────────────────────────────────────────────
    private readonly IAudioRingBuffer _ringBuffer;
    private readonly IWakeWordProvider _wakeWordProvider;
    private readonly IVadProvider _vadProvider;
    private readonly ISettingsService _settingsService;
    private readonly IPipelineStateService _pipelineState;
    private readonly IProviderRouter _providerRouter;
    private readonly IAIIntegrationService _aiIntegrationService;
    private readonly IAudioVisualizer _audioVisualizer;
    private readonly AudioRoutingManager _audioRoutingManager;
    private readonly IDebugLogger _logger;

    // ── Audio capture state ───────────────────────────────────────────────────
    private AudioRecord? _audioRecord;
    private CancellationTokenSource? _loopCts;

    // ── Re-used capture buffers (allocated once, not per-iteration) ───────────
    private readonly byte[] _captureBuffer = new byte[ChunkBytes];
    private readonly short[] _chunkShorts = new short[ChunkSamples];

    // ── Query accumulation state ──────────────────────────────────────────────
    private readonly List<short> _queryBuffer = new(16000 * 10);
    private short[]? _preRollSnapshot;
    private int _silenceChunksObserved;

    /// <summary>
    /// True while the STT → LLM → TTS pipeline is in flight.
    /// Prevents re-triggering the wake word until the current response is complete.
    /// </summary>
    private volatile bool _isProcessingQuery;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes a new pipeline controller with all required pipeline stage providers.
    /// </summary>
    public MicrophonePipelineController(
        IAudioRingBuffer ringBuffer,
        IWakeWordProvider wakeWordProvider,
        IVadProvider vadProvider,
        ISettingsService settingsService,
        IPipelineStateService pipelineState,
        IProviderRouter providerRouter,
        IAIIntegrationService aiIntegrationService,
        IAudioVisualizer audioVisualizer,
        AudioRoutingManager audioRoutingManager,
        IDebugLogger logger)
    {
        _ringBuffer = ringBuffer;
        _wakeWordProvider = wakeWordProvider;
        _vadProvider = vadProvider;
        _settingsService = settingsService;
        _pipelineState = pipelineState;
        _providerRouter = providerRouter;
        _aiIntegrationService = aiIntegrationService;
        _audioVisualizer = audioVisualizer;
        _audioRoutingManager = audioRoutingManager;
        _logger = logger;
    }

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the wake word provider, opens <see cref="AudioRecord"/>,
    /// transitions to <see cref="PipelineState.ListeningForTrigger"/>,
    /// and starts the background capture loop.
    /// </summary>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Log("Pipeline", "StartAsync: initializing wake word provider");
        await _wakeWordProvider.InitializeAsync(cancellationToken);

        int minBuffer = AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);
        if (minBuffer <= 0)
        {
            string msg = $"AudioRecord.GetMinBufferSize returned invalid value: {minBuffer}";
            _logger.Log("Pipeline", msg, DebugLogLevel.Error);
            _pipelineState.TransitionTo(PipelineState.Error, msg);
            return;
        }

        int recordBufferBytes = Math.Max(minBuffer, ChunkBytes) * 4;
        _audioRecord = new AudioRecord(
            AudioSource.VoiceCommunication,
            SampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit,
            recordBufferBytes);

        if (_audioRecord.State != State.Initialized)
        {
            const string msg = "AudioRecord failed to reach Initialized state";
            _logger.Log("Pipeline", msg, DebugLogLevel.Error);
            _pipelineState.TransitionTo(PipelineState.Error, msg);
            return;
        }

        _audioRecord.StartRecording();
        _audioVisualizer.ReportState(ListeningState.Listening);
        _pipelineState.TransitionTo(PipelineState.ListeningForTrigger, "Mic started");
        _logger.Log("Pipeline", $"AudioRecord started (buffer={recordBufferBytes} bytes); launching capture loop");

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => AudioCaptureLoopAsync(_loopCts.Token), _loopCts.Token);
    }

    /// <summary>
    /// Cancels the capture loop, releases <see cref="AudioRecord"/>, and transitions to
    /// <see cref="PipelineState.Idle"/>. Safe to call from <c>OnDestroy</c>.
    /// </summary>
    internal void StopAsync()
    {
        _logger.Log("Pipeline", "StopAsync called — stopping capture loop");
        _loopCts?.Cancel();

        try
        {
            _audioRecord?.Stop();
            _audioRecord?.Release();
        }
        catch (Exception ex)
        {
            _logger.Log("Pipeline", $"AudioRecord cleanup error: {ex.Message}", DebugLogLevel.Warning);
        }

        _audioRecord = null;
        _ringBuffer.Clear();
        _pipelineState.TransitionTo(PipelineState.Idle, "Mic stopped");
        _audioVisualizer.ReportState(ListeningState.Idle);
    }

    // ── Audio capture loop ────────────────────────────────────────────────────

    private async Task AudioCaptureLoopAsync(CancellationToken token)
    {
        _logger.Log("Pipeline", "Audio capture loop entered");

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_audioRecord is null) break;

                // Read raw PCM16 bytes from microphone
                int bytesRead = _audioRecord.Read(_captureBuffer, 0, ChunkBytes);
                if (bytesRead <= 0)
                {
                    // Negative value = AudioRecord error code; pause briefly and retry
                    await Task.Delay(10, token);
                    continue;
                }

                int samplesRead = bytesRead / 2;

                // Convert PCM16 bytes → shorts for ring buffer and wake word
                Buffer.BlockCopy(_captureBuffer, 0, _chunkShorts, 0, bytesRead);

                // Always fill the ring buffer so pre-roll audio is available on trigger
                _ringBuffer.Write(_chunkShorts.AsSpan(0, samplesRead));

                // Publish RMS to audio level visualizer
                _audioVisualizer.ReportAudioLevel(ComputeRms(_chunkShorts, samplesRead));

                PipelineState currentState = _pipelineState.CurrentState;

                // ── Wake word detection ───────────────────────────────────────
                if (currentState == PipelineState.ListeningForTrigger && !_isProcessingQuery)
                {
                    WakeWordResult wakeResult = await _wakeWordProvider.EvaluateChunkAsync(
                        _chunkShorts.AsMemory(0, samplesRead), token);

                    if (wakeResult.Detected)
                    {
                        _logger.Log("Pipeline",
                            $"Wake word detected — phrase='{wakeResult.MatchedPhrase}' confidence={wakeResult.Confidence:F2}");
                        HandleTriggerDetected();
                    }
                }
                // ── Query audio accumulation + end-of-speech detection ─────────
                else if (currentState == PipelineState.RecordingQuery)
                {
                    // Accumulate samples into the query buffer
                    for (int i = 0; i < samplesRead; i++)
                        _queryBuffer.Add(_chunkShorts[i]);

                    // VAD operates on raw PCM16 bytes
                    VadResult vadResult = await _vadProvider.DetectSpeechAsync(
                        _captureBuffer.AsMemory(0, bytesRead), SampleRate, token);

                    if (!vadResult.IsSpeechDetected)
                    {
                        _silenceChunksObserved++;

                        float postSilenceSeconds = _settingsService.VoiceAssistantSettings.PostSilenceSeconds;
                        int chunksNeeded = Math.Max(1, (int)(postSilenceSeconds / 0.08f));

                        if (_silenceChunksObserved >= chunksNeeded)
                        {
                            _logger.Log("Pipeline",
                                $"End-of-speech: {_silenceChunksObserved} silent chunks ≥ {chunksNeeded} needed — finalizing");
                            FinalizeQuery(token);
                        }
                    }
                    else
                    {
                        _silenceChunksObserved = 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Log("Pipeline", $"[CAPTURE LOOP ERROR] {ex.Message}", DebugLogLevel.Error);
                _pipelineState.TransitionTo(PipelineState.Error, ex.Message);
                _audioVisualizer.ReportState(ListeningState.Processing);

                try { await Task.Delay(3000, token); }
                catch (OperationCanceledException) { break; }

                _pipelineState.TransitionTo(PipelineState.ListeningForTrigger, "Auto-recovered from capture error");
                _audioVisualizer.ReportState(ListeningState.Listening);
            }
        }

        _logger.Log("Pipeline", "Audio capture loop exited");
        _audioVisualizer.ReportState(ListeningState.Idle);
    }

    // ── Pipeline stage handlers ───────────────────────────────────────────────

    private void HandleTriggerDetected()
    {
        _pipelineState.TransitionTo(PipelineState.TriggerDetected, "Wake word detected");

        // Snapshot the ring buffer pre-roll so speech before the trigger is included
        _preRollSnapshot = _ringBuffer.ReadLast(_settingsService.VoiceAssistantSettings.PreRollSeconds);

        // Reset query accumulation state
        _queryBuffer.Clear();
        _silenceChunksObserved = 0;

        _pipelineState.TransitionTo(PipelineState.RecordingQuery, "Recording query");
        _logger.Log("Pipeline", $"Pre-roll snapshot: {_preRollSnapshot.Length} samples; now recording query");
    }

    private void FinalizeQuery(CancellationToken token)
    {
        // Prepend pre-roll audio to captured query samples
        short[] preRoll = _preRollSnapshot ?? [];
        short[] fullAudio = [.. preRoll, .. _queryBuffer];

        // Reset state immediately so the loop doesn't accumulate into a stale buffer
        _queryBuffer.Clear();
        _preRollSnapshot = null;
        _silenceChunksObserved = 0;
        _isProcessingQuery = true;

        // Synchronously transition away from RecordingQuery so the loop doesn't re-enter this branch
        _pipelineState.TransitionTo(PipelineState.ProcessingSTT, "Starting AI pipeline");
        _logger.Log("Pipeline", $"FinalizeQuery: {fullAudio.Length} samples submitted to AI pipeline (fire-and-forget)");

        // Fire-and-forget: audio loop continues filling the ring buffer during AI processing
        _ = Task.Run(async () =>
        {
            try
            {
                await RunAiPipelineAsync(fullAudio, token);
            }
            catch (OperationCanceledException)
            {
                _logger.Log("Pipeline", "AI pipeline cancelled", DebugLogLevel.Warning);
            }
            catch (Exception ex)
            {
                _logger.Log("Pipeline", $"[AI PIPELINE ERROR] {ex.Message}", DebugLogLevel.Error);
                _pipelineState.TransitionTo(PipelineState.Error, ex.Message);
                _audioVisualizer.ReportState(ListeningState.Processing);

                try { await Task.Delay(3000, token); }
                catch (OperationCanceledException) { }

                _pipelineState.TransitionTo(PipelineState.ListeningForTrigger, "Recovered from AI pipeline error");
                _audioVisualizer.ReportState(ListeningState.Listening);
            }
            finally
            {
                // Always clear processing flag so wake word detection can resume
                _isProcessingQuery = false;
            }
        }, token);
    }

    // ── STT → LLM → TTS chain ────────────────────────────────────────────────

    private async Task RunAiPipelineAsync(short[] audioSamples, CancellationToken token)
    {
        // ── STT ───────────────────────────────────────────────────────────────
        _audioVisualizer.ReportState(ListeningState.Processing);
        _logger.Log("Pipeline", $"STT: converting {audioSamples.Length} samples ({audioSamples.Length / (float)SampleRate:F1}s) to PCM bytes");

        // Convert short[] to raw PCM16 byte[] — this is what IProviderRouter.TranscribeAsync expects
        var pcmBytes = new byte[audioSamples.Length * 2];
        Buffer.BlockCopy(audioSamples, 0, pcmBytes, 0, pcmBytes.Length);

        string transcription = await _providerRouter.TranscribeAsync(pcmBytes, null, token);

        if (string.IsNullOrWhiteSpace(transcription))
        {
            _logger.Log("Pipeline", "STT returned empty transcription — aborting pipeline", DebugLogLevel.Warning);
            _pipelineState.TransitionTo(PipelineState.ListeningForTrigger, "Empty transcription — try again");
            _audioVisualizer.ReportState(ListeningState.Listening);
            return;
        }

        _logger.Log("Pipeline", $"STT result: \"{transcription}\"");

        // ── LLM ───────────────────────────────────────────────────────────────
        _pipelineState.TransitionTo(PipelineState.ProcessingLLM, "Querying LLM");
        _logger.Log("Pipeline", $"LLM request: \"{transcription}\"");

        // SystemPrompt flows via the dedicated AiRequest property; AIIntegrationService
        // also discovers MCP tools and injects them automatically before the LLM call.
        string llmResponse = await _aiIntegrationService.ExecutePromptAsync(
            new AiRequest(transcription, MaxTokens: 512,
                SystemPrompt: _settingsService.VoiceAssistantSettings.SystemPrompt), token);

        if (string.IsNullOrWhiteSpace(llmResponse))
        {
            _logger.Log("Pipeline", "LLM returned empty response — aborting pipeline", DebugLogLevel.Warning);
            _pipelineState.TransitionTo(PipelineState.ListeningForTrigger, "Empty LLM response — try again");
            _audioVisualizer.ReportState(ListeningState.Listening);
            return;
        }

        _logger.Log("Pipeline", $"LLM response: \"{llmResponse}\"");

        // ── TTS ───────────────────────────────────────────────────────────────
        _pipelineState.TransitionTo(PipelineState.Speaking, "Speaking response");
        _audioVisualizer.ReportState(ListeningState.Speaking);
        _logger.Log("Pipeline", "TTS synthesis requested");

        AudioOutputMode outputMode = _settingsService.VoiceAssistantSettings.AudioOutput;
        try
        {
            _audioRoutingManager.ApplyOutputMode(outputMode);
            _logger.Log("Pipeline", $"Audio routing applied for TTS: {outputMode}");

            byte[] ttsAudio = await _providerRouter.SynthesizeAsync(llmResponse, token);
            _logger.Log("Pipeline", $"TTS synthesis complete: {ttsAudio.Length} bytes");

            await PlayTtsAudioAsync(ttsAudio, token);
        }
        finally
        {
            _audioRoutingManager.RestoreDefaultRouting();
            _logger.Log("Pipeline", "Audio routing restored after TTS playback");
        }

        // ── Complete ──────────────────────────────────────────────────────────
        _pipelineState.TransitionTo(PipelineState.ListeningForTrigger, "Ready for next command");
        _audioVisualizer.ReportState(ListeningState.Listening);
        _logger.Log("Pipeline", "AI pipeline complete — listening for next trigger");
    }

    // ── TTS playback ──────────────────────────────────────────────────────────

    private async ValueTask PlayTtsAudioAsync(byte[] audioData, CancellationToken ct)
    {
        string? filePath = null;
        try
        {
            if (audioData.Length == 0)
            {
                // AndroidTtsEngine plays directly through the speaker and returns empty bytes
                _logger.Log("Pipeline", "TTS: native engine path (empty delegate bytes) — waiting 500 ms");
                await Task.Delay(500, ct);
                return;
            }

            filePath = Path.Combine(
                Microsoft.Maui.Storage.FileSystem.CacheDirectory,
                $"quantumz-tts-{Guid.NewGuid():N}.wav");

            await File.WriteAllBytesAsync(filePath, audioData, ct);

            using var player = new MediaPlayer();
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            player.Completion += (_, _) => tcs.TrySetResult();
            player.Error += (_, args) =>
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"MediaPlayer error What={args.What} Extra={args.Extra}"));
                args.Handled = true;
            };

            player.SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(GetTtsAudioUsage(_settingsService.VoiceAssistantSettings.AudioOutput))
                .SetContentType(AudioContentType.Speech)
                .Build()!);

            player.SetDataSource(filePath);
            player.Prepare();
            player.Start();
            _logger.Log("Pipeline", "TTS playback started");

            await tcs.Task.WaitAsync(ct);
            _logger.Log("Pipeline", "TTS playback completed");
        }
        catch (OperationCanceledException)
        {
            _logger.Log("Pipeline", "TTS playback cancelled (service shutting down)");
        }
        catch (Exception ex)
        {
            _logger.Log("Pipeline", $"TTS playback error: {ex.Message}", DebugLogLevel.Error);
        }
        finally
        {
            if (filePath is not null)
                try { File.Delete(filePath); } catch { /* ignore cleanup failures */ }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes normalized RMS amplitude in the range [0, 1] for a PCM16 sample window.
    /// </summary>
    private static float ComputeRms(short[] samples, int count)
    {
        if (count == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < count; i++)
            sum += (float)samples[i] * samples[i];
        return MathF.Sqrt(sum / count) / 32768f;
    }

    private static AudioUsageKind GetTtsAudioUsage(AudioOutputMode outputMode) =>
        outputMode == AudioOutputMode.Bluetooth
            ? AudioUsageKind.Assistant
            : AudioUsageKind.AssistanceAccessibility;

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;

        try
        {
            _audioRecord?.Stop();
            _audioRecord?.Release();
        }
        catch { /* ignore dispose-time errors */ }

        _audioRecord = null;

        // Release wake word model resources (ONNX session, NPU handles, etc.)
        await _wakeWordProvider.DisposeAsync();
    }
}
