// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Starling.Common.Image;

namespace Starling.Paint.Svg;

/// <summary>
/// A pure-managed first-cut SVG rasterizer. Parses an SVG document and renders
/// the shapes real-world icons use into a backend-neutral
/// <see cref="DecodedImage"/> (straight RGBA8888, top-down, tightly packed),
/// matching the contract the OS-native raster decoders produce — so the engine
/// can treat a decoded SVG exactly like a decoded PNG.
/// </summary>
/// <remarks>
/// <para>
/// Rasterization runs on ImageSharp.Drawing 3 (already the engine's paint
/// backend), keeping this firmly inside the managed-first interop policy: no
/// native code, no new dependency. It lives in <c>Starling.Paint</c> — the only
/// engine project that references ImageSharp.Drawing — rather than in the native
/// interop seam <c>Starling.Codecs</c>, which references only
/// <c>Starling.Common</c> and must not take a draw dependency.
/// </para>
/// <para>
/// Supported: <c>&lt;svg&gt;</c> (width/height/viewBox), <c>&lt;g&gt;</c>,
/// <c>&lt;path&gt;</c>, <c>&lt;rect&gt;</c> (incl. distinct rx/ry),
/// <c>&lt;circle&gt;</c>, <c>&lt;ellipse&gt;</c>, <c>&lt;line&gt;</c>,
/// <c>&lt;polyline&gt;</c>, <c>&lt;polygon&gt;</c>, <c>&lt;use&gt;</c>,
/// <c>&lt;symbol&gt;</c>; fill/stroke/stroke-width/fill-rule/opacity via
/// presentation attributes, <c>style="…"</c>, and flat <c>&lt;style&gt;</c>
/// class rules; <c>transform</c>; hex/rgb()/hsl()/named/currentColor colors;
/// <c>linearGradient</c>/<c>radialGradient</c> paint servers;
/// stroke-dasharray/dashoffset/linecap/linejoin/miterlimit; group opacity
/// compositing. Unknown elements and attributes are ignored gracefully.
/// </para>
/// <para>
/// Deferred (out of scope): <c>&lt;text&gt;</c>, <c>&lt;filter&gt;</c>,
/// <c>&lt;image&gt;</c>, markers, <c>&lt;clipPath&gt;</c>,
/// <c>patternUnits="objectBoundingBox"</c>.
/// </para>
/// </remarks>
public static class SvgImageDecoder
{
    /// <summary>Default intrinsic size when neither width/height nor viewBox is present (CSS replaced-element default).</summary>
    private const int DefaultDimension = 150;

    /// <summary>Clamp the rasterized canvas so a hostile/huge viewBox can't OOM.</summary>
    private const int MaxDimension = 4096;

    /// <summary>
    /// Decode SVG <paramref name="utf8"/> bytes into a <see cref="DecodedImage"/>.
    /// <paramref name="currentColor"/> resolves <c>currentColor</c> references
    /// (defaults to opaque black). Throws <see cref="SvgDecodeException"/> on
    /// malformed XML or a document with no rasterizable root.
    /// </summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> utf8, Color? currentColor = null)
        => DecodeText(DecodeText(utf8), currentColor);

    /// <summary>Decode from an already-decoded SVG source string.</summary>
    public static DecodedImage DecodeText(string svg, Color? currentColor = null)
    {
        var root = ParseRoot(svg);

        // --- intrinsic size + viewBox → user-space transform -----------------
        ViewBox? viewBox = ParseViewBox(Attr(root, "viewBox"));
        float? widthAttr = ParseLength(Attr(root, "width"));
        float? heightAttr = ParseLength(Attr(root, "height"));

        float intrinsicW = widthAttr ?? viewBox?.Width ?? DefaultDimension;
        float intrinsicH = heightAttr ?? viewBox?.Height ?? DefaultDimension;

        int pxW = Math.Clamp((int)MathF.Ceiling(intrinsicW <= 0 ? DefaultDimension : intrinsicW), 1, MaxDimension);
        int pxH = Math.Clamp((int)MathF.Ceiling(intrinsicH <= 0 ? DefaultDimension : intrinsicH), 1, MaxDimension);

        // Map the viewBox into the viewport. preserveAspectRatio defaults to
        // "xMidYMid meet": a uniform scale that fits, then centering.
        Matrix3x2 viewportTransform = Matrix3x2.Identity;
        if (viewBox is { } vb && vb.Width > 0 && vb.Height > 0)
        {
            float s = MathF.Min(pxW / vb.Width, pxH / vb.Height);
            float tx = -vb.MinX * s + (pxW - vb.Width * s) / 2f;
            float ty = -vb.MinY * s + (pxH - vb.Height * s) / 2f;
            viewportTransform = new Matrix3x2(s, 0, 0, s, tx, ty);
        }

        // --- collect <style> class rules -------------------------------------
        var sheet = new SvgStyleSheet();
        CollectStyles(root, sheet);

        // --- collect paint servers (<pattern>, <linearGradient>,
        // <radialGradient>) by id, and all elements with an id so <use>
        // can find them; the viewport size resolves percentage geometry --------
        var (paintServers, elementsById) = CollectPaintServers(root);
        var viewport = new Vector2(
            viewBox?.Width ?? intrinsicW,
            viewBox?.Height ?? intrinsicH);
        var renderCtx = new SvgRenderContext(sheet, paintServers, elementsById, viewport);

        var rootStyle = new SvgStyle { CurrentColor = currentColor ?? Color.Black };

        // --- render via the ImageSharp.Drawing canvas ------------------------
        // The DrawingCanvas Save(options) REPLACES the current transform rather
        // than composing it, so we compose transforms ourselves and always pass
        // the full accumulated matrix down the tree (mirroring how
        // ImageSharpBackend threads its own transform stack).
        using var image = new Image<Rgba32>(pxW, pxH, new Rgba32(0, 0, 0, 0));
        image.Mutate(imageCtx => imageCtx.Paint(canvas =>
        {
            // Render the <svg> root as an element (not just its children) so its
            // own presentation attributes — `fill="none"`, `stroke`,
            // `stroke-width`, … — apply and inherit down the tree. Many icons
            // (e.g. stroke-only line icons with fill="none" stroke="currentColor"
            // on the root) otherwise fall back to the default black fill and
            // paint as a solid blob.
            RenderElement(canvas, root, rootStyle, viewportTransform, renderCtx);
        }));

        // Pattern tiles and gradient offscreen layers were sampled lazily
        // during the Mutate above; now that rasterization is complete they
        // can be released.
        foreach (var disposable in renderCtx.Pending)
            disposable.Dispose();

        var buffer = new byte[checked(pxW * pxH * 4)];
        image.CopyPixelDataTo(buffer);
        return DecodedImage.FromBuffer(pxW, pxH, buffer);
    }

