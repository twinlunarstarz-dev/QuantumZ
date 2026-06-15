using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Provides voice activity detection over raw PCM16 audio windows.
/// </summary>
public interface IVadProvider : IProvider
{
    /// <summary>
    /// Fired when the provider detects a speech activity transition, such as speech start or speech end.
    /// </summary>
    event EventHandler<VadActivityEventArgs>? ActivityChanged;

    /// <summary>
    /// Detects whether a PCM16 mono audio window contains speech and emits activity transition events.
    /// </summary>
    ValueTask<VadResult> DetectSpeechAsync(ReadOnlyMemory<byte> pcm16Audio, int sampleRate, CancellationToken ct = default);
}
