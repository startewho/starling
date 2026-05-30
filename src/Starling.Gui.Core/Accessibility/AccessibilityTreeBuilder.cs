using Starling.Dom;
using Starling.Layout.Box;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Gui.Core.Accessibility;

/// <summary>
/// Builds an <see cref="AccessibilityNode"/> tree from a laid-out box tree. Walks
/// the boxes, accumulating document coordinates (a box's <see cref="Box.Frame"/>
/// is parent-relative), and emits a node for each element that carries a semantic
/// role. Non-semantic boxes are flattened — their semantic descendants attach to
/// the nearest semantic ancestor — so the tree a screen reader sees is the
/// meaningful structure, not every presentational `div`.
/// </summary>
/// <remarks>
/// The accessible name follows the usual precedence: <c>aria-label</c>, then
/// role-specific sources (a control's associated <c>&lt;label&gt;</c> or
/// placeholder, an image's <c>alt</c>/<c>title</c>), then the element's text.
/// This is engine-agnostic and shell-agnostic; the per-platform bridge turns it
/// into native accessibility objects.
/// </remarks>
public static class AccessibilityTreeBuilder
{
    /// <summary>Builds the tree rooted at a Document node.</summary>
    public static AccessibilityNode Build(BlockBox root, Document document)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(document);

        var children = new List<AccessibilityNode>();
        foreach (var child in root.Children)
            Walk(child, root.Frame.X, root.Frame.Y, document, children);

