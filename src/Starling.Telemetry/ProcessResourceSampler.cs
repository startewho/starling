using System.Diagnostics;
using Starling.Common.Diagnostics;

namespace Starling.Telemetry;

/// <summary>
/// Samples this process's CPU and memory use on a fixed cadence and records
/// them as gauges through <see cref="IDiagnostics"/> (which the OTel sink maps
/// to OTLP metrics). These are the "local computer" signals the telemetry
/// daemon joins to span time-windows so a janky frame can be attributed to CPU
/// saturation or GC/memory pressure rather than guessed at.
///
/// CPU utilization is computed as the process-CPU-time delta over the wall-clock
/// delta, normalised by logical core count, so the gauge reads 0..1 across the
/// whole machine (1.0 == every core pinned by this process). Sampling is
/// best-effort: a platform that denies a counter is swallowed rather than
/// crashing the host.
/// </summary>
public sealed class ProcessResourceSampler : IDisposable
{
    private readonly IDiagnostics _diag;
    private readonly Process _proc = Process.GetCurrentProcess();
    private readonly int _cores = Math.Max(1, Environment.ProcessorCount);
    private readonly Timer _timer;
    private TimeSpan _lastCpu;
    private long _lastTimestamp;
    private bool _disposed;

    /// <summary>
    /// Start sampling at <paramref name="interval"/> (default 500 ms — a balance
    /// between correlation resolution and overhead). The first tick establishes
    /// the CPU baseline; the gauge becomes meaningful from the second tick.
    /// </summary>
    public ProcessResourceSampler(IDiagnostics diagnostics, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diag = diagnostics;
        var period = interval ?? TimeSpan.FromMilliseconds(500);
        _lastCpu = SafeTotalProcessorTime();
        _lastTimestamp = Stopwatch.GetTimestamp();
        // Emit the constant core count once up front so consumers can de-normalise.
        _diag.Gauge(RenderMetrics.ProcessCpuCores, _cores);
        _timer = new Timer(static state => ((ProcessResourceSampler)state!).Sample(), this, period, period);
    }

    private void Sample()
    {
        if (_disposed) return;
        try
        {
            _proc.Refresh();

            var now = Stopwatch.GetTimestamp();
            var wall = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalMilliseconds;
            var cpu = SafeTotalProcessorTime();
            var cpuDeltaMs = (cpu - _lastCpu).TotalMilliseconds;
            _lastCpu = cpu;
            _lastTimestamp = now;

            // Fraction of total machine capacity: cpu time / (wall * cores).
            var util = wall > 0 ? Math.Clamp(cpuDeltaMs / (wall * _cores), 0, 1) : 0;
            _diag.Gauge(RenderMetrics.ProcessCpuUtilization, util);

            _diag.Gauge(RenderMetrics.ProcessMemoryWorkingSet, _proc.WorkingSet64);
            _diag.Gauge(RenderMetrics.ProcessMemoryManaged, GC.GetTotalMemory(forceFullCollection: false));
            _diag.Gauge(RenderMetrics.ProcessHeapBytes, GC.GetGCMemoryInfo().HeapSizeBytes);
            _diag.Gauge(RenderMetrics.ProcessGcGen0, GC.CollectionCount(0));
            _diag.Gauge(RenderMetrics.ProcessGcGen1, GC.CollectionCount(1));
            _diag.Gauge(RenderMetrics.ProcessGcGen2, GC.CollectionCount(2));

            try { _diag.Gauge(RenderMetrics.ProcessThreads, _proc.Threads.Count); }
            catch { /* Threads is platform-sensitive; skip if denied. */ }
        }
        catch
        {
            // Sampling must never take the host down.
        }
    }

    private TimeSpan SafeTotalProcessorTime()
    {
        try { return _proc.TotalProcessorTime; }
        catch { return _lastCpu; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        _proc.Dispose();
    }
}
