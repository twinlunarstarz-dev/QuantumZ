using System.Threading;
using System.Threading.Tasks;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Abstraction for text-to-speech engines.
/// </summary>
public interface ITtsEngine
{
    /// <summary>
    /// Synthesizes text into raw audio bytes (format depends on implementation).
    /// </summary>
    ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default);
}
