using System.Text.Json;

namespace Starling.Mcp;

/// <summary>
/// One subsystem's contribution to the MCP prompts surface.
/// </summary>
public interface IMcpPromptProvider
{
    /// <summary>
    /// JSON array literal of MCP prompt descriptors.
    /// <c>[{"name": "...", "title": "...", "description": "..."}, ...]</c>.
    /// </summary>
    string GetPromptDescriptorsJson();

    /// <summary>True if this provider owns the named prompt.</summary>
    bool HasPrompt(string name);

    /// <summary>Build the named prompt with the provided argument object.</summary>
    Task<McpPromptResult> GetAsync(string name, JsonElement arguments, CancellationToken ct);
}

public sealed record McpPromptResult(string Description, string Text);
