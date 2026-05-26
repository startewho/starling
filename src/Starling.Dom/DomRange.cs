namespace Starling.Dom;

/// <summary>
/// DOM §4.6 — Live Range. Represents a contiguous portion of the DOM tree
/// defined by two boundary points (start, end), each a (node, offset) pair.
/// Offsets into CharacterData nodes are code-unit offsets into the character
/// data; offsets into other nodes count child nodes.
/// </summary>
/// <remarks>
/// Named <c>DomRange</c> (not <c>Range</c>) to avoid a collision with
/// <see cref="System.Range"/> and the JS engine's RangeErrorPrototype slot.
/// The JS-visible name is "Range" — the binding layer installs it under that key.
/// </remarks>
public sealed class DomRange
{
    // §4.6: A range has a start and an end, both boundary points.
    // A boundary point is a (node, offset) tuple.

    public Node StartContainer { get; private set; }
    public int StartOffset { get; private set; }
    public Node EndContainer { get; private set; }
    public int EndOffset { get; private set; }

    /// <summary>§4.6 — collapsed: start and end are the same boundary point.</summary>
    public bool Collapsed => ReferenceEquals(StartContainer, EndContainer) && StartOffset == EndOffset;

    /// <summary>§4.6 — commonAncestorContainer: the deepest node that contains
    /// both boundary points.</summary>
    public Node CommonAncestorContainer
    {
        get
        {
            // Walk from start container; stop when we reach an inclusive ancestor of end.
            var container = StartContainer;
            while (!IsInclusiveAncestor(container, EndContainer))
                container = container.ParentNode!;
            return container;
        }
    }

    // -----------------------------------------------------------------------
    // Constructors

    /// <summary>Create a collapsed range at (document, 0) — the §4.6
    /// "new Range()" initializer. The document is the associated document
    /// for the "new Range()" constructor; it is the owner document of the
    /// current page.</summary>
    public DomRange(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StartContainer = document;
        StartOffset = 0;
        EndContainer = document;
        EndOffset = 0;
    }

    private DomRange(Node startContainer, int startOffset, Node endContainer, int endOffset)
    {
        StartContainer = startContainer;
        StartOffset = startOffset;
        EndContainer = endContainer;
        EndOffset = endOffset;
    }

    // -----------------------------------------------------------------------
    // Setters (§4.6.3)

    /// <summary>§4.6.3 set the start.</summary>
    public void SetStart(Node node, int offset)
    {
        ArgumentNullException.ThrowIfNull(node);
        ValidateNotDocumentType(node, "setStart");
        ValidateOffset(node, offset, "setStart");

        // If the new start is after the old end (or in a different tree),
        // collapse to the new start.
        if (!SameRoot(node, EndContainer) || ComparePoints(node, offset, EndContainer, EndOffset) > 0)
        {
            StartContainer = node;
            StartOffset = offset;
            EndContainer = node;
            EndOffset = offset;
        }
        else
        {
            StartContainer = node;
            StartOffset = offset;
        }
    }

    /// <summary>§4.6.3 set the end.</summary>
    public void SetEnd(Node node, int offset)
    {
        ArgumentNullException.ThrowIfNull(node);
        ValidateNotDocumentType(node, "setEnd");
        ValidateOffset(node, offset, "setEnd");

        // If the new end is before the old start (or in a different tree),
        // collapse to the new end.
        if (!SameRoot(node, StartContainer) || ComparePoints(node, offset, StartContainer, StartOffset) < 0)
        {
            StartContainer = node;
            StartOffset = offset;
            EndContainer = node;
            EndOffset = offset;
        }
        else
        {
            EndContainer = node;
            EndOffset = offset;
        }
    }

    /// <summary>§4.6.3 setStartBefore.</summary>
    public void SetStartBefore(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var parent = node.ParentNode ?? throw DomRangeException.Create("InvalidNodeTypeError",
            "setStartBefore: node has no parent");
        SetStart(parent, IndexOf(node));
    }

    /// <summary>§4.6.3 setStartAfter.</summary>
    public void SetStartAfter(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var parent = node.ParentNode ?? throw DomRangeException.Create("InvalidNodeTypeError",
            "setStartAfter: node has no parent");
        SetStart(parent, IndexOf(node) + 1);
    }

