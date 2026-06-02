using System.Net.Http.Headers;
using AwesomeAssertions;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;
using Starling.Common.Diagnostics;

namespace Starling.Telemetry.Daemon.Tests;

/// <summary>
/// End-to-end of the OTLP/HTTP-protobuf receiver: boot the real daemon host on
/// loopback ports, POST serialized OTLP requests exactly as the .NET exporter
/// would, then read the result back through the REST query API.
/// </summary>
[TestClass]
public sealed class OtlpHttpReceiverTests
{
    [TestMethod]
    public async Task PostOtlpTracesAndMetrics_ShowUpInQueryApi()
    {
        var basePort = System.Random.Shared.Next(20100, 38000);
        var http = basePort;
        var grpc = basePort + 1;

        var app = DaemonApp.Build(new DaemonOptions(grpc, http));
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{http}") };

            await PostProtobuf(client, "/v1/traces", BuildTraceRequest());
            await PostProtobuf(client, "/v1/metrics", BuildMetricsRequest());

            var health = await client.GetStringAsync("/healthz");
            health.Should().Contain("\"spansIngested\":1");
            health.Should().Contain("\"metricsIngested\":1");

            var offenders = await client.GetStringAsync("/api/top-offenders?window=3600");
            offenders.Should().Contain("paint.gpu.readback");

            var resources = await client.GetStringAsync("/api/resources?window=3600");
            resources.Should().Contain("cpuUtilization");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task PostMalformedProtobuf_Returns400_NotNull500()
    {
        var basePort = System.Random.Shared.Next(20100, 38000);
        var app = DaemonApp.Build(new DaemonOptions(basePort + 1, basePort));
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{basePort}") };
            // field 1, wire-type 2 (length-delimited), declared length 5, but truncated.
            using var content = new ByteArrayContent([0x0A, 0x05, 0x01]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

            var resp = await client.PostAsync("/v1/traces", content);

            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task PostProtobuf(HttpClient client, string path, IMessage message)
    {
        using var content = new ByteArrayContent(message.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        var response = await client.PostAsync(path, content);
        response.EnsureSuccessStatusCode();
    }

    private static ExportTraceServiceRequest BuildTraceRequest()
    {
        var start = Nanos(DateTime.UtcNow.AddMilliseconds(-100));
        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
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
                                    TraceId = ByteString.CopyFrom(new byte[16]),
                                    SpanId = ByteString.CopyFrom(new byte[8]),
                                    StartTimeUnixNano = start,
                                    EndTimeUnixNano = start + 30_000_000UL,
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private static ExportMetricsServiceRequest BuildMetricsRequest() => new()
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
                                Gauge = new Gauge
                                {
                                    DataPoints = { new NumberDataPoint { TimeUnixNano = Nanos(DateTime.UtcNow), AsDouble = 0.77 } },
                                },
                            },
                        },
                    },
                },
            },
        },
    };

    private static ulong Nanos(DateTime utc)
        => (ulong)new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds() * 1_000_000UL;
}
