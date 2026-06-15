using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuantumZ.Core.Interfaces;

public interface IAIClient
{
    /// <summary>
    /// Sends a prompt to the AI model and returns the response.
    /// </summary>
    ValueTask<AiResponse> SendPromptAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to the AI model and streams the response.
    /// </summary>
    IAsyncEnumerable<string> StreamPromptAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>
    /// Fetches available models from the AI server.
    /// </summary>
    Task<List<string>> GetAvailableModelsAsync();
}

public record AiRequest(
    string Prompt,
    List<ChatMessage>? History = null,
    float Temperature = 0.7f,
    int MaxTokens = 2048,
    bool EnableToolCalling = true
)
{
    public List<ChatMessage> History { get; init; } = History ?? [];
}

public record AiResponse(
    string Content,
    List<ToolCall>? ToolCalls = null,
    string? ModelId = null,
    long UsageTokens = 0
)
{
    public List<ToolCall> ToolCalls { get; init; } = ToolCalls ?? [];
}

public record ChatMessage(
    string Role, // "system", "user", "assistant", "tool"
    string Content,
    string? ToolCallId = null
);

public record ToolCall(
    string Id,
    string Name,
    string ArgumentsJson
);
