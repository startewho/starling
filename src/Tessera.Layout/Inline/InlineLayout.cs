using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Layout.Box;
using Tessera.Layout.Text;

namespace Tessera.Layout.Inline;

/// <summary>
/// Lays out an anonymous block's inline children into line boxes. Produces
/// <see cref="TextFragment"/> entries on each <see cref="TextBox"/> describing
/// where each piece appears on its parent's line.
/// </summary>
internal sealed class InlineLayout
{
    private readonly ITextMeasurer _measurer;

    public InlineLayout(ITextMeasurer measurer)
    {
        _measurer = measurer;
    }

    public double Layout(Box.Box container, double availableWidth)
    {
        var fontSize = ResolveFontSize(container.Style);
        var lineHeight = ResolveLineHeight(container.Style, fontSize);
        var baseline = _measurer.Baseline(fontSize);

        // Collect a flat sequence of inline-formatting items by walking the
        // container's inline subtree (text runs flatten through InlineBox
        // wrappers; <img> shows up as an ImageRun).
        var runs = new List<InlineRun>();
        Flatten(container, runs);

        // No content → zero height.
        if (runs.Count == 0) return 0;

        double cursorX = 0, cursorY = 0;
        double currentLineHeight = lineHeight;
        var fragments = new List<(TextBox Owner, int Index)>();
        var placedImages = new List<ImageBox>();

        foreach (var run in runs)
        {
            switch (run)
            {
                case TextRun text:
                    LayoutText(text, container.Style, availableWidth, baseline,
                        fragments, ref cursorX, ref cursorY, ref currentLineHeight);
                    break;
                case ImageRun image:
                    LayoutImage(image.Box, availableWidth,
                        placedImages, ref cursorX, ref cursorY, ref currentLineHeight);
                    break;
            }
        }

        AlignLines(container.Style, availableWidth, fragments, placedImages);
        return cursorY + currentLineHeight;
    }

    private void LayoutText(
        TextRun run,
        ComputedStyle? containerStyle,
        double availableWidth,
        double containerBaseline,
        List<(TextBox Owner, int Index)> fragments,
        ref double cursorX,
        ref double cursorY,
        ref double currentLineHeight)
    {
        var normalized = NormalizeWhitespace(run.Text);
        if (normalized.Length == 0) return;

        var localFontSize = ResolveFontSize(run.Style ?? containerStyle);
        var localLineHeight = ResolveLineHeight(run.Style ?? containerStyle, localFontSize);
        currentLineHeight = Math.Max(currentLineHeight, localLineHeight);

        foreach (var word in SplitToWords(normalized))
        {
            if (word.Length == 0) continue;
            var width = _measurer.MeasureWidth(word, localFontSize);
            var leadingSpace = word.StartsWith(" ", StringComparison.Ordinal);

            if (cursorX > 0 && cursorX + width > availableWidth)
            {
                cursorY += currentLineHeight;
                cursorX = 0;
                currentLineHeight = localLineHeight;
                if (leadingSpace)
                {
                    var trimmed = word.TrimStart(' ');
                    if (trimmed.Length == 0) continue;
                    var trimmedWidth = _measurer.MeasureWidth(trimmed, localFontSize);
                    AddFragment(run.Owner, fragments,
                        new TextFragment(trimmed, cursorX, cursorY, trimmedWidth, currentLineHeight, containerBaseline));
                    cursorX += trimmedWidth;
                    continue;
                }
            }

            AddFragment(run.Owner, fragments,
                new TextFragment(word, cursorX, cursorY, width, currentLineHeight, _measurer.Baseline(localFontSize)));
            cursorX += width;
        }
    }

    private static void LayoutImage(
        ImageBox image,
        double availableWidth,
        List<ImageBox> placedImages,
        ref double cursorX,
        ref double cursorY,
        ref double currentLineHeight)
    {
        var width = image.IntrinsicWidth;
        var height = image.IntrinsicHeight;

        // Wrap to the next line if this image won't fit and the current line
        // already has content. An image wider than the container stays on its
        // own line and overflows — matching the simple v1 "no overflow break"
        // behaviour of text.
        if (cursorX > 0 && cursorX + width > availableWidth)
        {
            cursorY += currentLineHeight;
            cursorX = 0;
            currentLineHeight = height;
        }
        else
        {
            currentLineHeight = Math.Max(currentLineHeight, height);
        }

        // v1 places the image top-aligned within its line. Baseline alignment
        // ("bottom of replaced element sits on text baseline") is a follow-up.
        image.Frame = new Rect(cursorX, cursorY, width, height);
        placedImages.Add(image);
        cursorX += width;
    }

