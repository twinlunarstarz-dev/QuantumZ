namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Provides LLM prompt execution and streaming token generation.
/// </summary>
public interface ILlmProvider : IProvider
{
    /// <summary>
    /// Sends a prompt to the LLM provider and returns the completed response.
    /// </summary>
    ValueTask<AiResponse> SendPromptAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to the LLM provider and streams response chunks as they arrive.
    /// </summary>
    IAsyncEnumerable<string> StreamPromptAsync(AiRequest request, CancellationToken ct = default);
}
