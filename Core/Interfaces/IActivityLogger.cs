using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

public interface IActivityLogger
{
    /// <summary>
    /// Captures a fragment of transcribed text for background logging and later summarization.
    /// </summary>
    ValueTask LogFragmentAsync(string text, string? metadata = null);
}