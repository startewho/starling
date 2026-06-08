// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

/// <summary>
/// CSS Masking 1 §7 / CSS Shapes 1 §4 — clip-path property parsing.
/// Covers basic-shape functions, geometry-box keywords, url() references,
/// and shape + geometry-box combinations.
/// </summary>
[TestClass]
[Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/")]
[Spec("css-shapes-1", "https://www.w3.org/TR/css-shapes-1/")]
public sealed class ClipPathParserTests
{
    private static PropertyDeclaration ParseClipPath(string value)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ clip-path: {value} }}");
        var rule = (StyleRule)sheet.Rules[0];
        var decls = rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
        return decls.Single(d => d.Id == PropertyId.ClipPath);
    }

    private static CssClipPath ParseClipPathValue(string value)
        => (CssClipPath)ParseClipPath(value).Value;

    // -----------------------------------------------------------------------
    // none
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ClipPath_none_is_none()
    {
        var v = ParseClipPathValue("none");
        v.IsNone.Should().BeTrue();
        v.Shape.Should().BeNull();
        v.IsUrl.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // geometry-box keywords alone
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ClipPath_borderBox_keyword()
    {
        var v = ParseClipPathValue("border-box");
        v.IsNone.Should().BeFalse();
        v.Shape.Should().BeNull();
        v.GeometryBox.Should().Be(CssGeometryBox.BorderBox);
    }

    [TestMethod]
    public void ClipPath_paddingBox_keyword()
    {
        var v = ParseClipPathValue("padding-box");
        v.GeometryBox.Should().Be(CssGeometryBox.PaddingBox);
    }

    [TestMethod]
    public void ClipPath_contentBox_keyword()
    {
        var v = ParseClipPathValue("content-box");
        v.GeometryBox.Should().Be(CssGeometryBox.ContentBox);
    }

    [TestMethod]
    public void ClipPath_marginBox_keyword()
    {
        var v = ParseClipPathValue("margin-box");
        v.GeometryBox.Should().Be(CssGeometryBox.MarginBox);
    }

    [TestMethod]
    public void ClipPath_fillBox_keyword()
    {
        var v = ParseClipPathValue("fill-box");
        v.GeometryBox.Should().Be(CssGeometryBox.FillBox);
    }

    [TestMethod]
    public void ClipPath_strokeBox_keyword()
    {
        var v = ParseClipPathValue("stroke-box");
        v.GeometryBox.Should().Be(CssGeometryBox.StrokeBox);
    }

    [TestMethod]
    public void ClipPath_viewBox_keyword()
    {
        var v = ParseClipPathValue("view-box");
        v.GeometryBox.Should().Be(CssGeometryBox.ViewBox);
    }

    // -----------------------------------------------------------------------
    // url(#ref)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ClipPath_url_with_fragment()
    {
        var v = ParseClipPathValue("url(#myClip)");
        v.IsUrl.Should().BeTrue();
        v.UrlFragmentId.Should().Be("myClip");
    }

    [TestMethod]
    public void ClipPath_url_quoted_string_strips_hash()
    {
        var v = ParseClipPathValue("url(\"#clip1\")");
        v.IsUrl.Should().BeTrue();
        v.UrlFragmentId.Should().Be("clip1");
    }

    // -----------------------------------------------------------------------
    // circle()
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ClipPath_circle_no_args_has_default_center_position()
    {
        var v = ParseClipPathValue("circle()");
        var circle = v.Shape.Should().BeOfType<CssCircleShape>().Subject;
        circle.Radius.Should().BeNull();
        circle.RadiusKeyword.Should().BeNull();
        circle.Position.X.IsPercentage.Should().BeTrue();
        circle.Position.X.Percentage.Should().Be(50);
        circle.Position.Y.Percentage.Should().Be(50);
    }

    [TestMethod]
    public void ClipPath_circle_with_px_radius()
    {
        var v = ParseClipPathValue("circle(50px)");
        var circle = v.Shape.Should().BeOfType<CssCircleShape>().Subject;
        circle.Radius.Should().NotBeNull();
        circle.Radius!.Length.Should().Be(new CssLength(50, CssLengthUnit.Px));
    }

    [TestMethod]
    public void ClipPath_circle_with_percentage_radius()
    {
        var v = ParseClipPathValue("circle(40%)");
        var circle = v.Shape.Should().BeOfType<CssCircleShape>().Subject;
        circle.Radius!.IsPercentage.Should().BeTrue();
        circle.Radius.Percentage.Should().Be(40);
    }

    [TestMethod]
    public void ClipPath_circle_closest_side_keyword()
    {
        var v = ParseClipPathValue("circle(closest-side)");
        var circle = v.Shape.Should().BeOfType<CssCircleShape>().Subject;
        circle.Radius.Should().BeNull();
        circle.RadiusKeyword.Should().Be("closest-side");
    }

    [TestMethod]
    public void ClipPath_circle_farthest_side_keyword()
    {
        var v = ParseClipPathValue("circle(farthest-side)");
        var circle = v.Shape.Should().BeOfType<CssCircleShape>().Subject;
        circle.RadiusKeyword.Should().Be("farthest-side");
    }

    [TestMethod]
    public void ClipPath_circle_with_at_position()
    {
        var v = ParseClipPathValue("circle(50px at 25% 75%)");
        var circle = v.Shape.Should().BeOfType<CssCircleShape>().Subject;
        circle.Radius!.Length.Should().Be(new CssLength(50, CssLengthUnit.Px));
        circle.Position.X.Percentage.Should().Be(25);
        circle.Position.Y.Percentage.Should().Be(75);
    }

    [TestMethod]
    public void ClipPath_circle_at_center_keyword()
    {
        var v = ParseClipPathValue("circle(at center)");
        var circle = v.Shape.Should().BeOfType<CssCircleShape>().Subject;
        circle.Position.X.Percentage.Should().Be(50);
        circle.Position.Y.Percentage.Should().Be(50);
    }

    // -----------------------------------------------------------------------
    // ellipse()
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ClipPath_ellipse_no_args()
    {
        var v = ParseClipPathValue("ellipse()");
        var e = v.Shape.Should().BeOfType<CssEllipseShape>().Subject;
        e.RadiusX.Should().BeNull();
        e.RadiusY.Should().BeNull();
        e.Position.X.Percentage.Should().Be(50);
    }

    [TestMethod]
    public void ClipPath_ellipse_two_px_radii()
    {
        var v = ParseClipPathValue("ellipse(100px 50px)");
        var e = v.Shape.Should().BeOfType<CssEllipseShape>().Subject;
        e.RadiusX!.Length.Should().Be(new CssLength(100, CssLengthUnit.Px));
        e.RadiusY!.Length.Should().Be(new CssLength(50, CssLengthUnit.Px));
    }

    [TestMethod]
    public void ClipPath_ellipse_with_at_position()
    {
        var v = ParseClipPathValue("ellipse(100px 50px at center)");
        var e = v.Shape.Should().BeOfType<CssEllipseShape>().Subject;
        e.RadiusX!.Length.Should().Be(new CssLength(100, CssLengthUnit.Px));
        e.Position.X.Percentage.Should().Be(50);
    }

    // -----------------------------------------------------------------------
    // inset()
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ClipPath_inset_single_value_applies_to_all_sides()
    {
        var v = ParseClipPathValue("inset(10px)");
        var ins = v.Shape.Should().BeOfType<CssInsetShape>().Subject;
        ins.Top.Length.Should().Be(new CssLength(10, CssLengthUnit.Px));
        ins.Right.Length.Should().Be(new CssLength(10, CssLengthUnit.Px));
        ins.Bottom.Length.Should().Be(new CssLength(10, CssLengthUnit.Px));
        ins.Left.Length.Should().Be(new CssLength(10, CssLengthUnit.Px));
        ins.Radii.Should().BeNull();
    }

    [TestMethod]
    public void ClipPath_inset_two_values()
    {
        var v = ParseClipPathValue("inset(10px 20px)");
        var ins = v.Shape.Should().BeOfType<CssInsetShape>().Subject;
        ins.Top.Length.Should().Be(new CssLength(10, CssLengthUnit.Px));
        ins.Right.Length.Should().Be(new CssLength(20, CssLengthUnit.Px));
        ins.Bottom.Length.Should().Be(new CssLength(10, CssLengthUnit.Px));
        ins.Left.Length.Should().Be(new CssLength(20, CssLengthUnit.Px));
    }

    [TestMethod]
    public void ClipPath_inset_three_values()
    {
        var v = ParseClipPathValue("inset(10px 20px 30px)");
        var ins = v.Shape.Should().BeOfType<CssInsetShape>().Subject;
        ins.Top.Length.Should().Be(new CssLength(10, CssLengthUnit.Px));
        ins.Right.Length.Should().Be(new CssLength(20, CssLengthUnit.Px));
        ins.Bottom.Length.Should().Be(new CssLength(30, CssLengthUnit.Px));
        ins.Left.Length.Should().Be(new CssLength(20, CssLengthUnit.Px)); // mirrors right
    }

    [TestMethod]
    public void ClipPath_inset_four_values()
    {
        var v = ParseClipPathValue("inset(10px 20px 30px 40px)");
        var ins = v.Shape.Should().BeOfType<CssInsetShape>().Subject;
        ins.Top.Length.Should().Be(new CssLength(10, CssLengthUnit.Px));
        ins.Right.Length.Should().Be(new CssLength(20, CssLengthUnit.Px));
        ins.Bottom.Length.Should().Be(new CssLength(30, CssLengthUnit.Px));
        ins.Left.Length.Should().Be(new CssLength(40, CssLengthUnit.Px));
    }

    [TestMethod]
    public void ClipPath_inset_with_round_border_radius()
    {
        var v = ParseClipPathValue("inset(10px round 5px)");
        var ins = v.Shape.Should().BeOfType<CssInsetShape>().Subject;
        ins.Radii.Should().NotBeNull();
        ins.Radii!.Count.Should().Be(4);
        ins.Radii[0].H.Length.Should().Be(new CssLength(5, CssLengthUnit.Px));
    }

    [TestMethod]
    public void ClipPath_inset_percentage_offsets()
    {
        var v = ParseClipPathValue("inset(10% 20%)");
        var ins = v.Shape.Should().BeOfType<CssInsetShape>().Subject;
        ins.Top.IsPercentage.Should().BeTrue();
        ins.Top.Percentage.Should().Be(10);
        ins.Right.Percentage.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // polygon()
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ClipPath_polygon_basic_triangle()
    {
        var v = ParseClipPathValue("polygon(50% 0%, 0% 100%, 100% 100%)");
        var poly = v.Shape.Should().BeOfType<CssPolygonShape>().Subject;
        poly.FillRule.Should().Be(CssFillRule.Nonzero);
        poly.Vertices.Should().HaveCount(3);
        poly.Vertices[0].X.Percentage.Should().Be(50);
        poly.Vertices[0].Y.Percentage.Should().Be(0);
    }

    [TestMethod]
    public void ClipPath_polygon_evenodd_fill_rule()
    {
        var v = ParseClipPathValue("polygon(evenodd, 0 0, 100px 0, 100px 100px, 0 100px)");
        var poly = v.Shape.Should().BeOfType<CssPolygonShape>().Subject;
        poly.FillRule.Should().Be(CssFillRule.EvenOdd);
        poly.Vertices.Should().HaveCount(4);
    }

    [TestMethod]
    public void ClipPath_polygon_nonzero_explicit()
    {
        var v = ParseClipPathValue("polygon(nonzero, 0 0, 50px 50px, 100px 0)");
        var poly = v.Shape.Should().BeOfType<CssPolygonShape>().Subject;
        poly.FillRule.Should().Be(CssFillRule.Nonzero);
        poly.Vertices.Should().HaveCount(3);
    }

    [TestMethod]
    public void ClipPath_polygon_px_vertices()
    {
        var v = ParseClipPathValue("polygon(0px 0px, 100px 0px, 100px 100px)");
        var poly = v.Shape.Should().BeOfType<CssPolygonShape>().Subject;
        poly.Vertices.Should().HaveCount(3);
        poly.Vertices[1].X.Length.Should().Be(new CssLength(100, CssLengthUnit.Px));
        poly.Vertices[1].Y.Length.Should().Be(new CssLength(0, CssLengthUnit.Px));
    }

    // -----------------------------------------------------------------------
    // shape + geometry-box combinations
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ClipPath_circle_with_border_box_reference()
    {
        var v = ParseClipPathValue("circle(50%) border-box");
        v.Shape.Should().BeOfType<CssCircleShape>();
        v.GeometryBox.Should().Be(CssGeometryBox.BorderBox);
    }

    [TestMethod]
    public void ClipPath_geometry_box_before_shape_function()
    {
        var v = ParseClipPathValue("border-box circle(50%)");
        v.Shape.Should().BeOfType<CssCircleShape>();
        v.GeometryBox.Should().Be(CssGeometryBox.BorderBox);
    }

    [TestMethod]
    public void ClipPath_inset_with_padding_box_reference()
    {
        var v = ParseClipPathValue("inset(5px) padding-box");
        v.Shape.Should().BeOfType<CssInsetShape>();
        v.GeometryBox.Should().Be(CssGeometryBox.PaddingBox);
    }
}
