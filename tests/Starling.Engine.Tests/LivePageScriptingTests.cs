using AwesomeAssertions;
using SixLabors.ImageSharp;
using Starling.Dom.Events;

namespace Starling.Engine.Tests;

/// <summary>
/// Covers the live-page JS context retained past first paint (<see cref="PageScripting"/>):
/// the interactive load path keeps the realm alive so the shell can dispatch DOM
/// events into page listeners and pump timers/microtasks against wall-clock time.
/// </summary>
[TestClass]
public sealed class LivePageScriptingTests
{
    private static readonly RenderOptions Options = new(new Size(800, 600), 16f);

    [TestMethod]
    public async Task Interactive_load_retains_a_live_scripting_context()
    {
        using var page = await LoadInteractiveAsync(
            "<!doctype html><html><body><input id='q'><script>window.x=1;</script></body></html>");
        page.Scripting.Should().NotBeNull("a page with scripts loaded interactively stays live");
    }

    [TestMethod]
    public async Task Dispatching_an_input_event_runs_the_page_listener()
    {
        const string html = """
            <!doctype html><html><body>
              <input id='q' type='text'>
              <div id='out'>none</div>
              <script>
                var q = document.getElementById('q');
                q.addEventListener('input', function () {
                  document.getElementById('out').textContent = 'sync:' + q.value;
                });
              </script>
            </body></html>
            """;
        using var page = await LoadInteractiveAsync(html);
        var input = page.Document.GetElementById("q")!;
        var output = page.Document.GetElementById("out")!;

        // The shell sets the live value, then dispatches the DOM input event.
        input.InputValue = "hi";
        var mutated = page.Scripting!.DispatchEvent(
            input, new InputEvent("input", new EventInit(Bubbles: true)) { Data = "i", InputType = "insertText" });

        mutated.Should().BeTrue("the listener mutated the DOM");
        output.TextContent.Should().Be("sync:hi");
    }

    [TestMethod]
    public async Task Dispatching_click_to_status_island_root_updates_counter()
    {
        const string html = """
            <!doctype html><html><body>
              <div id='wasm-island'>
                <span id='wasm-state'>booting</span>
                <button id='wasm-clicks'>0</button>
              </div>
              <script>
                var island = document.getElementById('wasm-island');
                var clicks = document.getElementById('wasm-clicks');
                var count = 0;
                island.addEventListener('click', function () {
                  count = count + 1;
                  clicks.textContent = String(count);
                });
                var bytes = new Uint8Array([
                  0x00,0x61,0x73,0x6d,0x01,0x00,0x00,0x00,
                  0x01,0x07,0x01,0x60,0x02,0x7f,0x7f,0x01,0x7f,
                  0x03,0x02,0x01,0x00,
                  0x07,0x07,0x01,0x03,0x61,0x64,0x64,0x00,0x00,
                  0x0a,0x09,0x01,0x07,0x00,0x20,0x00,0x20,0x01,0x6a,0x0b
                ]);
                WebAssembly.instantiate(bytes).then(function (result) {
                  document.getElementById('wasm-state').textContent =
                    'Wasmtime add(19, 23) = ' + result.instance.exports.add(19, 23);
                });
              </script>
            </body></html>
            """;
        using var page = await LoadInteractiveAsync(html);
        var island = page.Document.GetElementById("wasm-island")!;
        var clicks = page.Document.GetElementById("wasm-clicks")!;

        var mutated = page.Scripting!.DispatchEvent(
            island, new MouseEvent("click", new EventInit(Bubbles: true, Cancelable: true)) { Button = 0 });

        mutated.Should().BeTrue("the status island listener should update the visible counter");
        clicks.TextContent.Should().Be("1");
    }

    [TestMethod]
    public async Task Pumping_runs_a_timer_scheduled_after_load()
    {
        // The setTimeout is scheduled by the event listener — i.e. AFTER load —
        // so only the live post-load pump can fire it (load-time pumping is done).
        const string html = """
            <!doctype html><html><body>
              <input id='q' type='text'>
              <div id='out'>none</div>
              <script>
                document.getElementById('q').addEventListener('input', function () {
                  setTimeout(function () {
                    document.getElementById('out').setAttribute('data-async', 'yes');
                  }, 50);
                });
              </script>
            </body></html>
            """;
        using var page = await LoadInteractiveAsync(html);
        var input = page.Document.GetElementById("q")!;
        var output = page.Document.GetElementById("out")!;

        page.Scripting!.DispatchEvent(input, new InputEvent("input", new EventInit(Bubbles: true)));
        output.GetAttribute("data-async").Should().BeNull("the timer hasn't come due yet");

        // Advance the live clock past the 50ms delay.
        var fired = page.Scripting!.PumpFrame(100);

        fired.Should().BeTrue("pumping past the delay fires the timer and mutates the DOM");
        output.GetAttribute("data-async").Should().Be("yes");
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<LaidOutPage> LoadInteractiveAsync(string html)
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"starling-live-{Guid.NewGuid():N}.html");
        File.WriteAllText(fixture, html);
        var engine = new StarlingEngine();
        // onFirstPaint non-null selects the progressive/interactive path that
        // retains the live realm on the returned page.
        var result = await engine.LayoutPageAsync(
            "file://" + fixture.Replace('\\', '/'), Options, CancellationToken.None, onFirstPaint: _ => { });
        result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
        return result.Value;
    }
}
