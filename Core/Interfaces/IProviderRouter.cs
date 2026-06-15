using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Routes audio and AI pipeline work to the best available provider with fallback support.
/// </summary>
public interface IProviderRouter
{
    /// <summary>
    /// Resolves the currently available VAD provider using router preference order.
    /// </summary>
    ValueTask<IVadProvider> ResolveVadProviderAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves the currently available STT provider using router preference order.
    /// </summary>
    ValueTask<ISttProvider> ResolveSttProviderAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves the currently available LLM provider using router preference order.
    /// </summary>
    ValueTask<ILlmProvider> ResolveLlmProviderAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves the currently available TTS provider using router preference order.
    /// </summary>
    ValueTask<ITtsProvider> ResolveTtsProviderAsync(CancellationToken ct = default);

    /// <summary>
    /// Detects speech using the best available VAD provider and falls back on provider failure.
    /// </summary>
    ValueTask<VadResult> DetectSpeechAsync(ReadOnlyMemory<byte> pcm16Audio, int sampleRate, CancellationToken ct = default);

    /// <summary>
    /// Transcribes audio using the best available STT provider and falls back on provider failure.
    /// </summary>
    ValueTask<string> TranscribeAsync(byte[] pcm16Audio, CancellationToken ct = default);

    /// <summary>
    /// Transcribes audio using the best available STT provider and streams text updates to the supplied progress sink.
    /// </summary>
    ValueTask<string> TranscribeAsync(byte[] pcm16Audio, IProgress<string>? textProgress, CancellationToken ct = default);

    /// <summary>
    /// Sends an LLM prompt using the best available LLM provider and falls back on provider failure.
    /// </summary>
    ValueTask<AiResponse> SendPromptAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>
    /// Streams an LLM prompt from the currently resolved LLM provider.
    /// </summary>
    IAsyncEnumerable<string> StreamPromptAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>
    /// Synthesizes speech using the best available TTS provider and falls back on provider failure.
    /// </summary>
    ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default);
}
