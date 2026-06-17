using System.Collections.Frozen;
using Starling.Dom;

namespace Starling.Html.TreeBuilder;

public sealed partial class HtmlTreeBuilder
{
    // §13.2.4.2 "special" category — build-once/read-many, so frozen.
    private static readonly FrozenSet<string> HtmlSpecial = new[]
    {
        "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound",
        "blockquote", "body", "br", "button", "caption", "center", "col", "colgroup",
        "dd", "details", "dir", "div", "dl", "dt", "embed", "fieldset", "figcaption",
        "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5",
        "h6", "head", "header", "hgroup", "hr", "html", "iframe", "img", "input", "keygen",
        "li", "link", "listing", "main", "marquee", "menu", "meta", "nav", "noembed",
        "noframes", "noscript", "object", "ol", "p", "param", "plaintext", "pre", "script",
        "search", "section", "select", "source", "style", "summary", "table", "tbody",
        "td", "template", "textarea", "tfoot", "th", "thead", "title", "tr", "track",
        "ul", "wbr", "xmp",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static bool IsSpecial(Element e) => e.Namespace switch
    {
        HtmlNs => HtmlSpecial.Contains(e.LocalName),
        MathMlNs => e.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext" or "annotation-xml",
        SvgNs => e.LocalName is "foreignObject" or "desc" or "title",
        _ => false,
    };

    /// <summary>§13.2.4.3 "reconstruct the active formatting elements".</summary>
    private void ReconstructActiveFormattingElements()
    {
        if (_activeFormatting.Count == 0)
        {
            return;
        }

        if (_activeFormatting[^1].Element is not { } last || _openElements.Contains(last))
        {
            return;
        }

        var i = _activeFormatting.Count - 1;
        // Rewind.
        while (i > 0)
        {
            i--;
            var entry = _activeFormatting[i].Element;
            if (entry is null || _openElements.Contains(entry))
            {
                i++; // Advance to the entry after the marker/open element.
                break;
            }
        }

        for (; i < _activeFormatting.Count; i++)
        {
            if (_activeFormatting[i].Element is not { } entry)
            {
                continue;
            }
            var clone = CloneElement(entry);
            InsertElementAtAppropriatePlace(clone);
            _openElements.Push(clone);
            _activeFormatting.ReplaceAt(i, clone);
        }
    }

    private Element CloneElement(Element src)
    {
        Element clone = src.Namespace == HtmlNs
            ? _document.CreateElement(src.LocalName)
            : _document.CreateElementNS(src.Namespace, QualifiedName(src.Prefix, src.LocalName));
        foreach (var a in src.Attributes)
        {
            if (string.IsNullOrEmpty(a.Namespace))
            {
                clone.SetAttribute(a.LocalName, a.Value);
            }
            else
            {
                clone.SetAttributeNS(a.Namespace, QualifiedName(a.Prefix, a.LocalName), a.Value);
            }
        }
        return clone;
    }

    private static string QualifiedName(string? prefix, string localName)
        => string.IsNullOrEmpty(prefix) ? localName : $"{prefix}:{localName}";

    /// <summary>§13.2.6.4.7 "adoption agency algorithm".</summary>
    private void RunAdoptionAgency(string subject)
    {
        // Step 1: if the current node is an HTML element with the subject name
        // and is not in the AFE list, just pop it.
        var cur = _openElements.Current;
        if (cur is { Namespace: HtmlNs } && cur.LocalName == subject && !_activeFormatting.Contains(cur))
        {
            _openElements.Pop();
            return;
        }

        for (var outer = 0; outer < 8; outer++)
        {
            // Step: find the formatting element.
            var formattingElement = _activeFormatting.LastBeforeMarker(subject);
            if (formattingElement is null)
            {
                AnyOtherEndTag(new Tokenizer.EndTagToken(subject, [], false));
                return;
            }
            if (!_openElements.Contains(formattingElement))
            {
                _activeFormatting.Remove(formattingElement);
                return;
            }
            if (!_openElements.HasInScope(formattingElement))
            {
                return; // parse error.
            }

            var feIndex = _openElements.IndexOf(formattingElement);

            // Furthest block: topmost special node below formattingElement.
            Element? furthestBlock = null;
            for (var j = feIndex + 1; j < _openElements.Count; j++)
            {
                if (IsSpecial(_openElements[j])) { furthestBlock = _openElements[j]; break; }
            }

            if (furthestBlock is null)
            {
                // Pop up to and including the formatting element; remove from AFE.
                _openElements.PopUntilElement(formattingElement);
                _activeFormatting.Remove(formattingElement);
                return;
            }

            var commonAncestor = _openElements[feIndex - 1];
            var bookmark = _activeFormatting.IndexOf(formattingElement);

            var node = furthestBlock;
            var lastNode = furthestBlock;
            var nodeIndex = _openElements.IndexOf(node);

            var inner = 0;
            while (true)
            {
                inner++;
                // Move node up the stack.
                nodeIndex--;
                if (nodeIndex < 0)
                {
                    break;
                }

                node = _openElements[nodeIndex];
                if (node == formattingElement)
                {
                    break;
                }

                var afeIndex = _activeFormatting.IndexOf(node);
                if (inner > 3 && afeIndex >= 0)
                {
                    _activeFormatting.RemoveAt(afeIndex);
                    if (afeIndex < bookmark)
                    {
                        bookmark--;
                    }

                    afeIndex = -1;
                }

                if (afeIndex < 0)
                {
                    // Not in AFE: remove from the stack and continue.
                    _openElements.Remove(node);
                    // nodeIndex still points one above the removed slot for the next iteration.
                    continue;
                }

                // Create a clone, replacing the entry in both AFE and the stack.
                var clone = CloneElement(node);
                _activeFormatting.ReplaceAt(afeIndex, clone);
                var stackIdx = _openElements.IndexOf(node);
                _openElements.ReplaceAt(stackIdx, clone);
                node = clone;
                nodeIndex = stackIdx;

                if (lastNode == furthestBlock)
                {
                    bookmark = afeIndex + 1;
                }

                node.AppendChild(lastNode);
                lastNode = node;
            }

            // Step: place lastNode into the common ancestor (foster-parented when
            // the common ancestor is a table-context element).
            var (parent, before) = AppropriatePlaceForInserting(commonAncestor, forceFoster: true);
            parent.InsertBefore(lastNode, before);

            // Create a new element for the formatting element, move furthestBlock's
            // children into it, then append it to furthestBlock.
            var newElement = CloneElement(formattingElement);
            var fbChild = furthestBlock.FirstChild;
            while (fbChild is not null)
            {
                var next = fbChild.NextSibling;
                newElement.AppendChild(fbChild);
                fbChild = next;
            }
            furthestBlock.AppendChild(newElement);

            // Update the AFE list: remove the old FE entry, insert the new one at bookmark.
            var oldAfe = _activeFormatting.IndexOf(formattingElement);
            if (oldAfe >= 0)
            {
                _activeFormatting.RemoveAt(oldAfe);
                if (oldAfe < bookmark)
                {
                    bookmark--;
                }
            }
            if (bookmark < 0)
            {
                bookmark = 0;
            }

            if (bookmark > _activeFormatting.Count)
            {
                bookmark = _activeFormatting.Count;
            }

            _activeFormatting.Insert(bookmark, newElement);

            // Update the stack: remove FE, insert newElement just below furthestBlock.
            _openElements.Remove(formattingElement);
            var fbIdx = _openElements.IndexOf(furthestBlock);
            _openElements.InsertAt(fbIdx + 1, newElement);
        }
    }
}
