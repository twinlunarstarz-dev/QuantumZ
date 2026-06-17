using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    string? ToolCallId = null,
    List<ToolCall>? ToolCalls = null
)
{
    [JsonPropertyName("tool\u005fcall\u005fid")]
    public string? ToolCallId { get; init; } = ToolCallId;

    [JsonPropertyName("tool\u005fcalls")]
    public List<ToolCall> ToolCalls { get; init; } = ToolCalls ?? [];
}

public record ToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonIgnore] string Name,
    [property: JsonIgnore] string ArgumentsJson
)
{
    [JsonPropertyName("type")]
    public string Type => "function";

    [JsonPropertyName("function")]
    public ToolCallFunction Function => new(Name, ArgumentsJson);
}

public record ToolCallFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments
);
