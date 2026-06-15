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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async ValueTask<List<McpTool>> DiscoverToolsAsync(CancellationToken ct = default)
    {
        var allTools = new List<McpTool>();

        foreach (var server in settings.McpServers)
        {
            if (server.Transport == McpTransportType.Stdio)
            {
                Console.WriteLine($"Skipping stdio MCP server '{server.Name}' (Android does not support local stdio processes).");
                continue;
            }

            try
            {
                var request = new JsonRpcRequest("list_tools", null);
                var response = await httpClient.PostAsJsonAsync(server.Endpoint, request, _jsonOptions, ct);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<JsonRpcResponse<ListToolsResult>>(_jsonOptions, ct);
                if (result?.Result?.Tools != null)
                {
                    allTools.AddRange(result.Result.Tools.Select(t => new McpTool(
                        Name: t.Name,
                        Description: t.Description ?? "",
                        InputSchemaJson: JsonSerializer.Serialize(t.InputSchema, _jsonOptions),
                        ServerName: server.Name
                    )));
                }
            }
            catch (Exception ex)
            {
                // Log error but continue discovering other servers
                Console.WriteLine($"Error discovering tools from MCP server {server.Name}: {ex.Message}");
            }
        }

        return allTools;
    }

    public async ValueTask<ToolResult> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        // Find which server provides this tool.
        // For the current implementation, we iterate through configured servers that are not disabled.

        // For now, let's try all servers that claim this tool if we had a registry. 
        // Since we don't have a persistent registry here yet, we iterate through configured servers.
        foreach (var s in settings.McpServers)
        {
            if (s.Disabled || s.Transport == McpTransportType.Stdio)
                continue;

            try
            {
                var request = new JsonRpcRequest("call_tool", new CallToolParams(toolName, argumentsJson));
                var response = await httpClient.PostAsJsonAsync(s.Endpoint, request, _jsonOptions, ct);
                if (!response.IsSuccessStatusCode) continue;

                var result = await response.Content.ReadFromJsonAsync<JsonRpcResponse<CallToolResult>>(_jsonOptions, ct);
                if (result?.Result != null)
                {
                    return new ToolResult(
                        Success: true, 
                        Content: JsonSerializer.Serialize(result.Result.Content, _jsonOptions)
                    );
                }
            }
            catch { /* Try next server */ }
        }

        return new ToolResult(false, "", "No MCP server was able to execute the requested tool.");
    }

    // --- JSON-RPC DTOs ---

    private record JsonRpcRequest(string method, object? params_ = null) 
    {
        [JsonPropertyName("params")]
        public object? Params { get; set; } = params_;
        public int id { get; set; } = 1;
        public string jsonrpc { get; set; } = "2.0";

        // Custom constructor to handle the 'params' keyword which is reserved in C#
    }

    private record JsonRpcResponse<T>(T? Result, string? Error);

    private record ListToolsResult(List<ToolDefinition> Tools);

    private record ToolDefinition(string Name, string? Description, object? InputSchema);

    private record CallToolParams(string name, string arguments);

    private record CallToolResult(List<ContentItem> Content);

    private record ContentItem(string type, string text);
}
