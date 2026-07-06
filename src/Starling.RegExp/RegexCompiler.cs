// SPDX-License-Identifier: Apache-2.0

namespace Starling.RegExp;

/// <summary>AST → flat Pike-VM instruction array via Thompson NFA construction.</summary>
public sealed class RegexCompiler
{
    private readonly List<RegexInst> _code = new();
    private readonly List<RegexCharClass> _klasses = new();
    private readonly List<RegexProgram> _subs = new();
    private readonly RegexFlags _flags;
    private readonly bool _ignoreCase;
    private readonly bool _captureGroups;
    private readonly int _captureCount;
    private readonly IReadOnlyDictionary<string, int> _namedCaptures;
    private readonly bool _reversed;
    private int _loopCount;

    public RegexCompiler(RegexFlags flags, int captureCount, IReadOnlyDictionary<string, int> named, bool captureGroups = true, bool reversed = false)
    {
        _flags = flags;
        _ignoreCase = (flags & RegexFlags.IgnoreCase) != 0;
        _captureGroups = captureGroups;
        _captureCount = captureCount;
        _namedCaptures = named;
        _reversed = reversed;
    }

    public RegexProgram Compile(RegexNode root)
    {
        // Capture group 0 = entire match. We emit SaveStart(0)/SaveEnd(0) around the root.
        Emit(RegexOp.SaveStart, 0, 0);
        Walk(root);
        Emit(RegexOp.SaveEnd, 0, 0);
        Emit(RegexOp.Match, 0, 0);
        return new RegexProgram(_code, _klasses, _subs, _captureCount, _namedCaptures, _reversed, _loopCount);
    }

    private int Emit(RegexOp op, int a, int b)
    {
        var pc = _code.Count;
        _code.Add(new RegexInst(op, a, b));
        return pc;
    }

    private void Patch(int pc, RegexOp op, int a, int b)
    {
        _code[pc] = new RegexInst(op, a, b);
    }

    private int Here => _code.Count;

    private int AddKlass(RegexCharClass k)
    {
        _klasses.Add(k);
        return _klasses.Count - 1;
    }

    private int AddSub(RegexProgram p)
    {
        _subs.Add(p);
        return _subs.Count - 1;
    }

    private void Walk(RegexNode node)
    {
        switch (node)
        {
            case EmptyNode:
                return;
            case LiteralNode lit:
                Emit(_ignoreCase ? RegexOp.CharIgnoreCase : RegexOp.Char, lit.CodePoint, 0);
                return;
            case AnyNode an:
                Emit(an.DotAll ? RegexOp.Any : RegexOp.AnyExceptNewline, 0, 0);
                return;
            case CharClassNode klass:
                Emit(RegexOp.CharClass, AddKlass(klass.Klass), 0);
                return;
            case AnchorNode anchor:
                Emit(anchor.Kind switch
                {
                    AnchorKind.StartOfInput => RegexOp.AssertStart,
                    AnchorKind.EndOfInput => RegexOp.AssertEnd,
                    AnchorKind.WordBoundary => RegexOp.AssertWordBoundary,
                    _ => RegexOp.AssertNonWordBoundary,
                }, 0, 0);
                return;
            case SequenceNode seq:
                // A lookbehind sub-program consumes right-to-left, so its
                // concatenation compiles in reverse source order.
                if (_reversed)
                {
                    for (var i = seq.Items.Count - 1; i >= 0; i--)
                    {
                        Walk(seq.Items[i]);
                    }
                }
                else
                {
                    foreach (var i in seq.Items)
                    {
                        Walk(i);
                    }
                }

                return;
            case AlternationNode alt:
                WalkAlternation(alt);
                return;
            case QuantifierNode q:
                WalkQuantifier(q);
                return;
            case GroupNode g:
                {
                    if (g.CaptureIndex is int idx && _captureGroups)
                    {
                        // Right-to-left matching reaches the group's right edge
                        // first, so the save ops swap.
                        Emit(_reversed ? RegexOp.SaveEnd : RegexOp.SaveStart, idx, 0);
                        Walk(g.Child);
                        Emit(_reversed ? RegexOp.SaveStart : RegexOp.SaveEnd, idx, 0);
                    }
                    else
                    {
                        Walk(g.Child);
                    }
                    return;
                }
            case BackrefNode bref:
                Emit(RegexOp.Backref, bref.Group, 0);
                return;
            case NamedBackrefNode nbref:
                if (!_namedCaptures.TryGetValue(nbref.Name, out var ni))
                {
                    throw new RegexSyntaxException($"Invalid named backreference: {nbref.Name}");
                }

                Emit(RegexOp.Backref, ni, 0);
                return;
            case LookaroundNode la:
                {
                    // The sub-program shares the outer capture numbering; a
                    // successful positive assertion propagates its captures
                    // back out (the VM copies the sub slots, minus group 0).
                    var subCompiler = new RegexCompiler(_flags, _captureCount, _namedCaptures,
                        captureGroups: _captureGroups, reversed: la.Behind);
                    var subProg = subCompiler.Compile(la.Child);
                    var subIdx = AddSub(subProg);
                    int arg2 = (la.Negative ? 1 : 0) | (la.Behind ? 2 : 0);
                    Emit(RegexOp.Lookaround, subIdx, arg2);
                    return;
                }
        }
    }

