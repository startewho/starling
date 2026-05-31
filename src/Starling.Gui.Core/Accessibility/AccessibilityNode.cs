using LayoutRect = Starling.Layout.Rect;

namespace Starling.Gui.Core.Accessibility;

/// <summary>
/// The accessibility role of a node — the semantic kind a screen reader
/// announces. Mapped from the element's tag and its <c>role</c> attribute by
/// <see cref="AccessibilityTreeBuilder"/>. A small, platform-neutral set; the
/// per-platform bridge (macOS <c>NSAccessibility</c>, Windows UI Automation,
/// Linux AT-SPI) maps these to native roles.
/// </summary>
public enum AccessibilityRole
{
    Generic,
    Document,
    Group,
    Heading,
    Paragraph,
    Link,
    Button,
    TextField,
    CheckBox,
    RadioButton,
    Image,
    List,
    ListItem,
    Navigation,
    Banner,
    ContentInfo,
    Main,
    Article,
    Region,
    Complementary,
    Form,
    Search,
    ComboBox,
}

/// <summary>
/// One node of the accessibility tree: the semantic role, the computed
/// accessible name, an optional value (a text field's contents), the
/// document-coordinate bounds, focus state, and children. Built from the layout
/// box tree by <see cref="AccessibilityTreeBuilder"/> and consumed by the native
/// accessibility bridge. Engine-agnostic, so the Avalonia and native shells can
/// both expose it.
/// </summary>
public sealed class AccessibilityNode
{
    public AccessibilityRole Role { get; init; }

    /// <summary>The accessible name (aria-label, alt, associated label, or text).</summary>
    public string Name { get; init; } = "";

    /// <summary>The value a screen reader reads for a control (e.g. a field's text), else null.</summary>
    public string? Value { get; init; }

    /// <summary>Bounds in document (page) coordinates. The bridge maps to screen.</summary>
    public LayoutRect Bounds { get; init; }

    /// <summary>True when this node's element is the document's focused element.</summary>
    public bool Focused { get; init; }

    /// <summary>1–6 for a heading, else 0.</summary>
    public int HeadingLevel { get; init; }

    /// <summary>Checked state for a checkbox/radio, else false.</summary>
    public bool Checked { get; init; }

    public IReadOnlyList<AccessibilityNode> Children { get; init; } = [];
}
