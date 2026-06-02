# Starling Telemetry Daemon

A standalone, out-of-process **OTLP receiver + lag analyzer** for the Starling
browser. It exists to answer two questions when pages feel laggy or the app
freezes:

1. **Which actions/metrics are causing the lag** — ranked span offenders, a
   frame-time/budget-overrun report, and the tile-cache miss ratio.
2. **How spans correlate with local CPU and memory** — every render span is
   joined to the browser process's sampled CPU utilization and working set, so a
   slow frame can be labelled CPU-bound vs. blocked/waiting.

It is intentionally separate from the Aspire dashboard: no AppHost required, and
it samples the browser's own CPU/RAM for correlation the dashboard doesn't do.

## Run it

```bash
dotnet run --project src/Starling.Telemetry.Daemon
```

Listens on (override with env vars):

| Port  | Protocol            | Env var                       |
|-------|---------------------|-------------------------------|
| 4317  | OTLP/gRPC           | `STARLING_DAEMON_GRPC_PORT`   |
| 4318  | OTLP/HTTP-protobuf + REST query API | `STARLING_DAEMON_HTTP_PORT` |
| 4319  | MCP (loopback)      | `STARLING_DAEMON_MCP_URL`     |

## Point the browser at it

The daemon reuses the standard OTLP exporter the hosts already wire up. The
easiest path — one env var, picked up by `OtelBootstrap.ConfigureDaemonExportFromEnv()`
in both the Avalonia GUI and the headless CLI:

```bash
# Avalonia GUI (the default host — this is where CPU/memory correlation lights up)
STARLING_TELEMETRY_DAEMON=http://localhost:4318 dotnet run --project src/Starling.Gui

# headless render
STARLING_TELEMETRY_DAEMON=http://localhost:4318 dotnet run --project src/Starling.Headless -- render page.html
```

`STARLING_TELEMETRY_DAEMON` sets `OTEL_EXPORTER_OTLP_ENDPOINT` +
`OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`. To use gRPC instead, point
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` directly (gRPC is the
exporter default protocol).

## Query the lag (REST)

```bash
curl localhost:4318/api/summary          # ingest counts + frames + CPU/RAM + top offenders
curl localhost:4318/api/top-offenders    # spans ranked by total time, with CPU/RAM during each
curl localhost:4318/api/frames           # frame-time p50/p95/p99, overrun rate, slowest frames + CPU/RAM
curl localhost:4318/api/resources        # latest + avg/max CPU utilization and working set
curl 'localhost:4318/api/correlate?span=paint.gpu.readback'   # is this span CPU-bound or blocked?
curl localhost:4318/healthz              # liveness + ingest counters
```

All accept `?window=<seconds>`.

## Query the lag (MCP)

The daemon hosts an MCP server (`STARLING_DAEMON_MCP_URL`, default
`http://127.0.0.1:4319/mcp`) exposing:

- the reused raw-telemetry tools — `browser_telemetry_traces` / `_logs` /
  `_metrics` / `_describe` (and `telemetry://` resources);
- analysis tools — `lag_overview`, `lag_top_offenders`, `lag_frames`,
  `lag_resources`, `lag_correlate_span`.

## What the browser emits

The receiver ingests anything OTLP, but the lag analysis keys on these
Starling-emitted signals (names in `Starling.Common.Diagnostics.RenderMetrics`):

| Signal | Kind | Pinpoints |
|---|---|---|
| `gui.frame.time_ms` | gauge | per-frame interactivity; drives the frame report |
| `gui.frame.budget_overrun` | counter | frames over the 16 ms budget |
| `gui.render`, `live.tick`, `live.pump`, `live.relayout`, `live.prepare_anim` | spans | Avalonia host loop phases |
| `paint.composite`, `paint.layertree.build` | spans | per-frame compositor / layer-tree rebuild cost |
| `paint.tile.cache_hit` / `cache_miss` | counters | per-tile cache effectiveness |
| `paint.tile.miss_ratio` | gauge | ≈1.0 while only a small region changed ⇒ whole-layer invalidation |
| `paint.tile.rasters_per_frame` | counter | unbounded synchronous tile raster |
| `paint.composite.output_alloc_bytes` | gauge | per-frame output-buffer allocation |
| `process.cpu.utilization`, `process.memory.working_set`, `process.memory.*`, `process.gc.*`, `process.threads` | gauges | local-machine resource use, sampled for span correlation |

The `process.*` gauges come from `ProcessResourceSampler`, started by the host;
the daemon joins them to each span's `[start, end]` window.
