using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Fallback wake word provider that triggers on sustained speech energy (RMS threshold).
/// Requires no model file — active immediately after service registration.
/// This is the BuiltIn-mode wake word option.
/// </summary>
internal sealed class RmsWakeWordProvider(ISettingsService settingsService, IDebugLogger logger)
    : IWakeWordProvider
{
    private const int SpeechThresholdChunks = 8;  // 8 × 80 ms = 640 ms sustained speech
    private const int SilenceResetChunks = 3;      // 3 × 80 ms = 240 ms silence resets counter
    private const float RmsThreshold = 0.015f;     // normalised RMS energy threshold

    private int _speechChunkCount;
    private int _silenceChunkCount;

    /// <inheritdoc/>
    public string TriggerPhrase => settingsService.VoiceAssistantSettings.TriggerPhrase;

    /// <inheritdoc/>
    public bool IsInitialized => true;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.Log(nameof(RmsWakeWordProvider),
            "RmsWakeWordProvider ready (no model required)",
            LogLevel.Info);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<WakeWordResult> EvaluateChunkAsync(
        ReadOnlyMemory<short> chunk,
        CancellationToken cancellationToken = default)
    {
        var span = chunk.Span;
        var count = span.Length;

        // Compute RMS of the raw PCM16 samples.
        float sumSquares = 0f;
        for (var i = 0; i < count; i++)
        {
            var s = span[i] / 32768f;
            sumSquares += s * s;
        }

        var normRms = count > 0 ? MathF.Sqrt(sumSquares / count) : 0f;

        if (normRms > RmsThreshold)
        {
            _speechChunkCount++;
            _silenceChunkCount = 0;
        }
        else
        {
            _silenceChunkCount++;
            if (_silenceChunkCount >= SilenceResetChunks)
                _speechChunkCount = 0;
        }

        if (_speechChunkCount >= SpeechThresholdChunks)
        {
            _speechChunkCount = 0;
            return ValueTask.FromResult(new WakeWordResult(true, 1f, TriggerPhrase));
        }

        return ValueTask.FromResult(new WakeWordResult(false, normRms, null));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
