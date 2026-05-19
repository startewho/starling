namespace Starling.Js.RegExp;

/// <summary>AST nodes produced by <see cref="RegexParser"/>. The compiler in
/// <see cref="RegexCompiler"/> consumes these to emit a flat instruction array
/// for the Pike VM.</summary>
public abstract record RegexNode;

public sealed record EmptyNode : RegexNode;
public sealed record LiteralNode(int CodePoint) : RegexNode;
public sealed record AnyNode(bool DotAll) : RegexNode;
public sealed record CharClassNode(RegexCharClass Klass) : RegexNode;
public sealed record AnchorNode(AnchorKind Kind) : RegexNode;
public sealed record SequenceNode(System.Collections.Generic.IReadOnlyList<RegexNode> Items) : RegexNode;
public sealed record AlternationNode(System.Collections.Generic.IReadOnlyList<RegexNode> Alternatives) : RegexNode;
public sealed record QuantifierNode(RegexNode Child, int Min, int Max, bool Greedy) : RegexNode;  // Max=-1 means unbounded
public sealed record GroupNode(int? CaptureIndex, string? Name, RegexNode Child) : RegexNode;
public sealed record BackrefNode(int Group) : RegexNode;
public sealed record NamedBackrefNode(string Name) : RegexNode;
public sealed record LookaroundNode(bool Behind, bool Negative, RegexNode Child) : RegexNode;

public enum AnchorKind { StartOfInput, EndOfInput, WordBoundary, NonWordBoundary }
