namespace Starling.Js.RegExp;

/// <summary>Pike-VM opcodes per Russ Cox's "RE: VM approach" paper.</summary>
public enum RegexOp
{
    Char,        // consume code unit equal to Arg1 (case sensitivity handled by compiler)
    CharIgnoreCase, // consume code unit equal to Arg1, case-folded
    CharClass,   // consume code unit matched by char-class at Klasses[Arg1]
    Any,         // consume any code unit
    AnyExceptNewline, // consume any code unit that isn't a line terminator
    Jmp,         // unconditional jump to Arg1
    Split,       // fork: try Arg1 first, then Arg2 (greedy ordering); swap for lazy
    Match,       // accept
    SaveStart,   // record capture[Arg1].start = sp
    SaveEnd,     // record capture[Arg1].end = sp
    AssertStart, // ^
    AssertEnd,   // $
    AssertWordBoundary,
    AssertNonWordBoundary,
    Backref,     // attempt to match the prior captured text of group Arg1
    Lookaround,  // begin lookaround; Arg1=sub-program index in Subs; Arg2=negative?(1:0)|behind?(2:0)
}

/// <summary>Flat instruction with up to two integer operands.</summary>
public readonly record struct RegexInst(RegexOp Op, int Arg1, int Arg2);
