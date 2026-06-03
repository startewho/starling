// SPDX-License-Identifier: Apache-2.0
namespace Starling.Dom;

/// <summary>
/// The HTML <c>&lt;template&gt;</c> element (DOM §4.13.3 / HTML §4.12.3). Its
/// parsed children do not become normal child nodes — they live in a separate
/// <see cref="Content"/> fragment.
/// </summary>
/// <remarks>
/// Per spec the content fragment belongs to a distinct "template contents owner"
/// document with no browsing context and scripting disabled. We model that with
/// a fresh inert <see cref="Document"/>: it has no <c>NodeConnected</c> host
/// hook, so appending a <c>&lt;script&gt;</c> into template content never runs
/// it, and template content never participates in layout. Both the Starling
/// parser and the AngleSharp adapter fill this fragment, so the two backends
/// agree on <c>template.content</c>.
/// </remarks>
public sealed class HtmlTemplateElement : Element
{
    private DocumentFragment? _content;

    public HtmlTemplateElement(string tagName, string? @namespace = null)
        : base(tagName, @namespace)
    {
    }

    /// <summary>The template's content fragment, created on first access and
    /// owned by a dedicated inert document.</summary>
    public DocumentFragment Content => _content ??= new Document().CreateDocumentFragment();
}
