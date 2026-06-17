using System.Collections.Frozen;
using Starling.Dom;
using Starling.Html.Tokenizer;

namespace Starling.Html.TreeBuilder;

public sealed partial class HtmlTreeBuilder
{
    private static bool IsMathMlTextIntegrationPoint(Element e)
        => e.Namespace == MathMlNs && e.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext";

    private static bool IsHtmlIntegrationPoint(Element e)
    {
        if (e.Namespace == MathMlNs && e.LocalName == "annotation-xml")
        {
            var enc = e.GetAttribute("encoding");
            return enc is not null &&
                (enc.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
                 enc.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));
        }
        return e.Namespace == SvgNs && e.LocalName is "foreignObject" or "desc" or "title";
    }

    // HTML start tags that break out of foreign content (§13.2.6.5).
    private static readonly FrozenSet<string> ForeignBreakout = new[]
    {
        "b", "big", "blockquote", "body", "br", "center", "code", "dd", "div", "dl",
        "dt", "em", "embed", "h1", "h2", "h3", "h4", "h5", "h6", "head", "hr", "i",
        "img", "li", "listing", "menu", "meta", "nobr", "ol", "p", "pre", "ruby", "s",
        "small", "span", "strong", "strike", "sub", "sup", "table", "tt", "u", "ul", "var",
    }.ToFrozenSet(StringComparer.Ordinal);

    private void HandleForeignContent(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken { CodePoint: 0 }:
                InsertCharacter("�");
                return;
            case CharacterToken c:
                InsertCharacter(CodePointToString(c.CodePoint));
                if (!IsWhitespaceChar(c.CodePoint))
                {
                    _framesetOk = false;
                }

                return;
            case CommentToken comment:
                InsertComment(comment);
                return;
            case DoctypeToken:
                return;

            case StartTagToken start when ForeignBreakout.Contains(start.Name)
                                          || (start.Name == "font" && HasFontBreakoutAttr(start)):
                // Breakout: pop back into HTML/integration content, then reprocess.
                while (!_openElements.IsEmpty)
                {
                    var cur = _openElements.Current;
                    if (cur.Namespace == HtmlNs || IsMathMlTextIntegrationPoint(cur) || IsHtmlIntegrationPoint(cur))
                    {
                        break;
                    }

                    _openElements.Pop();
                }
                // Reprocess using the current insertion mode's HTML rules directly
                // (not the foreign dispatcher — in the fragment case the adjusted
                // current node would route straight back here).
                ProcessUsingInsertionMode(token);
                return;

            case StartTagToken start:
                {
                    // The dispatcher only routes here with a foreign (non-null)
                    // adjusted current node, so the new element inherits its namespace.
                    if (AdjustedCurrentNode is { } acn)
                    {
                        InsertForeignElement(start, acn.Namespace);
                        if (start.SelfClosing)
                        {
                            _openElements.Pop();
                        }
                    }
                }
                return;

            case EndTagToken { Name: "script" } when _openElements.Current is { Namespace: SvgNs, LocalName: "script" }:
                _openElements.Pop();
                return;

            case EndTagToken end:
                ForeignEndTag(end);
                return;
        }
    }

    private static bool HasFontBreakoutAttr(StartTagToken token)
    {
        foreach (var a in token.Attributes)
        {
            if (a.Name is "color" or "face" or "size")
            {
                return true;
            }
        }

        return false;
    }

    private void ForeignEndTag(EndTagToken end)
    {
        var i = _openElements.Count - 1;
        var node = _openElements[i];
        // (parse error if names mismatch — not tracked for the tree result.)
        while (true)
        {
            // The token name is already lowercased by the tokenizer; compare
            // case-insensitively so a foreign element's mixed-case local name
            // (e.g. foreignObject) matches without allocating a lowercased copy.
            if (string.Equals(node.LocalName, end.Name, StringComparison.OrdinalIgnoreCase))
            {
                _openElements.PopUntilElement(node);
                return;
            }
            i--;
            if (i < 0)
            {
                return;
            }

            node = _openElements[i];
            if (node.Namespace == HtmlNs)
            {
                ProcessUsingInsertionMode(end);
                return;
            }
        }
    }

    private void InsertForeignElement(StartTagToken token, string @namespace)
    {
        var localName = @namespace == SvgNs && SvgTagNames.TryGetValue(token.Name, out var adjusted)
            ? adjusted
            : token.Name;

        var element = _document.CreateElementNS(@namespace, localName);
        foreach (var attr in token.Attributes)
        {
            ApplyForeignAttribute(element, @namespace, attr);
        }

        InsertElementAtAppropriatePlace(element);
        _openElements.Push(element);
    }

    private static void ApplyForeignAttribute(Element element, string @namespace, HtmlAttribute attr)
    {
        // §13.2.6.5 adjust foreign attributes: a handful map into the xlink/xml/
        // xmlns namespaces with a prefixed qualified name.
        if (ForeignAttrNamespaces.TryGetValue(attr.Name, out var mapped))
        {
            element.SetAttributeNS(mapped.ns, mapped.qualified, attr.Value);
            return;
        }

        var name = attr.Name;
        if (@namespace == MathMlNs && name == "definitionurl")
        {
            name = "definitionURL";
        }
        else if (@namespace == SvgNs && SvgAttrNames.TryGetValue(name, out var svgName))
        {
            name = svgName;
        }

        if (name == attr.Name)
        {
            element.SetAttribute(name, attr.Value); // no case change needed.
        }
        else
        {
            element.SetAttributeNS(null, name, attr.Value); // preserve adjusted case.
        }
    }

    private static readonly FrozenDictionary<string, (string? ns, string qualified)> ForeignAttrNamespaces = new Dictionary<string, (string? ns, string qualified)>(StringComparer.Ordinal)
    {
        ["xlink:actuate"] = (XLinkNs, "xlink:actuate"),
        ["xlink:arcrole"] = (XLinkNs, "xlink:arcrole"),
        ["xlink:href"] = (XLinkNs, "xlink:href"),
        ["xlink:role"] = (XLinkNs, "xlink:role"),
        ["xlink:show"] = (XLinkNs, "xlink:show"),
        ["xlink:title"] = (XLinkNs, "xlink:title"),
        ["xlink:type"] = (XLinkNs, "xlink:type"),
        ["xml:lang"] = (XmlNs, "xml:lang"),
        ["xml:space"] = (XmlNs, "xml:space"),
        ["xmlns"] = (XmlNsNs, "xmlns"),
        ["xmlns:xlink"] = (XmlNsNs, "xmlns:xlink"),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> SvgTagNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["altglyph"] = "altGlyph",
        ["altglyphdef"] = "altGlyphDef",
        ["altglyphitem"] = "altGlyphItem",
        ["animatecolor"] = "animateColor",
        ["animatemotion"] = "animateMotion",
        ["animatetransform"] = "animateTransform",
        ["clippath"] = "clipPath",
        ["feblend"] = "feBlend",
        ["fecolormatrix"] = "feColorMatrix",
        ["fecomponenttransfer"] = "feComponentTransfer",
        ["fecomposite"] = "feComposite",
        ["feconvolvematrix"] = "feConvolveMatrix",
        ["fediffuselighting"] = "feDiffuseLighting",
        ["fedisplacementmap"] = "feDisplacementMap",
        ["fedistantlight"] = "feDistantLight",
        ["fedropshadow"] = "feDropShadow",
        ["feflood"] = "feFlood",
        ["fefunca"] = "feFuncA",
        ["fefuncb"] = "feFuncB",
        ["fefuncg"] = "feFuncG",
        ["fefuncr"] = "feFuncR",
        ["fegaussianblur"] = "feGaussianBlur",
        ["feimage"] = "feImage",
        ["femerge"] = "feMerge",
        ["femergenode"] = "feMergeNode",
        ["femorphology"] = "feMorphology",
        ["feoffset"] = "feOffset",
        ["fepointlight"] = "fePointLight",
        ["fespecularlighting"] = "feSpecularLighting",
        ["fespotlight"] = "feSpotLight",
        ["fetile"] = "feTile",
        ["feturbulence"] = "feTurbulence",
        ["foreignobject"] = "foreignObject",
        ["glyphref"] = "glyphRef",
        ["lineargradient"] = "linearGradient",
        ["radialgradient"] = "radialGradient",
        ["textpath"] = "textPath",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> SvgAttrNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["attributename"] = "attributeName",
        ["attributetype"] = "attributeType",
        ["basefrequency"] = "baseFrequency",
        ["baseprofile"] = "baseProfile",
        ["calcmode"] = "calcMode",
        ["clippathunits"] = "clipPathUnits",
        ["diffuseconstant"] = "diffuseConstant",
        ["edgemode"] = "edgeMode",
        ["filterunits"] = "filterUnits",
        ["glyphref"] = "glyphRef",
        ["gradienttransform"] = "gradientTransform",
        ["gradientunits"] = "gradientUnits",
        ["kernelmatrix"] = "kernelMatrix",
        ["kernelunitlength"] = "kernelUnitLength",
        ["keypoints"] = "keyPoints",
        ["keysplines"] = "keySplines",
        ["keytimes"] = "keyTimes",
        ["lengthadjust"] = "lengthAdjust",
        ["limitingconeangle"] = "limitingConeAngle",
        ["markerheight"] = "markerHeight",
        ["markerunits"] = "markerUnits",
        ["markerwidth"] = "markerWidth",
        ["maskcontentunits"] = "maskContentUnits",
        ["maskunits"] = "maskUnits",
        ["numoctaves"] = "numOctaves",
        ["pathlength"] = "pathLength",
        ["patterncontentunits"] = "patternContentUnits",
        ["patterntransform"] = "patternTransform",
        ["patternunits"] = "patternUnits",
        ["pointsatx"] = "pointsAtX",
        ["pointsaty"] = "pointsAtY",
        ["pointsatz"] = "pointsAtZ",
        ["preservealpha"] = "preserveAlpha",
        ["preserveaspectratio"] = "preserveAspectRatio",
        ["primitiveunits"] = "primitiveUnits",
        ["refx"] = "refX",
        ["refy"] = "refY",
        ["repeatcount"] = "repeatCount",
        ["repeatdur"] = "repeatDur",
        ["requiredextensions"] = "requiredExtensions",
        ["requiredfeatures"] = "requiredFeatures",
        ["specularconstant"] = "specularConstant",
        ["specularexponent"] = "specularExponent",
        ["spreadmethod"] = "spreadMethod",
        ["startoffset"] = "startOffset",
        ["stddeviation"] = "stdDeviation",
        ["stitchtiles"] = "stitchTiles",
        ["surfacescale"] = "surfaceScale",
        ["systemlanguage"] = "systemLanguage",
        ["tablevalues"] = "tableValues",
        ["targetx"] = "targetX",
        ["targety"] = "targetY",
        ["textlength"] = "textLength",
        ["viewbox"] = "viewBox",
        ["viewtarget"] = "viewTarget",
        ["xchannelselector"] = "xChannelSelector",
        ["ychannelselector"] = "yChannelSelector",
        ["zoomandpan"] = "zoomAndPan",
    }.ToFrozenDictionary(StringComparer.Ordinal);
}