    private static void AddFragment(TextBox owner, List<(TextBox Owner, int Index)> fragments, TextFragment fragment)
    {
        owner.Fragments.Add(fragment);
        fragments.Add((owner, owner.Fragments.Count - 1));
    }

    private static void AlignLines(
        ComputedStyle? style,
        double availableWidth,
        List<(TextBox Owner, int Index)> fragments,
        List<ImageBox> placedImages)
    {
        var align = style?.Get(PropertyId.TextAlign) is CssKeyword keyword
            ? keyword.Name.ToLowerInvariant()
            : "start";
        if (align is not ("center" or "right" or "end") || (fragments.Count == 0 && placedImages.Count == 0))
            return;

        // Group both text fragments and image boxes by their Y so per-line
        // alignment shifts apply uniformly to everything sitting on the line.
        var lines = new Dictionary<double, (List<(TextBox Owner, int Index)> Texts, List<ImageBox> Images, double RightEdge)>();
        foreach (var item in fragments)
        {
            var frag = item.Owner.Fragments[item.Index];
            var key = frag.Y;
            if (!lines.TryGetValue(key, out var line)) line = ([], [], 0);
            line.Texts.Add(item);
            line.RightEdge = Math.Max(line.RightEdge, frag.X + frag.Width);
            lines[key] = line;
        }
        foreach (var image in placedImages)
        {
            var key = image.Frame.Y;
            if (!lines.TryGetValue(key, out var line)) line = ([], [], 0);
            line.Images.Add(image);
            line.RightEdge = Math.Max(line.RightEdge, image.Frame.X + image.Frame.Width);
            lines[key] = line;
        }

        foreach (var (_, line) in lines)
        {
            var offset = align == "center"
                ? Math.Max(0, (availableWidth - line.RightEdge) / 2d)
                : Math.Max(0, availableWidth - line.RightEdge);
            if (offset == 0) continue;

            foreach (var item in line.Texts)
            {
                var fragment = item.Owner.Fragments[item.Index];
                item.Owner.Fragments[item.Index] = fragment with { X = fragment.X + offset };
            }
            foreach (var image in line.Images)
            {
                image.Frame = image.Frame with { X = image.Frame.X + offset };
            }
        }
    }

    private abstract record InlineRun;
    private sealed record TextRun(string Text, ComputedStyle? Style, TextBox Owner) : InlineRun;
    private sealed record ImageRun(ImageBox Box) : InlineRun;

    private static void Flatten(Box.Box box, List<InlineRun> runs)
    {
        foreach (var child in box.Children)
        {
            switch (child)
            {
                case TextBox tb:
                    tb.Fragments.Clear();
                    runs.Add(new TextRun(tb.Text, tb.Style, tb));
                    break;
                case ImageBox img:
                    runs.Add(new ImageRun(img));
                    break;
                case InlineBox ib:
                    Flatten(ib, runs);
                    break;
                default:
                    // Block-in-inline would land here; in v1 we just walk it as a sub-tree.
                    Flatten(child, runs);
                    break;
            }
        }
    }

    private static string NormalizeWhitespace(string text)
    {
        if (text.Length == 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        var prevSpace = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<string> SplitToWords(string text)
    {
        // Each word is one or more non-space chars, possibly preceded by a space.
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                if (i > start) yield return text[start..i];
                yield return " ";
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }

    private static double ResolveFontSize(ComputedStyle? style)
    {
        if (style is null) return 16;
        return style.Get(PropertyId.FontSize) switch
        {
            CssLength len => Block.BlockLayout.ToPx(len),
            CssNumber n => n.Value,
            _ => 16,
        };
    }

    private double ResolveLineHeight(ComputedStyle? style, double fontSize)
    {
        if (style is null) return _measurer.NormalLineHeight(fontSize);
        return style.Get(PropertyId.LineHeight) switch
        {
            CssNumber n => n.Value * fontSize,
            CssLength len => Block.BlockLayout.ToPx(len),
            CssPercentage pct => fontSize * pct.Value / 100d,
            CssKeyword k when k.Name == "normal" => _measurer.NormalLineHeight(fontSize),
            _ => _measurer.NormalLineHeight(fontSize),
        };
    }

}
