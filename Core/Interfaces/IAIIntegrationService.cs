using System.Threading;
using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Orchestrates the high-level AI interaction flow, including prompt execution 
/// and tool calling loops between the LLM client and MCP orchestrator.
/// </summary>
public interface IAIIntegrationService
{
    /// <summary>
    /// Executes an AI request, handling potential multi-turn tool calls before returning a final response content.
    /// </summary>
    /// <param name="request">The prompt and history configuration.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The final text response from the AI assistant.</returns>
    ValueTask<string> ExecutePromptAsync(AiRequest request, CancellationToken ct = default);
}