    private void WalkAlternation(AlternationNode alt)
    {
        // For N alternatives, emit a chain of Splits.
        // Pattern (Russ Cox): split L1, L2; L1: code(a); jmp End; L2: code(b); End:
        var ends = new List<int>();
        for (var i = 0; i < alt.Alternatives.Count; i++)
        {
            var isLast = i == alt.Alternatives.Count - 1;
            int splitPc = -1;
            if (!isLast)
            {
                splitPc = Emit(RegexOp.Split, 0, 0); // patched
            }
            var branchStart = Here;
            Walk(alt.Alternatives[i]);
            if (!isLast)
            {
                ends.Add(Emit(RegexOp.Jmp, 0, 0));
                var nextStart = Here;
                Patch(splitPc, RegexOp.Split, branchStart, nextStart);
            }
        }
        foreach (var jpc in ends)
        {
            Patch(jpc, RegexOp.Jmp, Here, 0);
        }
    }

    private void WalkQuantifier(QuantifierNode q)
    {
        // §22.2.2.3.1 RepeatMatcher: every entry into the Atom clears the
        // captures positioned inside it, so a group that doesn't participate
        // in the final iteration reads back undefined.
        var hasReset = TryGetCaptureRange(q.Child, out var minGroup, out var maxGroup) && _captureGroups;

        // Compile minimum repetitions in series.
        for (var i = 0; i < q.Min; i++)
        {
            if (hasReset)
            {
                Emit(RegexOp.ResetCaptures, minGroup, maxGroup);
            }

            Walk(q.Child);
        }

        // A bounded max beyond any realizable input length (strings cap well
        // under 2^30 code units) compiles as an unbounded tail instead of
        // materializing millions of body copies.
        var max = q.Max;
        if (max != -1 && max - q.Min > 1_000_000)
        {
            max = -1;
        }

        if (max == -1)
        {
            // {min,} — kleene star tail.
            // L: split body, end  (or end, body for lazy)
            // body: ...; jmp L
            // end:
            // A body that can match empty carries RepeatMatcher's progress
            // guard: an iteration that consumed nothing fails its path
            // (backtracking then tries the body's other alternatives).
            var guarded = CanMatchEmpty(q.Child);
            var loopId = guarded ? _loopCount++ : -1;
            var loopStart = Emit(RegexOp.Split, 0, 0);
            var bodyStart = Here;
            if (guarded)
            {
                Emit(RegexOp.MarkPos, loopId, 0);
            }

            if (hasReset)
            {
                Emit(RegexOp.ResetCaptures, minGroup, maxGroup);
            }

            Walk(q.Child);
            if (guarded)
            {
                Emit(RegexOp.ProgressJmp, loopId, loopStart);
            }
            else
            {
                Emit(RegexOp.Jmp, loopStart, 0);
            }

            var end = Here;
            if (q.Greedy)
            {
                Patch(loopStart, RegexOp.Split, bodyStart, end);
            }
            else
            {
                Patch(loopStart, RegexOp.Split, end, bodyStart);
            }
        }
        else
        {
            // {min,max} — emit (max-min) optional copies.
            var remaining = max - q.Min;
            var splitPcs = new List<int>();
            var bodyStarts = new List<int>();
            for (var i = 0; i < remaining; i++)
            {
                splitPcs.Add(Emit(RegexOp.Split, 0, 0));
                bodyStarts.Add(Here);
                if (hasReset)
                {
                    Emit(RegexOp.ResetCaptures, minGroup, maxGroup);
                }

                Walk(q.Child);
            }
            var end = Here;
            for (var i = 0; i < remaining; i++)
            {
                if (q.Greedy)
                {
                    Patch(splitPcs[i], RegexOp.Split, bodyStarts[i], end);
                }
                else
                {
                    Patch(splitPcs[i], RegexOp.Split, end, bodyStarts[i]);
                }
            }
        }
    }

    /// <summary>Conservative nullability: can this subtree match the empty
    /// string?</summary>
    private static bool CanMatchEmpty(RegexNode node) => node switch
    {
        EmptyNode => true,
        AnchorNode => true,
        LookaroundNode => true,
        BackrefNode => true,
        NamedBackrefNode => true,
        QuantifierNode q => q.Min == 0 || CanMatchEmpty(q.Child),
        GroupNode g => CanMatchEmpty(g.Child),
        SequenceNode seq => AllCanMatchEmpty(seq.Items),
        AlternationNode alt => AnyCanMatchEmpty(alt.Alternatives),
        _ => false,
    };

    private static bool AllCanMatchEmpty(IReadOnlyList<RegexNode> items)
    {
        foreach (var i in items)
        {
            if (!CanMatchEmpty(i))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AnyCanMatchEmpty(IReadOnlyList<RegexNode> alts)
    {
        foreach (var a in alts)
        {
            if (CanMatchEmpty(a))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the (contiguous, source-ordered) capture index range
    /// inside a subtree. False when the subtree captures nothing.</summary>
    private static bool TryGetCaptureRange(RegexNode node, out int min, out int max)
    {
        min = int.MaxValue;
        max = int.MinValue;
        ScanCaptures(node, ref min, ref max);
        return max >= 0;
    }

    private static void ScanCaptures(RegexNode node, ref int min, ref int max)
    {
        switch (node)
        {
            case GroupNode g:
                if (g.CaptureIndex is int idx)
                {
                    if (idx < min) { min = idx; }
                    if (idx > max) { max = idx; }
                }

                ScanCaptures(g.Child, ref min, ref max);
                return;
            case SequenceNode seq:
                foreach (var i in seq.Items)
                {
                    ScanCaptures(i, ref min, ref max);
                }

                return;
            case AlternationNode alt:
                foreach (var a in alt.Alternatives)
                {
                    ScanCaptures(a, ref min, ref max);
                }

                return;
            case QuantifierNode q:
                ScanCaptures(q.Child, ref min, ref max);
                return;
            case LookaroundNode la:
                ScanCaptures(la.Child, ref min, ref max);
                return;
        }
    }
}
