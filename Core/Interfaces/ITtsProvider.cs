namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Provides text-to-speech synthesis for assistant responses.
/// </summary>
public interface ITtsProvider : IProvider
{
    /// <summary>
    /// Synthesizes text into provider-specific audio bytes or plays it directly for built-in engines.
    /// </summary>
    ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default);
}
