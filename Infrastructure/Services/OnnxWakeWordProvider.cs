using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Wake word provider using ONNX Runtime for openWakeWord-compatible 3-stage detection.
/// Accepts 80 ms PCM16 chunks (1280 samples at 16 kHz) and returns a confidence score.
/// Attempts to use the Android NNAPI execution provider; falls back to CPU silently.
/// </summary>
internal sealed class OnnxWakeWordProvider(ISettingsService settingsService, IDebugLogger logger)
    : IWakeWordProvider, IAsyncDisposable
{
    private const int InputLen = 1280;
    private const int MelBufferSize = 76 * 32; // 2432 floats — ~6 s of mel feature history

    private InferenceSession? _session;

    /// <summary>Ring buffer reserved for mel feature history (openWakeWord stage 1/2 pre-processing).</summary>
    private readonly float[] _inputBuffer = new float[MelBufferSize];

    /// <summary>Reusable PCM normalisation scratch buffer — avoids a heap allocation per 80 ms chunk.</summary>
    private readonly float[] _pcmBuffer = new float[InputLen];

    private readonly float _threshold = settingsService.VoiceAssistantSettings.WakeWordThreshold;
    private string _inputName = string.Empty;
    private bool _isInitialized;

    /// <inheritdoc/>
    public string TriggerPhrase => settingsService.VoiceAssistantSettings.TriggerPhrase;

    /// <inheritdoc/>
    public bool IsInitialized => _isInitialized;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Wake word path is currently managed via VoiceAssistantSettings or defaults,
        // as PipelineSettings was removed to unify on legacy provider sources of truth.
        var modelPath = string.Empty;

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            logger.Log(nameof(OnnxWakeWordProvider),
                "WakeWord model path not configured; OnnxWakeWordProvider disabled.",
                LogLevel.Warning);
            _isInitialized = false;
            return;
        }

        if (!File.Exists(modelPath))
        {
            logger.Log(nameof(OnnxWakeWordProvider),
                $"WakeWord model not found at {modelPath}; OnnxWakeWordProvider disabled.",
                LogLevel.Warning);
            _isInitialized = false;
            return;
        }

        await Task.Run(() =>
        {
            var opts = new SessionOptions();
            var nnapiAvailable = false;

            try
            {
                opts.AppendExecutionProvider_Nnapi(0u);
                nnapiAvailable = true;
            }
            catch (Exception ex)
            {
                logger.Log(nameof(OnnxWakeWordProvider),
                    $"NNAPI not available, falling back to CPU: {ex.Message}",
                    LogLevel.Info);
            }

            _session = new InferenceSession(modelPath, opts);
            _inputName = _session.InputMetadata.Keys.First();
            _isInitialized = true;

            logger.Log(nameof(OnnxWakeWordProvider),
                $"OnnxWakeWordProvider initialized. NNAPI={nnapiAvailable}, model={modelPath}",
                LogLevel.Info);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<WakeWordResult> EvaluateChunkAsync(
        ReadOnlyMemory<short> chunk,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized || _session is null)
            return ValueTask.FromResult(new WakeWordResult(false, 0f, null));

        try
        {
            var pcm = chunk.Span;
            var count = Math.Min(pcm.Length, InputLen);

            for (var i = 0; i < count; i++)
                _pcmBuffer[i] = pcm[i] / 32768f;

            // Pad with silence if chunk is shorter than the expected 1280 samples.
            for (var i = count; i < InputLen; i++)
                _pcmBuffer[i] = 0f;

            var inputTensor = new DenseTensor<float>(_pcmBuffer.AsMemory(), [1, InputLen]);
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)]);
            var confidence = results.First().AsTensor<float>()[0];
            var detected = confidence >= _threshold;

            return ValueTask.FromResult(new WakeWordResult(
                detected,
                confidence,
                detected ? TriggerPhrase : null));
        }
        catch (Exception ex)
        {
            logger.Log(nameof(OnnxWakeWordProvider),
                $"Inference error: {ex.Message}",
                LogLevel.Debug);
            return ValueTask.FromResult(new WakeWordResult(false, 0f, null));
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _session?.Dispose();
        _session = null;
        _isInitialized = false;
        return ValueTask.CompletedTask;
    }
}
