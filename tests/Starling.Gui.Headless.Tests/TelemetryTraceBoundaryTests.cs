// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Engine;
using Starling.Gui.Controls;
using Starling.Gui.Theme;
using EngineSize = SixLabors.ImageSharp.Size;

namespace Starling.Gui.Headless.Tests;

public class TelemetryTraceBoundaryTests
{
    private static readonly MethodInfo LiveTick =
        typeof(WebviewPanel).GetMethod("LiveTick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [AvaloniaFact]
    public async Task Live_tick_does_not_parent_under_a_leaked_navigation_activity()
    {
        var url = WritePage("""
            <body><div id="box">hello</div><script>window.__ready = 1;</script></body>
            """);

        var engine = new StarlingEngine();
        var result = await engine.LayoutPageAsync(
            url, new RenderOptions(new EngineSize(800, 600), FontSize: 16f),
            CancellationToken.None, onFirstPaint: _ => { });
        result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");

        var diag = new RecordingDiagnostics();
        using var panel = new WebviewPanel(
            new ThemeManager(), diag, _ => { }, (_, _) => { },
            (page, viewport) => engine.RelayoutPage(page, new RenderOptions(viewport, FontSize: 16f)));
        var window = new Window { Content = panel, Width = 800, Height = 600 };
        window.Show();
        panel.ShowPage(result.Value);
        window.CaptureRenderedFrame();
        diag.Clear();

        var leakedNavigate = new Activity("gui.navigate").Start();
        try
        {
            LiveTick.Invoke(panel, null);
            var tick = diag.Spans.Should().ContainSingle(s => s.Name == "gui.live.tick").Subject;
            tick.ParentName.Should().BeNull("dispatcher-driven frames are independent after first paint");
            Activity.Current.Should().BeSameAs(leakedNavigate);
        }
        finally
        {
            leakedNavigate.Stop();
            window.Close();
        }
    }

    private static string WritePage(string html)
    {
        var path = Path.Combine(Path.GetTempPath(), $"starling-trace-boundary-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, html);
        return "file://" + path.Replace('\\', '/');
    }

    private sealed class RecordingDiagnostics : IDiagnostics
    {
        private readonly ConcurrentQueue<SpanRecord> _spans = new();

        public IReadOnlyCollection<SpanRecord> Spans => _spans.ToArray();

        public IDisposable Span(string area, string operation)
        {
            var name = $"{area}.{operation}";
            var parentName = Activity.Current?.DisplayName;
            var activity = new Activity(name).Start();
            _spans.Enqueue(new SpanRecord(name, parentName));
            return new ActivityScope(activity);
        }

        public void Clear()
        {
            while (_spans.TryDequeue(out _)) { }
        }

        public void Counter(string name, double value) { }
        public void Gauge(string name, double value) { }
        public void Log(DiagLevel level, string area, string message) { }
        public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }
        public void LogException(string area, Exception exception, string? message = null) { }

        public readonly record struct SpanRecord(string Name, string? ParentName);

        private sealed class ActivityScope(Activity activity) : IDisposable
        {
            public void Dispose() => activity.Stop();
        }
    }
}
