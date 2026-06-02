using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Starling.Telemetry.Daemon.Ingestion;

/// <summary>
/// OpenTelemetry Protocol receiver services on the daemon's gRPC port. This is
/// the .NET exporter's default protocol, so a host only needs to point
/// OTEL_EXPORTER_OTLP_ENDPOINT here. Each service decodes the export request
/// into the shared record types and hands them to the ingest store. An empty
/// response signals full success.
/// </summary>
internal sealed class TraceIngestService(TelemetryIngestStore store) : TraceService.TraceServiceBase
{
    public override Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request, ServerCallContext context)
    {
        store.IngestSpans(OtlpConverter.ToActivityRecords(request));
        return Task.FromResult(new ExportTraceServiceResponse());
    }
}

internal sealed class MetricsIngestService(TelemetryIngestStore store) : MetricsService.MetricsServiceBase
{
    public override Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request, ServerCallContext context)
    {
        store.IngestMetrics(OtlpConverter.ToMeterRecords(request));
        return Task.FromResult(new ExportMetricsServiceResponse());
    }
}

internal sealed class LogsIngestService(TelemetryIngestStore store) : LogsService.LogsServiceBase
{
    public override Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request, ServerCallContext context)
    {
        store.IngestLogs(OtlpConverter.ToLogRecords(request));
        return Task.FromResult(new ExportLogsServiceResponse());
    }
}
