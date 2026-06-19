namespace QuantumZ.Core.Interfaces;

/// <summary>Result of a wake word detection evaluation on an audio chunk.</summary>
public readonly record struct WakeWordResult(bool Detected, float Confidence, string? MatchedPhrase);

/// <summary>
/// Evaluates short audio chunks in real-time to detect a configured trigger phrase.
/// Implementations should be optimized for continuous low-latency inference.
/// </summary>
public interface IWakeWordProvider
{
    /// <summary>
    /// Initializes the wake word model. Must be called before <see cref="EvaluateChunkAsync"/>.
    /// Implementations should load ONNX models and warm up NPU/DSP delegates here.
    /// </summary>
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a 80 ms PCM16 audio chunk (1280 samples at 16 kHz) against the trigger phrase.
    /// Must return in under 10 ms on the target device.
    /// </summary>
    /// <param name="chunk">Exactly 1280 PCM16 samples (80 ms at 16 kHz mono).</param>
    ValueTask<WakeWordResult> EvaluateChunkAsync(ReadOnlyMemory<short> chunk, CancellationToken cancellationToken = default);

    /// <summary>The trigger phrase this provider is listening for.</summary>
    string TriggerPhrase { get; }

    /// <summary>Whether the provider has been successfully initialized.</summary>
    bool IsInitialized { get; }

    /// <summary>Releases model resources.</summary>
    ValueTask DisposeAsync();
}
