namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Provides speech-to-text transcription for raw PCM16 audio.
/// </summary>
public interface ISttProvider : IProvider
{
    /// <summary>
    /// Initializes provider resources such as model handles, server state, or warm-up requests.
    /// </summary>
    ValueTask InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Transcribes raw PCM16 mono audio to text.
    /// </summary>
    ValueTask<string> TranscribeAsync(byte[] pcm16Audio, CancellationToken ct = default);

    /// <summary>
    /// Transcribes raw PCM16 mono audio to text while reporting partial or final text updates to the caller.
    /// </summary>
    async ValueTask<string> TranscribeAsync(byte[] pcm16Audio, IProgress<string>? textProgress, CancellationToken ct = default)
    {
        var text = await TranscribeAsync(pcm16Audio, ct);
        textProgress?.Report(text);
        return text;
    }
}
