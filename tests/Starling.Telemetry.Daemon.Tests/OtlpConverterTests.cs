using AwesomeAssertions;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using Starling.Common.Diagnostics;
using Starling.Telemetry.Daemon.Ingestion;

namespace Starling.Telemetry.Daemon.Tests;

[TestClass]
public sealed class OtlpConverterTests
{
    [TestMethod]
    public void ToActivityRecords_DecodesSpanFieldsAndTags()
    {
        var start = DateTime.UtcNow.AddSeconds(-1);
        var traceId = new byte[16]; traceId[0] = 0xAB;
        var spanId = new byte[8]; spanId[0] = 0xCD;

        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource
                    {
                        Attributes = { Kv("service.name", "starling-gui") },
                    },
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = new InstrumentationScope { Name = "Starling.Engine" },
                            Spans =
                            {
                                new Span
                                {
                                    Name = "paint.gpu.readback",
                                    TraceId = ByteString.CopyFrom(traceId),
                                    SpanId = ByteString.CopyFrom(spanId),
                                    StartTimeUnixNano = Nanos(start),
                                    EndTimeUnixNano = Nanos(start) + 50_000_000UL, // +50ms
                                    Attributes = { Kv("tile.col", 3L) },
                                    Status = new Status { Code = Status.Types.StatusCode.Error },
                                },
                            },
                        },
                    },
                },
            },
        };

        var records = OtlpConverter.ToActivityRecords(request);

        records.Should().HaveCount(1);
        var r = records[0];
        r.Source.Should().Be("Starling.Engine");
        r.OperationName.Should().Be("paint.gpu.readback");
        r.Duration.TotalMilliseconds.Should().BeApproximately(50, 0.5);
        r.TraceId.Should().StartWith("ab");
        r.SpanId.Should().StartWith("cd");
        r.Status.Should().Be(System.Diagnostics.ActivityStatusCode.Error);
        r.Tags.Should().Contain(kv => kv.Key == "service.name" && (string?)kv.Value == "starling-gui");
        r.Tags.Should().Contain(kv => kv.Key == "tile.col" && (long?)(kv.Value as long?) == 3L);
    }

    [TestMethod]
    public void ToMeterRecords_DecodesGaugeAndSumPoints()
    {
        var now = DateTime.UtcNow;
        var request = new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Scope = new InstrumentationScope { Name = "Starling.Engine" },
                            Metrics =
                            {
                                new Metric
                                {
                                    Name = RenderMetrics.ProcessCpuUtilization,
                                    Unit = "1",
                                    Gauge = new Gauge
                                    {
                                        DataPoints = { new NumberDataPoint { TimeUnixNano = Nanos(now), AsDouble = 0.83 } },
                                    },
                                },
                                new Metric
                                {
                                    Name = "paint.tile.cache_miss",
                                    Sum = new Sum
                                    {
                                        DataPoints = { new NumberDataPoint { TimeUnixNano = Nanos(now), AsInt = 7 } },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var records = OtlpConverter.ToMeterRecords(request);

        records.Should().HaveCount(2);
        records.Should().Contain(m => m.InstrumentName == RenderMetrics.ProcessCpuUtilization && m.Value == 0.83);
        records.Should().Contain(m => m.InstrumentName == "paint.tile.cache_miss" && m.Value == 7);
    }

    private static KeyValue Kv(string key, string value)
        => new() { Key = key, Value = new AnyValue { StringValue = value } };

    private static KeyValue Kv(string key, long value)
        => new() { Key = key, Value = new AnyValue { IntValue = value } };

    private static ulong Nanos(DateTime utc)
        => (ulong)new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds() * 1_000_000UL;
}
