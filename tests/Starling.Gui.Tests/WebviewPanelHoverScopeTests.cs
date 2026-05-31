using Xunit;
using DomElement = Starling.Dom.Element;

namespace Starling.Gui.Tests;

/// <summary>
/// Regression guard for the "everything goes invisible while the mouse moves"
/// bug. Moving the pointer over a container used to re-cascade and override that
/// container's WHOLE subtree on every move. The override shadowed each
/// descendant's animated / laid-out style, so animated or freshly-styled content
/// flashed to its base state (often invisible) until the pointer stopped. The fix
/// prunes the override set to the elements whose :hover cascade actually changes a
/// paint-relevant property, leaving everyone else on their normal style.
/// </summary>
[Collection("Avalonia")]
public sealed class WebviewPanelHoverScopeTests
{
    [Fact]
    public void Hover_overrides_only_the_hover_styled_element_not_its_subtree()
    {
        // <section> changes background on :hover; the nested <div> has no
        // :hover-dependent style. Hovering the div makes <section> match
        // `section:hover` as an ancestor, but the div itself must not be pulled
        // into the override set.
        var html = "<!doctype html><html><head><style>" +
            "section { background: rgb(0,128,0); padding: 20px; }" +
            "section:hover { background: rgb(0,0,255); }" +
            "div { background: rgb(200,0,0); width: 50px; height: 50px; }" +
            "</style></head><body><section><div>x</div></section></body></html>";

        using var h = WebviewPanelHarness.Load(html);
        var section = h.QueryFirst("section");
        var div = h.QueryFirst("div");

        h.Hover(div);

        // The :hover-styled ancestor is overridden so its blue background paints.
        Assert.True(h.HoverScopeContains(section),
            "the :hover-styled container should be in the override set");

        // The descendant has no :hover rule — overriding it would shadow its
        // animated / laid-out style (the invisibility bug). It must stay out.
        Assert.False(h.HoverScopeContains(div),
            "a descendant with no :hover rule must not be overridden");

        // Only the genuinely :hover-affected element is overridden — not the
        // whole subtree (div) or the unaffected ancestors (body / html).
        Assert.Equal(1, h.HoverOverrideCount);
    }
}
