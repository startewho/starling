// SPDX-License-Identifier: Apache-2.0
using Starling.Dom;
using Starling.Html.TreeBuilder;

namespace Starling.Html;

/// <summary>
/// Default <see cref="IHtmlParserBackend"/>: the Starling tree builder. A thin
/// wrapper over <see cref="HtmlTreeBuilder"/> so the seam adds no behavior of its
/// own.
/// </summary>
public sealed class StarlingHtmlBackend : IHtmlParserBackend
{
    /// <inheritdoc/>
    public string Name => "starling";

    /// <inheritdoc/>
    public Document Parse(string html, bool scriptingEnabled)
        => HtmlTreeBuilder.Parse(html, scriptingEnabled);

    /// <inheritdoc/>
    public DocumentFragment ParseFragment(string markup, Element context, Document ownerDocument)
        => HtmlTreeBuilder.ParseFragment(markup, context, ownerDocument);
}
