using System.Threading;
using System.Threading.Tasks;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Abstraction for speech-to-text engines (on-device Whisper or remote llama.cpp).
/// </summary>
public interface ISttEngine
{
    /// <summary>
    /// Returns true if the engine is loaded and ready.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Transcribes raw PCM16 audio (mono, 16kHz) to text.
    /// </summary>
    ValueTask<string> TranscribeAsync(byte[] pcm16Audio, CancellationToken ct = default);

    /// <summary>
    /// Initializes the engine (loads model, warms up, etc.).
    /// </summary>
    ValueTask InitializeAsync(CancellationToken ct = default);
}
