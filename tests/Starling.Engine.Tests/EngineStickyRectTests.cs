// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using SixLabors.ImageSharp;

namespace Starling.Engine.Tests;

/// <summary>
/// WP5, Decision 2 (browser-plan/scroll-model.md): <c>getBoundingClientRect</c>
/// on a stuck element reports the STUCK position. The engine's rect path is
/// document-space ("viewport-relative at scroll 0"), so the stuck position is
/// the natural frame plus the store-computed sticky shift — the exact spot the
/// painter draws the box, pre scroll translation.
/// </summary>
[TestClass]
public sealed class EngineStickyRectTests
{
    private static readonly RenderOptions DefaultOptions = new(new Size(800, 600), 16f);

    [TestMethod]
    public async Task getBoundingClientRect_reports_the_stuck_position_after_a_scroll_write()
    {
        var html = @"<!doctype html><html><body style='margin:0'>
            <div id='sc' style='overflow:auto;width:200px;height:100px'>
              <div id='wrap'>
                <div style='height:40px'></div>
                <div id='st' style='position:sticky;top:0;height:20px'></div>
                <div style='height:340px'></div>
              </div>
            </div>
            <p id='out'>?</p>
            <script>
                var sc = document.getElementById('sc');
                var st = document.getElementById('st');
                var before = Math.round(st.getBoundingClientRect().top);
                sc.scrollTop = 100;
                var after = Math.round(st.getBoundingClientRect().top);
                var height = Math.round(st.getBoundingClientRect().height);
                document.getElementById('out').textContent =
                    'before=' + before + ' after=' + after + ' h=' + height;
            </script>
          </body></html>";

        var outcome = await RenderHtmlAsync(html);
        // Natural: 40px below the scroller's (document-origin) padding box.
        outcome.DisplayText.Should().Contain("before=40");
        // Stuck: scrolled 100, top:0 pins it — shift = 0 − (40 − 100) = 60,
        // so the document-space rect is 40 + 60 = 100 (port-relative 0).
        outcome.DisplayText.Should().Contain("after=100");
        // The shift never resizes the box.
        outcome.DisplayText.Should().Contain("h=20");
    }

    [TestMethod]
    public async Task getBoundingClientRect_of_a_child_rides_its_stuck_ancestor()
    {
        var html = @"<!doctype html><html><body style='margin:0'>
            <div id='sc' style='overflow:auto;width:200px;height:100px'>
              <div id='wrap'>
                <div style='height:40px'></div>
                <div id='st' style='position:sticky;top:0;height:20px'>
                  <div id='kid' style='height:10px'></div>
                </div>
                <div style='height:340px'></div>
              </div>
            </div>
            <p id='out'>?</p>
            <script>
                document.getElementById('sc').scrollTop = 100;
                var r = document.getElementById('kid').getBoundingClientRect();
                document.getElementById('out').textContent = 'kid=' + Math.round(r.top);
            </script>
          </body></html>";

        var outcome = await RenderHtmlAsync(html);
        outcome.DisplayText.Should().Contain("kid=100",
            "descendants of a stuck box report positions shifted by the ancestor's stick");
    }

    private static async Task<RenderOutcome> RenderHtmlAsync(string html)
    {
        var tempHtml = Path.Combine(Path.GetTempPath(), $"starling-sticky-{Guid.NewGuid():N}.html");
        var tempPng = Path.Combine(Path.GetTempPath(), $"starling-sticky-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(tempHtml, html, CancellationToken.None);
        try
        {
            var engine = new StarlingEngine();
            var url = new Uri(tempHtml).AbsoluteUri;
            var result = await engine.RenderAsync(url, DefaultOptions, tempPng, CancellationToken.None);
            result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
            return result.Value;
        }
        finally
        {
            try { File.Delete(tempHtml); } catch (IOException) { }
            try { File.Delete(tempPng); } catch (IOException) { }
        }
    }
}