        return new AccessibilityNode
        {
            Role = AccessibilityRole.Document,
            Name = DocumentTitle(document),
            Bounds = new LayoutRect(root.Frame.X, root.Frame.Y, root.Frame.Width, root.Frame.Height),
            Children = children,
        };
    }

    private static void Walk(Box box, double originX, double originY, Document document, List<AccessibilityNode> into)
    {
        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        var element = box.Element;
        var role = element is null ? (AccessibilityRole?)null : MapRole(element);

        if (role is { } r)
        {
            var kids = new List<AccessibilityNode>();
            foreach (var child in box.Children)
                Walk(child, frameX, frameY, document, kids);

            into.Add(new AccessibilityNode
            {
                Role = r,
                Name = ComputeName(element!, document, r),
                Value = ComputeValue(element!, r),
                Bounds = new LayoutRect(frameX, frameY, box.Frame.Width, box.Frame.Height),
                Focused = ReferenceEquals(document.FocusedElement, element),
                HeadingLevel = r == AccessibilityRole.Heading ? HeadingLevel(element!) : 0,
                Checked = (r == AccessibilityRole.CheckBox || r == AccessibilityRole.RadioButton)
                          && HtmlFormControls.Checked(element!),
                Children = kids,
            });
        }
        else
        {
            // Non-semantic box: flatten — attach semantic descendants to this parent.
            foreach (var child in box.Children)
                Walk(child, frameX, frameY, document, into);
        }
    }

    /// <summary>Maps an element to a role, or null when it carries no semantics.</summary>
    private static AccessibilityRole? MapRole(Element element)
    {
        // An explicit ARIA role wins.
        var aria = element.GetAttribute("role");
        if (!string.IsNullOrEmpty(aria))
        {
            switch (aria.Trim().ToLowerInvariant())
            {
                case "heading": return AccessibilityRole.Heading;
                case "link": return AccessibilityRole.Link;
                case "button": return AccessibilityRole.Button;
                case "textbox": return AccessibilityRole.TextField;
                case "checkbox": return AccessibilityRole.CheckBox;
                case "radio": return AccessibilityRole.RadioButton;
                case "img": return AccessibilityRole.Image;
                case "list": return AccessibilityRole.List;
                case "listitem": return AccessibilityRole.ListItem;
                case "navigation": return AccessibilityRole.Navigation;
                case "banner": return AccessibilityRole.Banner;
                case "contentinfo": return AccessibilityRole.ContentInfo;
                case "main": return AccessibilityRole.Main;
                case "group": return AccessibilityRole.Group;
            }
        }

        switch (element.LocalName)
        {
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                return AccessibilityRole.Heading;
            case "a":
                return element.HasAttribute("href") ? AccessibilityRole.Link : null;
            case "button":
                return AccessibilityRole.Button;
            case "textarea":
                return AccessibilityRole.TextField;
            case "input":
                return InputRole(element);
            case "img": case "svg":
                return AccessibilityRole.Image;
            case "p":
                return AccessibilityRole.Paragraph;
            case "ul": case "ol":
                return AccessibilityRole.List;
            case "li":
                return AccessibilityRole.ListItem;
            case "nav":
                return AccessibilityRole.Navigation;
            case "header":
                return AccessibilityRole.Banner;
            case "footer":
                return AccessibilityRole.ContentInfo;
            case "main":
                return AccessibilityRole.Main;
            default:
                return null;
        }
    }

    private static AccessibilityRole? InputRole(Element element)
    {
        var type = HtmlFormControls.InputType(element);
        return type switch
        {
            "checkbox" => AccessibilityRole.CheckBox,
            "radio" => AccessibilityRole.RadioButton,
            "button" or "submit" or "reset" or "image" => AccessibilityRole.Button,
            "hidden" => null,
            _ => AccessibilityRole.TextField, // text, search, email, url, password, tel, number, …
        };
    }

    private static string ComputeName(Element element, Document document, AccessibilityRole role)
    {
        var ariaLabel = element.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel)) return Collapse(ariaLabel);

        switch (role)
        {
            case AccessibilityRole.Image:
                return Collapse(element.GetAttribute("alt")
                    ?? element.GetAttribute("title") ?? "");

            case AccessibilityRole.TextField:
            case AccessibilityRole.CheckBox:
            case AccessibilityRole.RadioButton:
                var labelled = AssociatedLabel(element, document);
                if (!string.IsNullOrWhiteSpace(labelled)) return Collapse(labelled);
                var placeholder = element.GetAttribute("placeholder");
                return Collapse(placeholder ?? "");

            case AccessibilityRole.Button:
                // A submit/button input's name is its value attribute; otherwise text.
                if (element.LocalName == "input")
                    return Collapse(element.GetAttribute("value") ?? "");
                return Collapse(element.TextContent);

            default:
                // Headings, links, list items, paragraphs: the element's text.
                return Collapse(element.TextContent);
        }
    }

    private static string? ComputeValue(Element element, AccessibilityRole role)
        => role == AccessibilityRole.TextField ? HtmlFormControls.Value(element) : null;

    /// <summary>Finds a <c>&lt;label for="id"&gt;</c> associated with the control.</summary>
    private static string AssociatedLabel(Element control, Document document)
    {
        var id = control.Id;
        if (string.IsNullOrEmpty(id)) return "";
        foreach (var el in document.DescendantElements())
        {
            if (el.LocalName == "label"
                && string.Equals(el.GetAttribute("for"), id, StringComparison.Ordinal))
                return el.TextContent;
        }
        return "";
    }

    private static int HeadingLevel(Element element)
    {
        var n = element.LocalName;
        if (n.Length == 2 && n[0] == 'h' && n[1] is >= '1' and <= '6')
            return n[1] - '0';
        // role="heading" — honour aria-level when present.
        var level = element.GetAttribute("aria-level");
        return int.TryParse(level, out var l) && l is >= 1 and <= 6 ? l : 2;
    }

    private static string DocumentTitle(Document document)
    {
        foreach (var el in document.DescendantElements())
            if (el.LocalName == "title")
                return Collapse(el.TextContent);
        return "";
    }

    /// <summary>Trims and collapses runs of whitespace, the way a name is announced.</summary>
    private static string Collapse(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        var inWs = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { inWs = true; continue; }
            if (inWs && sb.Length > 0) sb.Append(' ');
            inWs = false;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
