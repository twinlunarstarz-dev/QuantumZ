using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Infrastructure.Services;

public class McpOrchestrator(HttpClient httpClient, ISettingsService settings) : IMcpOrchestrator
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async ValueTask<List<McpTool>> DiscoverToolsAsync(CancellationToken ct = default)
    {
        var allTools = new List<McpTool>();

        foreach (var server in settings.McpServers.Where(s => !s.Disabled))
        {
            if (server.Transport == McpTransportType.Stdio)
            {
                Console.WriteLine($"Skipping stdio MCP server '{server.Name}' (Android does not support local stdio processes). Use a remote HTTP MCP gateway for mobile.");
                continue;
            }

            var result = await SendJsonRpcAsync<ListToolsResult>(server, "tools/list", null, ct)
                         ?? await SendJsonRpcAsync<ListToolsResult>(server, "list_tools", null, ct);

            if (result?.Tools is not { Count: > 0 })
                continue;

            allTools.AddRange(result.Tools
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => new McpTool(
                    Name: t.Name,
                    Description: t.Description ?? string.Empty,
                    InputSchemaJson: JsonSerializer.Serialize(t.InputSchema ?? EmptyInputSchema(), _jsonOptions),
                    ServerName: server.Name)));
        }

        return allTools
            .GroupBy(tool => $"{tool.ServerName}|{tool.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public async ValueTask<ToolResult> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        var arguments = ParseArguments(argumentsJson);
        var lastError = "No MCP server was able to execute the requested tool.";

        foreach (var server in settings.McpServers.Where(s => !s.Disabled && s.Transport != McpTransportType.Stdio))
        {
            var standardResult = await SendJsonRpcAsync<CallToolResult>(server, "tools/call", new CallToolParams(toolName, arguments), ct);
            if (standardResult is not null)
                return new ToolResult(true, ExtractContentText(standardResult));

            var legacyResult = await SendJsonRpcAsync<CallToolResult>(server, "call_tool", new LegacyCallToolParams(toolName, argumentsJson), ct);
            if (legacyResult is not null)
                return new ToolResult(true, ExtractContentText(legacyResult));

            lastError = $"MCP server '{server.Name}' did not execute tool '{toolName}'.";
        }

        return new ToolResult(false, string.Empty, lastError);
    }

    private async ValueTask<T?> SendJsonRpcAsync<T>(McpServerConfig server, string method, object? parameters, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint)
            {
                Content = JsonContent.Create(new JsonRpcRequest(method, parameters), options: _jsonOptions)
            };

            if (!string.IsNullOrWhiteSpace(server.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey.Trim());

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return default;

            var rpcResponse = await response.Content.ReadFromJsonAsync<JsonRpcResponse<T>>(_jsonOptions, ct);
            if (rpcResponse?.Error is not null)
            {
                Console.WriteLine($"MCP {server.Name} {method} error: {rpcResponse.Error.Message}");
                return default;
            }

            return rpcResponse?.Result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Console.WriteLine($"MCP {server.Name} {method} failed: {ex.Message}");
            return default;
        }
    }

    private static JsonElement ParseArguments(string argumentsJson)
    {
        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Fall through to empty object.
            }
        }

        using var fallback = JsonDocument.Parse("{}");
        return fallback.RootElement.Clone();
    }

    private static object EmptyInputSchema() => new { type = "object", properties = new { } };

    private string ExtractContentText(CallToolResult result)
    {
        var textItems = result.Content?
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .Select(item => item.Text!.Trim())
            .ToList() ?? [];

        return textItems.Count > 0
            ? string.Join("\n", textItems)
            : JsonSerializer.Serialize(result.Content ?? [], _jsonOptions);
    }

    private sealed record JsonRpcRequest(
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("params")] object? Params = null)
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; init; } = "2.0";

        [JsonPropertyName("id")]
        public int Id { get; init; } = 1;
    }

    private sealed record JsonRpcResponse<T>(
        [property: JsonPropertyName("result")] T? Result,
        [property: JsonPropertyName("error")] JsonRpcError? Error);

    private sealed record JsonRpcError(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message);

    private sealed record ListToolsResult(
        [property: JsonPropertyName("tools")] List<ToolDefinition>? Tools);

    private sealed record ToolDefinition(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("inputSchema")] object? InputSchema);

    private sealed record CallToolParams(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] object Arguments);

    private sealed record LegacyCallToolParams(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);

    private sealed record CallToolResult(
        [property: JsonPropertyName("content")] List<ContentItem>? Content);

    private sealed record ContentItem(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);
}