    private static XElement ParseRoot(string svg)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore, // tolerate <!DOCTYPE svg ...>
                XmlResolver = null,                   // never fetch external DTDs/entities
                CheckCharacters = false,
            };
            using var reader = XmlReader.Create(new System.IO.StringReader(svg), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new SvgDecodeException($"SVG is not well-formed XML: {ex.Message}", ex);
        }

        var root = doc.Root;
        if (root is null || !root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
            throw new SvgDecodeException("SVG document has no <svg> root element.");
        return root;
    }

    private static void RenderChildren(
        DrawingCanvas canvas, XElement parent, SvgStyle parentStyle, Matrix3x2 transform, SvgRenderContext ctx)
    {
        foreach (var el in parent.Elements())
            RenderElement(canvas, el, parentStyle, transform, ctx);
    }

    private static void RenderElement(
        DrawingCanvas canvas, XElement el, SvgStyle parentStyle, Matrix3x2 parentTransform, SvgRenderContext ctx)
    {
        string name = el.Name.LocalName;

        // Skip non-rendered containers (but their <style> was already harvested).
        // <pattern> / <linearGradient> / <radialGradient> are paint servers:
        // their content is resolved only when a shape references them via
        // fill="url(#id)", never inline. <symbol> is rendered via <use>.
        if (name is "defs" or "style" or "title" or "desc" or "metadata" or "symbol"
            or "clipPath" or "mask" or "pattern" or "linearGradient" or "radialGradient" or "filter")
            return;

        // Resolve cascaded style: clone parent, apply class rules, then
        // presentation attributes, then style="…" (highest priority).
        var style = parentStyle.Clone();
        if (!ctx.Sheet.IsEmpty)
            ctx.Sheet.Apply(style, name, Attr(el, "class"));
        ApplyPresentationAttributes(style, el);
        style.ApplyStyleString(Attr(el, "style"));

        // Compose this element's local transform onto the inherited one. SVG
        // applies the local transform first to a point, so for row-vector
        // matrices (point * M) the composition is local * parent.
        var transform = SvgTransform.Parse(Attr(el, "transform")) * parentTransform;

        switch (name)
        {
            case "g":
            case "svg":
            case "a":
                // Group opacity compositing: a <g opacity=…> where opacity < 1
                // must composite children into an offscreen layer first, then
                // blend at the group opacity. Otherwise overlapping children
                // double-blend through the inherited per-child Opacity.
                // SVG 1.1 §14.6 / Compositing and Blending §7.
                if (style.GroupOpacity < 1f - float.Epsilon && name == "g")
                    RenderGroupWithOpacity(canvas, el, style, transform, ctx);
                else
                    RenderChildren(canvas, el, style, transform, ctx);
                break;
            case "use":
                RenderUse(canvas, el, style, transform, ctx);
                break;
            case "path":
                DrawShape(canvas, SvgPathParser.Parse(Attr(el, "d")), style, transform, ctx);
                break;
            case "rect":
                DrawShape(canvas, BuildRect(el, ctx.Viewport), style, transform, ctx);
                break;
            case "circle":
                DrawShape(canvas, BuildCircle(el, ctx.Viewport), style, transform, ctx);
                break;
            case "ellipse":
                DrawShape(canvas, BuildEllipse(el, ctx.Viewport), style, transform, ctx);
                break;
            case "line":
                DrawShape(canvas, BuildLine(el, ctx.Viewport), style, transform, ctx, strokeOnly: true);
                break;
            case "polyline":
                DrawShape(canvas, BuildPoly(el, close: false), style, transform, ctx);
                break;
            case "polygon":
                DrawShape(canvas, BuildPoly(el, close: true), style, transform, ctx);
                break;
            default:
                // Unknown element: ignore but descend (some wrappers, e.g.
                // <switch>, hold renderable children).
                RenderChildren(canvas, el, style, transform, ctx);
                break;
        }
    }

    /// <summary>
    /// Render a <c>&lt;g opacity="…"&gt;</c> group into an offscreen layer,
    /// then blend the layer onto the canvas at the group opacity.
    /// SVG 1.1 §14.6 — group opacity forms an isolated compositing group.
    /// Uses <c>DrawingCanvas.SaveLayer</c> to open an isolated blend
    /// group and <c>DrawingCanvas.Restore</c> to composite it back.
    /// </summary>
    private static void RenderGroupWithOpacity(
        DrawingCanvas canvas, XElement el, SvgStyle style, Matrix3x2 transform, SvgRenderContext ctx)
    {
        float alpha = Math.Clamp(style.GroupOpacity, 0f, 1f);

        // Open an isolated compositing group; SaveLayer composites it at
        // BlendPercentage when Restore is called.
        canvas.SaveLayer(new SixLabors.ImageSharp.GraphicsOptions
        {
            BlendPercentage = alpha,
        });

        // Reset BOTH Opacity and GroupOpacity so children render fully opaque
        // inside the layer; the group opacity is applied at layer-flatten time
        // by Restore(), not by each child's own alpha multiplication.
        var childStyle = style.Clone();
        childStyle.Opacity = 1f;
        childStyle.GroupOpacity = 1f;

        RenderChildren(canvas, el, childStyle, transform, ctx);

        canvas.Restore();
    }

    /// <summary>
    /// Resolve a <c>&lt;use href="#id" x="…" y="…"/&gt;</c> reference and
    /// render the referenced element at the (x, y) offset. Handles both
    /// regular element references and <c>&lt;symbol&gt;</c> (renders its
    /// children). SVG 1.1 §5.6.
    /// </summary>
    private static void RenderUse(
        DrawingCanvas canvas, XElement useEl, SvgStyle style, Matrix3x2 transform, SvgRenderContext ctx)
    {
        // xlink:href and plain href are both valid in SVG 1.1/2.
        var href = Attr(useEl, "href") ?? Attr(useEl, "xlink:href");
        if (href is null || !href.StartsWith('#'))
            return;
        var refId = href[1..].Trim();
        if (refId.Length == 0)
            return;
        if (!ctx.ElementsById.TryGetValue(refId, out var refEl))
            return;

        float x = ParseLengthPct(Attr(useEl, "x"), ctx.Viewport.X) ?? 0;
        float y = ParseLengthPct(Attr(useEl, "y"), ctx.Viewport.Y) ?? 0;

        // Apply the (x,y) offset as an additional translation.
        var useTransform = Matrix3x2.CreateTranslation(x, y) * transform;

        string refName = refEl.Name.LocalName;
        if (refName.Equals("symbol", StringComparison.OrdinalIgnoreCase))
        {
            // A <symbol> provides its own isolated viewport; render its children.
            RenderChildren(canvas, refEl, style, useTransform, ctx);
        }
        else
        {
            RenderElement(canvas, refEl, style, useTransform, ctx);
        }
    }

    private static void DrawShape(
        DrawingCanvas canvas, IPath? path, SvgStyle style, Matrix3x2 transform, SvgRenderContext ctx, bool strokeOnly = false)
    {
        if (path is null)
            return;

        bool hasFillRef = style.FillRef is not null;

        if (!strokeOnly && hasFillRef)
        {
            var refId = style.FillRef!;
            if (ctx.PaintServers.TryGetValue(refId, out var server))
            {
                string serverName = server.Name.LocalName;
                if (serverName.Equals("pattern", StringComparison.OrdinalIgnoreCase))
                {
                    FillWithPattern(canvas, path, server, style, transform, ctx);
                }
                else if (serverName.Equals("linearGradient", StringComparison.OrdinalIgnoreCase)
                      || serverName.Equals("radialGradient", StringComparison.OrdinalIgnoreCase))
                {
                    FillWithGradient(canvas, path, server, style, transform, ctx);
                }
                // else: unsupported server type — check for fallback below
            }
            else if (style.FillFallback is { } fallback)
            {
                // url(#missing) fallback: paint the fallback color.
                var fc = ApplyAlpha(fallback, style.FillOpacity * style.Opacity);
                if (fc is not null)
                {
                    canvas.Save(new DrawingOptions
                    {
                        Transform = To4x4(transform),
                        ShapeOptions = new ShapeOptions
                        {
                            IntersectionRule = style.FillEvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,
                        },
                    });
                    canvas.Fill(Brushes.Solid(fc.Value), path);
                    canvas.Restore();
                }
            }
            // no fallback → paint nothing (consistent with original behaviour)
        }

        // When a fill reference is present (resolved above or not), it replaces
        // the solid fill — an unresolved reference with no fallback paints nothing.
        var fill = strokeOnly || hasFillRef ? null : style.EffectiveFill();
        if (fill is { } fc2)
        {
            canvas.Save(new DrawingOptions
            {
                Transform = To4x4(transform),
                ShapeOptions = new ShapeOptions
                {
                    IntersectionRule = style.FillEvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,
                },
            });
            canvas.Fill(Brushes.Solid(fc2), path);
            canvas.Restore();
        }

        // Stroke: respect stroke ref too (gradient/pattern strokes).
        if (style.StrokeRef is not null)
        {
            var refId = style.StrokeRef!;
            if (ctx.PaintServers.TryGetValue(refId, out var server) && style.StrokeWidth > 0)
            {
                string serverName = server.Name.LocalName;
                if (serverName.Equals("linearGradient", StringComparison.OrdinalIgnoreCase)
                 || serverName.Equals("radialGradient", StringComparison.OrdinalIgnoreCase))
                {
                    StrokeWithGradient(canvas, path, server, style, transform, ctx);
                }
            }
        }
        else
        {
            var stroke = style.EffectiveStroke();
            if (stroke is { } sc && style.StrokeWidth > 0)
            {
                var pen = BuildPen(sc, style);
                canvas.Save(new DrawingOptions { Transform = To4x4(transform) });
                canvas.Draw(pen, path);
                canvas.Restore();
            }
        }
    }

    /// <summary>
    /// Build a dashed pen from a solid color and SVG stroke style properties.
    /// Returns a <see cref="PatternPen"/> when a dash array is set, otherwise
    /// a plain <see cref="SolidPen"/>.
    /// </summary>
    private static Pen BuildPen(Color color, SvgStyle style)
    {
        var strokeOpts = new StrokeOptions
        {
            LineCap = style.StrokeLineCap,
            LineJoin = style.StrokeLineJoin,
            MiterLimit = style.StrokeMiterLimit,
        };

        // stroke-dasharray: encode the dash/gap sequence via PatternPen.
        // SVG dash values are in user units; PenOptions.StrokePattern stores
        // unit-relative ratios (pattern / strokeWidth). SVG 1.1 §11.4.
        if (style.StrokeDashArray is { Length: > 0 } dashes)
        {
            float w = style.StrokeWidth;
            if (w <= 0) w = 1f;
            // PenOptions(Color, width, pattern[]) — pattern values are absolute pixel lengths.
            float[] pattern = new float[dashes.Length];
            for (int i = 0; i < dashes.Length; i++)
                pattern[i] = Math.Max(0.001f, dashes[i]); // avoid zero-length segments
            var opts = new PenOptions(color, w, pattern) { StrokeOptions = strokeOpts };
            return new PatternPen(opts);
        }

        return new SolidPen(new PenOptions(color, style.StrokeWidth) { StrokeOptions = strokeOpts });
    }

    /// <summary>
    /// Paint <paramref name="path"/> with an SVG <c>&lt;pattern&gt;</c> paint
    /// server: rasterize one pattern tile, then tile it across the shape with an
    /// <see cref="ImageBrush"/> (which repeats the source image and is clipped to
    /// the fill path). Only the common <c>patternUnits="userSpaceOnUse"</c> form
    /// with a numeric tile <c>width</c>/<c>height</c> is supported; anything else
    /// leaves the fill unpainted rather than guessing.
    /// </summary>
    private static void FillWithPattern(
        DrawingCanvas canvas, IPath path, XElement pattern, SvgStyle style, Matrix3x2 transform, SvgRenderContext ctx)
    {
        var units = Attr(pattern, "patternUnits") ?? "objectBoundingBox";
        if (!units.Equals("userSpaceOnUse", StringComparison.OrdinalIgnoreCase))
            return;

        float? tileW = ParseLength(Attr(pattern, "width"));
        float? tileH = ParseLength(Attr(pattern, "height"));
        if (tileW is not > 0 || tileH is not > 0)
            return;

        // Device scale of this shape (x-axis length of its transform). The tile
        // is rasterized at that resolution so it stays crisp when the shape is
        // scaled by the viewport transform.
        float scale = MathF.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        if (scale <= 0) scale = 1f;

        int tilePxW = Math.Clamp((int)MathF.Ceiling(tileW.Value * scale), 1, MaxDimension);
        int tilePxH = Math.Clamp((int)MathF.Ceiling(tileH.Value * scale), 1, MaxDimension);

        float patternX = ParseLength(Attr(pattern, "x")) ?? 0;
        float patternY = ParseLength(Attr(pattern, "y")) ?? 0;

        // Map pattern content (userSpaceOnUse) into tile pixels: shift the tile
        // origin to (0,0), then scale to device resolution.
        var tileTransform =
            Matrix3x2.CreateTranslation(-patternX, -patternY) * Matrix3x2.CreateScale(scale);
        var contentStyle = new SvgStyle { CurrentColor = style.CurrentColor };

        // The brush samples this tile when the outer canvas timeline executes
        // (after this method returns), so it must outlive the call — stage it for
        // disposal once DecodeText finishes rendering.
        var tile = new Image<Rgba32>(tilePxW, tilePxH, new Rgba32(0, 0, 0, 0));
        ctx.Pending.Add(tile);
        tile.Mutate(c => c.Paint(tileCanvas =>
        {
            foreach (var child in pattern.Elements())
                RenderElement(tileCanvas, child, contentStyle, tileTransform, ctx);
        }));

        // Fill the shape (mapped to device space) with the repeating tile. The
        // path itself clips the brush to the shape; ImageBrush wraps the tile to
        // cover the region.
        var devicePath = path.Transform(To4x4(transform));
        canvas.Save(new DrawingOptions
        {
            ShapeOptions = new ShapeOptions
            {
                IntersectionRule = style.FillEvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,
            },
        });
        canvas.Fill(new ImageBrush<Rgba32>(tile), devicePath);
        canvas.Restore();
    }

    /// <summary>
    /// Paint <paramref name="path"/> with an SVG gradient paint server
    /// (<c>&lt;linearGradient&gt;</c> or <c>&lt;radialGradient&gt;</c>).
    /// Supports <c>gradientUnits</c> (objectBoundingBox default +
    /// userSpaceOnUse), <c>gradientTransform</c>, <c>spreadMethod</c>
    /// (pad/reflect/repeat), stop color/offset/opacity, and
    /// <c>xlink:href</c>/<c>href</c> stop inheritance. SVG 1.1 §13.
    /// </summary>
    private static void FillWithGradient(
        DrawingCanvas canvas, IPath path, XElement gradEl, SvgStyle style, Matrix3x2 transform, SvgRenderContext ctx)
    {
        var brush = BuildGradientBrush(gradEl, path, transform, ctx, forFill: true);
        if (brush is null)
            return;

        var devicePath = path.Transform(To4x4(transform));
        canvas.Save(new DrawingOptions
        {
            ShapeOptions = new ShapeOptions
            {
                IntersectionRule = style.FillEvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,
            },
        });
        canvas.Fill(brush, devicePath);
        canvas.Restore();
    }

    /// <summary>Stroke <paramref name="path"/> with a gradient paint server.</summary>
    private static void StrokeWithGradient(
        DrawingCanvas canvas, IPath path, XElement gradEl, SvgStyle style, Matrix3x2 transform, SvgRenderContext ctx)
    {
        var brush = BuildGradientBrush(gradEl, path, transform, ctx, forFill: false);
        if (brush is null)
            return;

        // PenOptions(Brush, width, pattern) is the only brush-accepting ctor;
        // pass an empty pattern array for a solid gradient stroke.
        var opts = new PenOptions(brush, style.StrokeWidth, [])
        {
            StrokeOptions = new StrokeOptions
            {
                LineCap = style.StrokeLineCap,
                LineJoin = style.StrokeLineJoin,
                MiterLimit = style.StrokeMiterLimit,
            },
        };
        var pen = new SolidPen(opts);
        canvas.Save(new DrawingOptions { Transform = To4x4(transform) });
        canvas.Draw(pen, path);
        canvas.Restore();
    }

    /// <summary>
    /// Build an ImageSharp gradient brush from a <c>&lt;linearGradient&gt;</c>
    /// or <c>&lt;radialGradient&gt;</c> element.
    /// </summary>
    private static Brush? BuildGradientBrush(
        XElement gradEl, IPath path, Matrix3x2 transform, SvgRenderContext ctx, bool forFill)
    {
        // Resolve stop inheritance: xlink:href/href can point to another gradient
        // whose stops are inherited when this element defines none of its own.
        var stopsEl = ResolveGradientRef(gradEl, ctx);
        var stops = CollectGradientStops(stopsEl);
        if (stops.Length < 1)
            return null;
        if (stops.Length == 1)
        {
            // Degenerate gradient: treat as solid.
            stops = [stops[0], stops[0]];
        }

        var repetition = ParseSpreadMethod(Attr(gradEl, "spreadMethod"));

        // gradientUnits: objectBoundingBox (default) measures coordinates
        // as fractions of the element's bounding box; userSpaceOnUse
        // measures in the current user coordinate system.
        var units = Attr(gradEl, "gradientUnits") ?? "objectBoundingBox";
        bool obb = units.Equals("objectBoundingBox", StringComparison.OrdinalIgnoreCase);

        // gradientTransform: an additional user-space transform for the gradient
        // coordinate system. We compose it onto the element transform.
        var gradTransform = SvgTransform.Parse(Attr(gradEl, "gradientTransform"));

        // Compute the element bounding box in user space (before element transform).
        RectangleF bbox = GetPathBounds(path);

        string localName = gradEl.Name.LocalName;

        if (localName.Equals("linearGradient", StringComparison.OrdinalIgnoreCase))
        {
            // SVG 1.1 §13.4.2 defaults: x1=0% y1=0% x2=100% y2=0%
            float x1 = ParseGradCoord(Attr(gradEl, "x1"), obb, bbox.X, bbox.Width) ?? (obb ? bbox.X : 0);
            float y1 = ParseGradCoord(Attr(gradEl, "y1"), obb, bbox.Y, bbox.Height) ?? (obb ? bbox.Y : 0);
            float x2 = ParseGradCoord(Attr(gradEl, "x2"), obb, bbox.X, bbox.Width) ?? (obb ? bbox.X + bbox.Width : bbox.Width);
            float y2 = ParseGradCoord(Attr(gradEl, "y2"), obb, bbox.Y, bbox.Height) ?? (obb ? bbox.Y : 0);

            if (gradTransform != Matrix3x2.Identity)
            {
                var p1 = Vector2.Transform(new Vector2(x1, y1), gradTransform);
                var p2 = Vector2.Transform(new Vector2(x2, y2), gradTransform);
                x1 = p1.X; y1 = p1.Y; x2 = p2.X; y2 = p2.Y;
            }

            if (MathF.Abs(x2 - x1) < 0.001f && MathF.Abs(y2 - y1) < 0.001f)
            {
                // Degenerate (zero-length) gradient: use last stop color.
                return Brushes.Solid(stops[^1].Color);
            }

            return new LinearGradientBrush(new PointF(x1, y1), new PointF(x2, y2), repetition, stops);
        }

        if (localName.Equals("radialGradient", StringComparison.OrdinalIgnoreCase))
        {
            // SVG 1.1 §13.4.3 defaults: cx=50% cy=50% r=50% fx=cx fy=cy
            float cx = ParseGradCoord(Attr(gradEl, "cx"), obb, bbox.X, bbox.Width) ?? (obb ? bbox.X + bbox.Width * 0.5f : bbox.X + bbox.Width * 0.5f);
            float cy = ParseGradCoord(Attr(gradEl, "cy"), obb, bbox.Y, bbox.Height) ?? (obb ? bbox.Y + bbox.Height * 0.5f : bbox.Y + bbox.Height * 0.5f);
            float r = ParseGradCoordAbs(Attr(gradEl, "r"), obb, MathF.Sqrt(bbox.Width * bbox.Width + bbox.Height * bbox.Height) * 0.5f)
                      ?? (obb ? MathF.Min(bbox.Width, bbox.Height) * 0.5f : MathF.Min(bbox.Width, bbox.Height) * 0.5f);

            if (gradTransform != Matrix3x2.Identity)
            {
                var pc = Vector2.Transform(new Vector2(cx, cy), gradTransform);
                cx = pc.X; cy = pc.Y;
                // Scale r by the gradient transform's scale factor.
                float rs = MathF.Sqrt(gradTransform.M11 * gradTransform.M11 + gradTransform.M12 * gradTransform.M12);
                if (rs > 0) r *= rs;
            }

            if (r <= 0)
                return Brushes.Solid(stops[^1].Color);

            return new RadialGradientBrush(new PointF(cx, cy), r, repetition, stops);
        }

        return null;
    }

    /// <summary>
    /// Resolve a gradient's <c>xlink:href</c>/<c>href</c> stop-inheritance
    /// chain: if the given element has no <c>&lt;stop&gt;</c> children, follow
    /// the reference to find stops. Cycles are broken after 10 hops.
    /// </summary>
    private static XElement ResolveGradientRef(XElement gradEl, SvgRenderContext ctx)
    {
        int guard = 0;
        var current = gradEl;
        while (guard++ < 10)
        {
            bool hasStops = current.Elements()
                .Any(e => e.Name.LocalName.Equals("stop", StringComparison.OrdinalIgnoreCase));
            if (hasStops)
                return current;

            var href = Attr(current, "href") ?? Attr(current, "xlink:href");
            if (href is null || !href.StartsWith('#'))
                break;
            var refId = href[1..].Trim();
            if (refId.Length == 0 || !ctx.PaintServers.TryGetValue(refId, out var refEl))
                break;
            current = refEl;
        }
        return current;
    }

    /// <summary>
    /// Collect <c>&lt;stop&gt;</c> children from a gradient element into
    /// ImageSharp <see cref="ColorStop"/> values. Handles stop-color and
    /// stop-opacity (as presentation attributes and via style="…").
    /// </summary>
    private static ColorStop[] CollectGradientStops(XElement gradEl)
    {
        var result = new List<ColorStop>();
        float? prevOffset = null;
        foreach (var stopEl in gradEl.Elements()
                    .Where(e => e.Name.LocalName.Equals("stop", StringComparison.OrdinalIgnoreCase)))
        {
            // Offset: a number [0,1] or percentage.
            float offset = 0f;
            var offsetStr = Attr(stopEl, "offset");
            if (offsetStr is not null)
            {
                var trimmed = offsetStr.Trim();
                if (trimmed.EndsWith('%')
                    && float.TryParse(trimmed[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    offset = Math.Clamp(pct / 100f, 0f, 1f);
                else if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
                    offset = Math.Clamp(raw, 0f, 1f);
            }
            // Offsets must be non-decreasing (SVG 1.1 §13.2.4).
            if (prevOffset.HasValue && offset < prevOffset.Value)
                offset = prevOffset.Value;
            prevOffset = offset;

            // stop-color / stop-opacity live in style="" or as presentation attrs.
            // Build a minimal style and apply both.
            Color stopColor = Color.Black;
            float stopOpacity = 1f;

            // Parse stop-color from style or attribute.
            string? stopColorStr = null;
            string? stopOpacityStr = null;
            var styleStr = Attr(stopEl, "style");
            if (styleStr is not null)
            {
                foreach (var decl in styleStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int colon = decl.IndexOf(':');
                    if (colon <= 0) continue;
                    var prop = decl[..colon].Trim();
                    var val = decl[(colon + 1)..].Trim();
                    if (prop.Equals("stop-color", StringComparison.OrdinalIgnoreCase))
                        stopColorStr = val;
                    else if (prop.Equals("stop-opacity", StringComparison.OrdinalIgnoreCase))
                        stopOpacityStr = val;
                }
            }
            stopColorStr ??= Attr(stopEl, "stop-color");
            stopOpacityStr ??= Attr(stopEl, "stop-opacity");

            if (stopColorStr is not null && SvgColor.TryParse(stopColorStr, Color.Black, out var sc, out var scNone))
                stopColor = scNone ? Color.Transparent : sc;
            if (stopOpacityStr is not null
                && float.TryParse(stopOpacityStr.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var so))
                stopOpacity = Math.Clamp(so, 0f, 1f);

            // Fold stop-opacity into the alpha channel.
            var px = stopColor.ToPixel<Rgba32>();
            px.A = (byte)Math.Clamp((int)Math.Round(px.A / 255f * stopOpacity * 255f), 0, 255);
            result.Add(new ColorStop(offset, Color.FromPixel(px)));
        }
        return result.ToArray();
    }

    /// <summary>
    /// Parse a gradient coordinate value. In <c>objectBoundingBox</c> mode,
    /// pure numbers and percentages are fractions mapped onto
    /// <c>origin + fraction * size</c>. In <c>userSpaceOnUse</c> mode the
    /// value is a plain user-unit length.
    /// </summary>
    private static float? ParseGradCoord(string? v, bool obb, float origin, float size)
    {
        if (string.IsNullOrWhiteSpace(v))
            return null;
        var t = v.Trim();
        if (t.EndsWith('%')
            && float.TryParse(t[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
        {
            float fraction = pct / 100f;
            return obb ? origin + fraction * size : fraction * size;
        }
        if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
        {
            return obb ? origin + raw * size : raw;
        }
        return null;
    }

    /// <summary>
    /// Like <see cref="ParseGradCoord"/> but for radii: in objectBoundingBox mode
    /// a pure number/percentage is a fraction of the <c>refLength</c> (not offset
    /// by an origin).
    /// </summary>
    private static float? ParseGradCoordAbs(string? v, bool obb, float refLength)
    {
        if (string.IsNullOrWhiteSpace(v))
            return null;
        var t = v.Trim();
        if (t.EndsWith('%')
            && float.TryParse(t[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            return obb ? pct / 100f * refLength : pct / 100f * refLength;
        if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            return obb ? raw * refLength : raw;
        return null;
    }

    private static GradientRepetitionMode ParseSpreadMethod(string? v)
    {
        if (v is null) return GradientRepetitionMode.None;
        return v.Trim().ToLowerInvariant() switch
        {
            "repeat" => GradientRepetitionMode.Repeat,
            "reflect" => GradientRepetitionMode.Reflect,
            _ => GradientRepetitionMode.None,    // "pad" (default) → None
        };
    }

    /// <summary>
    /// Compute the bounding box of a path in its own user coordinate system
    /// (before any element transform). Uses <see cref="IPath.Bounds"/> which
    /// returns the axis-aligned bounding rectangle.
    /// </summary>
    private static RectangleF GetPathBounds(IPath path)
    {
        var b = path.Bounds;
        // Ensure non-zero extents so gradient coordinates don't divide by zero.
        float w = b.Width <= 0 ? 1f : b.Width;
        float h = b.Height <= 0 ? 1f : b.Height;
        return new RectangleF(b.Left, b.Top, w, h);
    }

    // --- shape builders ------------------------------------------------------

    private static IPath? BuildRect(XElement el, Vector2 viewport)
    {
        // x/width resolve percentages against the viewport width, y/height
        // against its height (e.g. a full-bleed `<rect width='100%' height='100%'>`).
        float x = ParseLengthPct(Attr(el, "x"), viewport.X) ?? 0;
        float y = ParseLengthPct(Attr(el, "y"), viewport.Y) ?? 0;
        float w = ParseLengthPct(Attr(el, "width"), viewport.X) ?? 0;
        float h = ParseLengthPct(Attr(el, "height"), viewport.Y) ?? 0;
        if (w <= 0 || h <= 0)
            return null;

        float? rxRaw = ParseLength(Attr(el, "rx"));
        float? ryRaw = ParseLength(Attr(el, "ry"));
        float rx = MathF.Min(rxRaw ?? ryRaw ?? 0, w / 2f);
        float ry = MathF.Min(ryRaw ?? rxRaw ?? 0, h / 2f);

        if (rx > 0 || ry > 0)
        {
            // Ensure both radii are non-zero (SVG 1.1 §9.2: if one is given the
            // other defaults to it, already handled by the ?? above).
            if (rx <= 0) rx = ry;
            if (ry <= 0) ry = rx;
            return RoundedRectDistinct(x, y, w, h, rx, ry);
        }

        return new RectanglePolygon(x, y, w, h);
    }

    /// <summary>
    /// Trace a rounded rect with distinct horizontal (<paramref name="rx"/>) and
    /// vertical (<paramref name="ry"/>) corner radii as cubic Bézier arcs.
    /// k = 0.5523 approximates a 90° arc (SVG 1.1 §9.2).
    /// </summary>
    private static IPath RoundedRectDistinct(float x, float y, float w, float h, float rx, float ry)
    {
        const float k = 0.5522847498f;
        var pb = new PathBuilder();
        pb.MoveTo(new PointF(x + rx, y));
        pb.LineTo(new PointF(x + w - rx, y));
        pb.AddCubicBezier(new PointF(x + w - rx, y), new PointF(x + w - rx + rx * k, y), new PointF(x + w, y + ry - ry * k), new PointF(x + w, y + ry));
        pb.LineTo(new PointF(x + w, y + h - ry));
        pb.AddCubicBezier(new PointF(x + w, y + h - ry), new PointF(x + w, y + h - ry + ry * k), new PointF(x + w - rx + rx * k, y + h), new PointF(x + w - rx, y + h));
        pb.LineTo(new PointF(x + rx, y + h));
        pb.AddCubicBezier(new PointF(x + rx, y + h), new PointF(x + rx - rx * k, y + h), new PointF(x, y + h - ry + ry * k), new PointF(x, y + h - ry));
        pb.LineTo(new PointF(x, y + ry));
        pb.AddCubicBezier(new PointF(x, y + ry), new PointF(x, y + ry - ry * k), new PointF(x + rx - rx * k, y), new PointF(x + rx, y));
        pb.CloseFigure();
        return pb.Build();
    }

    private static EllipsePolygon? BuildCircle(XElement el, Vector2 viewport)
    {
        float cx = ParseLengthPct(Attr(el, "cx"), viewport.X) ?? 0;
        float cy = ParseLengthPct(Attr(el, "cy"), viewport.Y) ?? 0;
        // SVG 1.1 §10.4: r percentage resolved against sqrt((vw²+vh²)/2)
        float vpDiag = MathF.Sqrt((viewport.X * viewport.X + viewport.Y * viewport.Y) / 2f);
        float r = ParseLengthPct(Attr(el, "r"), vpDiag) ?? 0;
        return r <= 0 ? null : new EllipsePolygon(cx, cy, r, r);
    }

    private static EllipsePolygon? BuildEllipse(XElement el, Vector2 viewport)
    {
        float cx = ParseLengthPct(Attr(el, "cx"), viewport.X) ?? 0;
        float cy = ParseLengthPct(Attr(el, "cy"), viewport.Y) ?? 0;
        float rx = ParseLengthPct(Attr(el, "rx"), viewport.X) ?? 0;
        float ry = ParseLengthPct(Attr(el, "ry"), viewport.Y) ?? 0;
        return rx <= 0 || ry <= 0 ? null : new EllipsePolygon(cx, cy, rx, ry);
    }

    private static IPath BuildLine(XElement el, Vector2 viewport)
    {
        float x1 = ParseLengthPct(Attr(el, "x1"), viewport.X) ?? 0;
        float y1 = ParseLengthPct(Attr(el, "y1"), viewport.Y) ?? 0;
        float x2 = ParseLengthPct(Attr(el, "x2"), viewport.X) ?? 0;
        float y2 = ParseLengthPct(Attr(el, "y2"), viewport.Y) ?? 0;
        var pb = new PathBuilder();
        pb.AddLine(new PointF(x1, y1), new PointF(x2, y2));
        return pb.Build();
    }

    private static IPath? BuildPoly(XElement el, bool close)
    {
        var pts = ParsePoints(Attr(el, "points"));
        if (pts.Count < 2)
            return null;
        var pb = new PathBuilder();
        pb.AddLines(pts);
        if (close)
            pb.CloseFigure();
        return pb.Build();
    }

    private static List<PointF> ParsePoints(string? s)
    {
        var list = new List<PointF>();
        if (string.IsNullOrWhiteSpace(s))
            return list;
        var nums = s.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < nums.Length; i += 2)
        {
            if (float.TryParse(nums[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(nums[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                list.Add(new PointF(x, y));
        }
        return list;
    }

    // --- style attribute plumbing -------------------------------------------

    private static void ApplyPresentationAttributes(SvgStyle style, XElement el)
    {
        // color must be resolved before fill/stroke so currentColor sees it.
        ApplyAttr(style, el, "color");
        ApplyAttr(style, el, "fill");
        ApplyAttr(style, el, "stroke");
        ApplyAttr(style, el, "stroke-width");
        ApplyAttr(style, el, "fill-rule");
        ApplyAttr(style, el, "opacity");
        ApplyAttr(style, el, "fill-opacity");
        ApplyAttr(style, el, "stroke-opacity");
        ApplyAttr(style, el, "stroke-dasharray");
        ApplyAttr(style, el, "stroke-dashoffset");
        ApplyAttr(style, el, "stroke-linecap");
        ApplyAttr(style, el, "stroke-linejoin");
        ApplyAttr(style, el, "stroke-miterlimit");
    }

    private static void ApplyAttr(SvgStyle style, XElement el, string name)
    {
        var v = Attr(el, name);
        if (v is not null)
            style.ApplyDeclaration(name, v);
    }

    private static void CollectStyles(XElement el, SvgStyleSheet sheet)
    {
        foreach (var styleEl in el.Descendants().Where(e => e.Name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase)))
            sheet.AddCss(styleEl.Value);
    }

    // --- attribute / unit helpers -------------------------------------------

    /// <summary>Read an attribute by local name, namespace-agnostic.</summary>
    private static string? Attr(XElement el, string localName)
    {
        foreach (var a in el.Attributes())
            if (a.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                return a.Value;
        return null;
    }

    private static float? ParseLength(string? v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return null;
        v = v.Trim();
        if (v.EndsWith('%'))
            return null; // percentage lengths unsupported (first cut)
        // Strip a trailing unit suffix (px/pt/etc.); user units == px here.
        int end = v.Length;
        while (end > 0 && char.IsLetter(v[end - 1]))
            end--;
        return float.TryParse(v.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
    }

    /// <summary>
    /// Like <see cref="ParseLength"/> but resolves a percentage against
    /// <paramref name="basis"/> (e.g. a viewport extent), used for shape
    /// geometry such as <c>&lt;rect width='100%'&gt;</c>.
    /// </summary>
    private static float? ParseLengthPct(string? v, float basis)
    {
        if (string.IsNullOrWhiteSpace(v))
            return null;
        var t = v.Trim();
        if (t.EndsWith('%')
            && float.TryParse(t[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            return pct / 100f * basis;
        return ParseLength(v);
    }

    /// <summary>
    /// Map every paint server (<c>&lt;pattern&gt;</c>, <c>&lt;linearGradient&gt;</c>,
    /// <c>&lt;radialGradient&gt;</c>) and every element with an <c>id</c> so
    /// <c>fill="url(#id)"</c> references and <c>&lt;use href="#id"&gt;</c>
    /// can find them. Scans the whole document, not just <c>&lt;defs&gt;</c>,
    /// since the id is what binds.
    /// </summary>
    private static (Dictionary<string, XElement> paintServers, Dictionary<string, XElement> elementsById)
        CollectPaintServers(XElement root)
    {
        var servers = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var byId = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var el in root.DescendantsAndSelf())
        {
            var id = Attr(el, "id");
            if (!string.IsNullOrEmpty(id))
                byId.TryAdd(id, el);

            var localName = el.Name.LocalName;
            if (localName.Equals("pattern", StringComparison.OrdinalIgnoreCase)
             || localName.Equals("linearGradient", StringComparison.OrdinalIgnoreCase)
             || localName.Equals("radialGradient", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(id))
                    servers.TryAdd(id, el);
            }
        }
        return (servers, byId);
    }

    private static ViewBox? ParseViewBox(string? v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return null;
        var parts = v.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return null;
        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minX) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minY) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            return new ViewBox(minX, minY, w, h);
        return null;
    }

    /// <summary>
    /// Map a 2D affine <see cref="Matrix3x2"/> into the <see cref="Matrix4x4"/>
    /// the ImageSharp.Drawing canvas consumes (row 0/1 carry the linear part,
    /// row 3 the translation).
    /// </summary>
    private static Matrix4x4 To4x4(Matrix3x2 m) => new(
        m.M11, m.M12, 0f, 0f,
        m.M21, m.M22, 0f, 0f,
        0f, 0f, 1f, 0f,
        m.M31, m.M32, 0f, 1f);

    private static Color? ApplyAlpha(Color c, float alphaScale)
    {
        var px = c.ToPixel<Rgba32>();
        float a = px.A / 255f * Math.Clamp(alphaScale, 0f, 1f);
        if (a <= 0f) return null;
        px.A = (byte)Math.Clamp((int)Math.Round(a * 255f), 0, 255);
        return Color.FromPixel(px);
    }

    private static string DecodeText(ReadOnlySpan<byte> utf8)
    {
        // Strip a UTF-8 BOM if present, then decode. SVG is XML; the bytes we get
        // are virtually always UTF-8 in practice.
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            utf8 = utf8[3..];
        return System.Text.Encoding.UTF8.GetString(utf8);
    }

    private readonly record struct ViewBox(float MinX, float MinY, float Width, float Height);

    /// <summary>
    /// Per-render context threaded down the element tree: the harvested
    /// <c>&lt;style&gt;</c> sheet, the paint-server registry (id → element),
    /// the id → element table for <c>&lt;use&gt;</c> resolution, and the
    /// user-space viewport size used to resolve percentage geometry.
    /// </summary>
    private sealed record SvgRenderContext(
        SvgStyleSheet Sheet,
        IReadOnlyDictionary<string, XElement> PaintServers,
        IReadOnlyDictionary<string, XElement> ElementsById,
        Vector2 Viewport)
    {
        /// <summary>Offscreen images (pattern tiles, group opacity layers) that must
        /// outlive the canvas timeline (the brush samples them lazily when the
        /// Paint scope unwinds), disposed after rendering completes.</summary>
        public List<IDisposable> Pending { get; } = [];
    }
}
