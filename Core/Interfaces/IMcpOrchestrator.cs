using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuantumZ.Core.Interfaces;

public interface IMcpOrchestrator
{
    /// <summary>
    /// Discovers all available tools from configured MCP servers.
    /// </summary>
    ValueTask<List<McpTool>> DiscoverToolsAsync(CancellationToken ct = default);

    /// <summary>
    /// Executes a specific tool on the appropriate MCP server.
    /// </summary>
    ValueTask<ToolResult> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct = default);
}

public record McpTool(
    string Name,
    string Description,
    string InputSchemaJson,
    string ServerName
);

public record ToolResult(
    bool Success,
    string Content,
    string? ErrorMessage = null
);
