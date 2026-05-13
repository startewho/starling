using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Tessera.Layout.Box;

namespace Tessera.Layout.Tree;

/// <summary>
/// Builds the layout box tree from a DOM tree + a <see cref="StyleEngine"/>.
/// </summary>
internal sealed class BoxTreeBuilder
{
    private readonly StyleEngine _style;
    private readonly IImageResolver _images;

    public BoxTreeBuilder(StyleEngine style, IImageResolver? images = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
        _images = images ?? NullImageResolver.Instance;
    }

    public BlockBox Build(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.DocumentElement;
        if (root is null)
            return new BlockBox(style: null, element: null);

        var rootStyle = _style.Compute(root);
        var rootBox = new BlockBox(rootStyle, root);
        BuildChildren(root, rootStyle, rootBox);
        WrapInlinesInAnonymousBlocks(rootBox);
        return rootBox;
    }

    private void BuildChildren(Node parentNode, ComputedStyle parentStyle, Box.Box parentBox)
    {
        for (var child = parentNode.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case Element element:
                    var elementStyle = _style.Compute(element);
                    var display = DisplayKeyword(elementStyle);
                    if (display == "none") continue;
                    if (display == "contents")
                    {
                        BuildChildren(element, elementStyle, parentBox);
                        continue;
                    }
                    if (string.Equals(element.LocalName, "img", StringComparison.OrdinalIgnoreCase))
                    {
                        BuildImage(element, elementStyle, parentBox);
                        continue;
                    }
                    Box.Box box = display == "inline" || display == "inline-block"
                        ? new InlineBox(elementStyle, element)
                        : new BlockBox(elementStyle, element);
                    parentBox.AppendChild(box);
                    BuildChildren(element, elementStyle, box);
                    break;
                case Tessera.Dom.Text text:
                    var data = text.Data;
                    if (data.Length == 0) continue;
                    var textBox = new TextBox(data, parentStyle);
                    parentBox.AppendChild(textBox);
                    break;
            }
        }
    }

    /// <summary>
    /// CSS 2.2 §9.2.1.1: when a block container has both block and inline
    /// children, runs of consecutive inline children are wrapped in anonymous
    /// block boxes so block layout sees a uniform list of blocks.
    /// </summary>
    private static void WrapInlinesInAnonymousBlocks(Box.Box parent)
    {
        // AnonymousBlocks are the wrappers; don't re-wrap their (inline) children.
        if (parent.Kind != BoxKind.BlockContainer)
            return;

        // Always wrap inline runs in an anonymous block — even when the block
        // contains only inlines — so the block formatting context sees a
        // uniform list of block-level children. ImageBox (BoxKind.Replaced)
        // counts as inline content too: <img> is inline-replaced by default.
        var newChildren = new List<Box.Box>();
        AnonymousBlockBox? bucket = null;
        foreach (var child in parent.Children)
        {
            var isInline = child.Kind is BoxKind.Inline or BoxKind.Text or BoxKind.Replaced;
            if (isInline)
            {
                bucket ??= new AnonymousBlockBox(parent.Style);
                child.Parent = bucket;
                bucket.Children.Add(child);
            }
            else
            {
                if (bucket is not null && bucket.Children.Count > 0)
                {
                    newChildren.Add(bucket);
                    bucket.Parent = parent;
                    bucket = null;
                }
                newChildren.Add(child);
                child.Parent = parent;
            }
        }

        if (bucket is not null && bucket.Children.Count > 0)
        {
            bucket.Parent = parent;
            newChildren.Add(bucket);
        }

        parent.Children.Clear();
        parent.Children.AddRange(newChildren);

        foreach (var child in parent.Children) WrapInlinesInAnonymousBlocks(child);
    }

    private static string DisplayKeyword(ComputedStyle style)
        => style.Get(PropertyId.Display) is CssKeyword k ? k.Name.ToLowerInvariant() : "inline";

    /// <summary>
    /// Build an <c>&lt;img&gt;</c> as an <see cref="ImageBox"/> when the
    /// resolver has decoded bytes for it; otherwise degrade to a
    /// <see cref="TextBox"/> carrying the <c>alt</c> attribute (or nothing
    /// when <c>alt</c> is empty), matching the HTML spec's "missing image"
    /// behaviour at a minimum level.
    /// </summary>
    private void BuildImage(Element img, ComputedStyle style, Box.Box parentBox)
    {
        if (_images.TryResolve(img, out var resolved))
        {
            var (width, height) = ResolveImageSize(img, resolved);
            var box = new ImageBox(style, img, width, height, resolved.Source);
            parentBox.AppendChild(box);
            return;
        }

        var alt = img.GetAttribute("alt");
        if (string.IsNullOrEmpty(alt)) return;
        parentBox.AppendChild(new TextBox(alt, style));
    }

    /// <summary>
    /// HTML <c>width</c>/<c>height</c> attributes take precedence over the
    /// intrinsic dimensions; missing axes scale to preserve aspect ratio.
    /// CSS <c>width</c>/<c>height</c> support is deferred to a follow-up.
    /// </summary>
    private static (double Width, double Height) ResolveImageSize(Element img, ResolvedImage resolved)
    {
        var attrW = ParseDimensionAttribute(img.GetAttribute("width"));
        var attrH = ParseDimensionAttribute(img.GetAttribute("height"));
        var iw = resolved.Width > 0 ? resolved.Width : 1;
        var ih = resolved.Height > 0 ? resolved.Height : 1;

        if (attrW is { } w && attrH is { } h) return (w, h);
        if (attrW is { } onlyW) return (onlyW, onlyW * ih / iw);
        if (attrH is { } onlyH) return (onlyH * iw / ih, onlyH);
        return (iw, ih);
    }

    private static double? ParseDimensionAttribute(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // HTML allows trailing "px"; we accept that and bare integers/decimals.
        var trimmed = raw.Trim().TrimEnd('p', 'P', 'x', 'X');
        return double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : null;
    }
}
