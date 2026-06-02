# Model Context Protocol

Starling ships a loopback Model Context Protocol (MCP) server for local agent
harnesses. The repo-level config is `.mcp.json`:

```json
{
  "mcpServers": {
    "aspire": {
      "command": "aspire",
      "args": ["agent", "mcp"]
    },
    "starling": {
      "url": "http://127.0.0.1:3078/mcp"
    }
  }
}
```

Use that file when a harness can read repo MCP config. For harnesses that need
manual setup, register an HTTP MCP server named `starling` with the same URL.

## Starting It

`aspire run` starts the GUI and sets:

```bash
STARLING_MCP_URL=http://127.0.0.1:3078/mcp
```

Direct GUI runs default to:

```bash
http://127.0.0.1:3077/mcp
```

Headless only starts its MCP server when `STARLING_HEADLESS_MCP_URL` is set.
The telemetry daemon defaults to:

```bash
http://127.0.0.1:4319/mcp
```

## Discovery

The server exposes standard MCP discovery:

- `initialize` returns server info and capabilities.
- `tools/list` returns tool names, titles, descriptions, input schemas, output
  schemas, and tool annotations.
- `resources/list` returns telemetry resources such as `telemetry://traces`,
  `telemetry://logs`, and `telemetry://metrics`.
- `prompts/list` returns default Starling prompts when the matching tool group
  is loaded.

Browser tools are exposed by the GUI. Telemetry tools and resources are shared
by the GUI, headless host, and telemetry daemon.

## Transport

Starling uses the official C# MCP SDK Streamable HTTP transport. The endpoint
must be loopback HTTP and must include a path, such as `/mcp`.

Protocol clients should send `Accept: application/json, text/event-stream` on
JSON-RPC POST requests. The server can reply with either plain JSON or a
Server-Sent Events response. It rejects browser-origin requests from non-loopback
origins.