    /// <summary>§4.6.3 setEndBefore.</summary>
    public void SetEndBefore(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var parent = node.ParentNode ?? throw DomRangeException.Create("InvalidNodeTypeError",
            "setEndBefore: node has no parent");
        SetEnd(parent, IndexOf(node));
    }

    /// <summary>§4.6.3 setEndAfter.</summary>
    public void SetEndAfter(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var parent = node.ParentNode ?? throw DomRangeException.Create("InvalidNodeTypeError",
            "setEndAfter: node has no parent");
        SetEnd(parent, IndexOf(node) + 1);
    }

    /// <summary>§4.6.3 collapse(toStart?). Default is toStart=false (collapse to end).</summary>
    public void Collapse(bool toStart = false)
    {
        if (toStart)
        {
            EndContainer = StartContainer;
            EndOffset = StartOffset;
        }
        else
        {
            StartContainer = EndContainer;
            StartOffset = EndOffset;
        }
    }

    /// <summary>§4.6.3 selectNode.</summary>
    public void SelectNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var parent = node.ParentNode ?? throw DomRangeException.Create("InvalidNodeTypeError",
            "selectNode: node has no parent");
        var index = IndexOf(node);
        StartContainer = parent;
        StartOffset = index;
        EndContainer = parent;
        EndOffset = index + 1;
    }

    /// <summary>§4.6.3 selectNodeContents.</summary>
    public void SelectNodeContents(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is DocumentType)
            throw DomRangeException.Create("InvalidNodeTypeError", "selectNodeContents: cannot select a doctype node");
        StartContainer = node;
        StartOffset = 0;
        EndContainer = node;
        EndOffset = NodeLength(node);
    }

    // -----------------------------------------------------------------------
    // Comparison (§4.6.4)

    public const int START_TO_START = 0;
    public const int START_TO_END = 1;
    public const int END_TO_END = 2;
    public const int END_TO_START = 3;

    /// <summary>§4.6.4 compareBoundaryPoints.</summary>
    public int CompareBoundaryPoints(int how, DomRange other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (how is < 0 or > 3)
            throw DomRangeException.Create("NotSupportedError", $"compareBoundaryPoints: invalid 'how' argument {how}");

        // If the two ranges are not in the same tree, throw WrongDocumentError.
        if (!SameRoot(StartContainer, other.StartContainer))
            throw DomRangeException.Create("WrongDocumentError",
                "compareBoundaryPoints: ranges are not in the same tree");

        Node thisPoint, otherPoint;
        int thisOffset, otherOffset;

        switch (how)
        {
            case START_TO_START:
                thisPoint = StartContainer; thisOffset = StartOffset;
                otherPoint = other.StartContainer; otherOffset = other.StartOffset;
                break;
            case START_TO_END:
                thisPoint = EndContainer; thisOffset = EndOffset;
                otherPoint = other.StartContainer; otherOffset = other.StartOffset;
                break;
            case END_TO_END:
                thisPoint = EndContainer; thisOffset = EndOffset;
                otherPoint = other.EndContainer; otherOffset = other.EndOffset;
                break;
            case END_TO_START:
                thisPoint = StartContainer; thisOffset = StartOffset;
                otherPoint = other.EndContainer; otherOffset = other.EndOffset;
                break;
            default:
                throw new InvalidOperationException("unreachable");
        }

        return ComparePoints(thisPoint, thisOffset, otherPoint, otherOffset);
    }

    /// <summary>§4.6.4 comparePoint(node, offset).</summary>
    public int ComparePoint(Node node, int offset)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!SameRoot(node, StartContainer))
            throw DomRangeException.Create("WrongDocumentError",
                "comparePoint: node and range are not in the same tree");
        if (node is DocumentType)
            throw DomRangeException.Create("InvalidNodeTypeError", "comparePoint: doctype node is not allowed");
        ValidateOffset(node, offset, "comparePoint");

        if (ComparePoints(node, offset, StartContainer, StartOffset) < 0) return -1;
        if (ComparePoints(node, offset, EndContainer, EndOffset) > 0) return 1;
        return 0;
    }

    /// <summary>§4.6.4 isPointInRange.</summary>
    public bool IsPointInRange(Node node, int offset)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!SameRoot(node, StartContainer)) return false;
        if (node is DocumentType)
            throw DomRangeException.Create("InvalidNodeTypeError", "isPointInRange: doctype node is not allowed");
        ValidateOffset(node, offset, "isPointInRange");

        return ComparePoints(node, offset, StartContainer, StartOffset) >= 0
            && ComparePoints(node, offset, EndContainer, EndOffset) <= 0;
    }

    /// <summary>§4.6.4 intersectsNode.</summary>
    public bool IntersectsNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!SameRoot(node, StartContainer)) return false;

        var parent = node.ParentNode;
        if (parent is null) return true; // orphan root intersects any range in the same tree

        var offset = IndexOf(node);
        // The node overlaps the range if [offset, offset+1) intersects [start, end).
        return ComparePoints(parent, offset + 1, StartContainer, StartOffset) > 0
            && ComparePoints(parent, offset, EndContainer, EndOffset) < 0;
    }

    // -----------------------------------------------------------------------
    // Cloning (§4.6.5)

    /// <summary>§4.6.5 cloneRange — returns an independent copy.</summary>
    public DomRange CloneRange() =>
        new(StartContainer, StartOffset, EndContainer, EndOffset);

    /// <summary>§4.6.5 detach — no-op per current spec (ranges no longer get detached).</summary>
    public void Detach() { /* spec says no-op */ }

    // -----------------------------------------------------------------------
    // String representation (§4.6.6)

    /// <summary>§4.6.6 stringify: concatenates all text covered by the range.
    /// Walks all text nodes (CharacterData) in document order between start
    /// and end, slicing the boundary containers as needed.</summary>
    public string Stringify()
    {
        var sb = new System.Text.StringBuilder();

        if (ReferenceEquals(StartContainer, EndContainer))
        {
            // Same container — slice character data, or collect child text.
            if (StartContainer is CharacterData cd)
            {
                sb.Append(cd.Data, StartOffset, EndOffset - StartOffset);
            }
            else
            {
                // Element/Document: concatenate text from children [start, end).
                var i = 0;
                for (var c = StartContainer.FirstChild; c is not null; c = c.NextSibling)
                {
                    if (i >= StartOffset && i < EndOffset)
                        CollectText(c, sb);
                    i++;
                }
            }
            return sb.ToString();
        }

        // Walk all text descendants of the common ancestor in pre-order.
        // For each Text/CharacterData node, determine which slice to include.
        foreach (var textNode in TextNodesInPreOrder(CommonAncestorContainer))
        {
            if (ReferenceEquals(textNode, StartContainer))
            {
                sb.Append(textNode.Data, StartOffset, textNode.Data.Length - StartOffset);
            }
            else if (ReferenceEquals(textNode, EndContainer))
            {
                sb.Append(textNode.Data, 0, EndOffset);
                break; // End container is the last one.
            }
            else
            {
                // Fully contained? Check it's after start and before end.
                if (IsAfterOrAt(textNode) && IsBeforeOrAt(textNode))
                    sb.Append(textNode.Data);
            }
        }

        return sb.ToString();
    }

    private static void CollectText(Node node, System.Text.StringBuilder sb)
    {
        if (node is CharacterData cd) { sb.Append(cd.Data); return; }
        for (var c = node.FirstChild; c is not null; c = c.NextSibling)
            CollectText(c, sb);
    }

    private IEnumerable<CharacterData> TextNodesInPreOrder(Node root)
    {
        if (root is CharacterData rootCd) { yield return rootCd; yield break; }
        var stack = new Stack<Node>();
        for (var c = root.LastChild; c is not null; c = c.PreviousSibling) stack.Push(c);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is CharacterData cd) { yield return cd; continue; }
            for (var c = n.LastChild; c is not null; c = c.PreviousSibling) stack.Push(c);
        }
    }

    private bool IsAfterOrAt(Node node)
    {
        // Is node at or after StartContainer?
        if (ReferenceEquals(node, StartContainer)) return true;
        return ComparePoints(StartContainer, StartOffset, node, 0) <= 0;
    }

    private bool IsBeforeOrAt(Node node)
    {
        // Is node at or before EndContainer?
        if (ReferenceEquals(node, EndContainer)) return true;
        return ComparePoints(node, NodeLength(node), EndContainer, EndOffset) <= 0;
    }

    // -----------------------------------------------------------------------
    // Extraction / manipulation (§4.6.7) — minimal stubs for WPT coverage

    /// <summary>§4.6.7 deleteContents — remove all nodes/text covered by range;
    /// collapse to start. This is a structural operation; simplified implementation
    /// handles the most common cases (single-node and same-parent).</summary>
    public void DeleteContents()
    {
        if (Collapsed) return;

        // Clone boundary points to be stable during mutation.
        var sc = StartContainer; var so = StartOffset;
        var ec = EndContainer; var eo = EndOffset;

        if (ReferenceEquals(sc, ec))
        {
            // Same node — trim the character data or remove child range.
            if (sc is CharacterData cd)
            {
                cd.Data = cd.Data[..so] + cd.Data[eo..];
                Collapse(true);
                return;
            }
            // Element/Document — remove children [so, eo).
            RemoveChildrenInRange(sc, so, eo);
            Collapse(true);
            return;
        }

        // Collect fully-contained nodes and remove them.
        foreach (var node in NodesInRange().ToList())
            node.RemoveFromParent();

        // Trim the end container character data.
        if (ec is CharacterData ecd)
            ecd.Data = ecd.Data[eo..];

        // Trim the start container character data.
        if (sc is CharacterData scd)
            scd.Data = scd.Data[..so];

        Collapse(true);
    }

    // -----------------------------------------------------------------------
    // Helpers

    /// <summary>DOM §4.1: node length. For CharacterData it's the number of
    /// code units; for DocumentType it's 0; for others it's childNode count.</summary>
    public static int NodeLength(Node node) => node switch
    {
        DocumentType => 0,
        CharacterData cd => cd.Data.Length,
        _ => ChildCount(node),
    };

    private static int ChildCount(Node node)
    {
        var count = 0;
        for (var c = node.FirstChild; c is not null; c = c.NextSibling) count++;
        return count;
    }

    /// <summary>Index of <paramref name="node"/> in its parent's child list (0-based).</summary>
    public static int IndexOf(Node node)
    {
        var index = 0;
        for (var c = node.ParentNode?.FirstChild; c is not null && !ReferenceEquals(c, node); c = c.NextSibling)
            index++;
        return index;
    }

    /// <summary>True when <paramref name="a"/> is an inclusive ancestor of <paramref name="b"/>.</summary>
    private static bool IsInclusiveAncestor(Node a, Node b)
    {
        for (var n = b; n is not null; n = n.ParentNode)
            if (ReferenceEquals(n, a)) return true;
        return false;
    }

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are
    /// in the same tree (have the same root node).</summary>
    private static bool SameRoot(Node a, Node b) =>
        ReferenceEquals(Root(a), Root(b));

    private static Node Root(Node n)
    {
        var cur = n;
        while (cur.ParentNode is not null) cur = cur.ParentNode;
        return cur;
    }

    /// <summary>DOM §4.4 tree order comparison of two boundary points.
    /// Returns &lt;0 if (nodeA, offsetA) is before (nodeB, offsetB),
    /// 0 if equal, &gt;0 if after.</summary>
    private static int ComparePoints(Node nodeA, int offsetA, Node nodeB, int offsetB)
    {
        if (ReferenceEquals(nodeA, nodeB)) return offsetA.CompareTo(offsetB);

        // Find positions in a pre-order list.
        // Collect ancestors of both from root downward.
        var ancestorsA = AncestorsFromRoot(nodeA);
        var ancestorsB = AncestorsFromRoot(nodeB);

        // Find first divergence in ancestor chains.
        var minLen = Math.Min(ancestorsA.Count, ancestorsB.Count);
        int i;
        for (i = 0; i < minLen; i++)
            if (!ReferenceEquals(ancestorsA[i], ancestorsB[i])) break;

        if (i == minLen)
        {
            // One is ancestor of other. Ancestor comes first only when the
            // deeper node is past the offset in the ancestor.
            if (ancestorsA.Count < ancestorsB.Count)
            {
                // nodeA is an ancestor of nodeB. The question: is offsetA
                // before or after the descendant's position?
                var childInA = ancestorsB[i]; // this child of nodeA that contains nodeB
                var childIndex = IndexOf(childInA);
                return offsetA <= childIndex ? -1 : 1;
            }
            else
            {
                // nodeB is ancestor of nodeA.
                var childInB = ancestorsA[i];
                var childIndex = IndexOf(childInB);
                return childIndex < offsetB ? -1 : 1;
            }
        }

        // The two siblings are ancestorsA[i] and ancestorsB[i], siblings in ancestorsA[i-1].
        // Compare their positions.
        var siblingA = ancestorsA[i];
        var siblingB = ancestorsB[i];
        for (var s = siblingA.NextSibling; s is not null; s = s.NextSibling)
            if (ReferenceEquals(s, siblingB)) return -1; // A is before B
        return 1; // B is before A
    }

    private static List<Node> AncestorsFromRoot(Node node)
    {
        var chain = new List<Node>();
        for (var n = node; n is not null; n = n.ParentNode) chain.Add(n);
        chain.Reverse(); // root first
        return chain;
    }

    private static void ValidateNotDocumentType(Node node, string op)
    {
        // setStart/setEnd: if node is a doctype, throw InvalidNodeTypeError.
        if (node is DocumentType)
            throw DomRangeException.Create("InvalidNodeTypeError",
                $"{op}: doctype nodes cannot be boundary points");
    }

    private static void ValidateOffset(Node node, int offset, string op)
    {
        var len = NodeLength(node);
        if (offset < 0 || offset > len)
            throw DomRangeException.Create("IndexSizeError",
                $"{op}: offset {offset} is out of range [0, {len}]");
    }

    /// <summary>Enumerate nodes that are <em>fully</em> between start and end
    /// (i.e. neither the start nor end container, but wholly contained within
    /// the range). Used by Stringify and DeleteContents.</summary>
    private IEnumerable<Node> NodesInRange()
    {
        // Walk the tree in pre-order starting from the root; emit nodes that
        // are after the start and before the end.
        var root = CommonAncestorContainer;

        // Collect all descendants of the common ancestor in pre-order.
        var stack = new Stack<Node>();
        for (var c = root.LastChild; c is not null; c = c.PreviousSibling)
            stack.Push(c);

        while (stack.Count > 0)
        {
            var n = stack.Pop();

            var afterStart = !IsInclusiveAncestor(n, StartContainer)
                && ComparePoints(StartContainer, StartOffset, n, 0) <= 0;
            var beforeEnd = !IsInclusiveAncestor(n, EndContainer)
                && ComparePoints(n, NodeLength(n), EndContainer, EndOffset) <= 0;

            if (afterStart && beforeEnd)
            {
                yield return n;
                // Don't recurse into it — we already yielded it whole.
                continue;
            }

            // Push children for further examination.
            for (var c = n.LastChild; c is not null; c = c.PreviousSibling)
                stack.Push(c);
        }
    }

    private static void RemoveChildrenInRange(Node parent, int fromIndex, int toIndex)
    {
        var toRemove = new List<Node>();
        var i = 0;
        for (var c = parent.FirstChild; c is not null; c = c.NextSibling)
        {
            if (i >= fromIndex && i < toIndex) toRemove.Add(c);
            i++;
        }
        foreach (var c in toRemove) c.RemoveFromParent();
    }
}

/// <summary>
/// Lightweight exception type used by DomRange internals to signal DOM
/// errors. The binding layer catches these and converts them to JS
/// <c>DOMException</c> objects. Only <see cref="DomRangeException.DomName"/> is used for routing;
/// the message is for debugging.
/// </summary>
public sealed class DomRangeException : Exception
{
    /// <summary>DOMException name, e.g. "IndexSizeError".</summary>
    public string DomName { get; }

    public DomRangeException() : base() { DomName = "Error"; }
    public DomRangeException(string message) : base(message) { DomName = "Error"; }
    public DomRangeException(string message, Exception inner) : base(message, inner) { DomName = "Error"; }

    /// <summary>Create with a DOM error name and detail message.</summary>
    public DomRangeException(string domName, string message, string? _unused) : base(message)
    {
        DomName = domName;
    }

    /// <summary>Factory: create with a DOM error name and detail message.</summary>
    public static DomRangeException Create(string domName, string message) =>
        new(domName, message, null);
}
