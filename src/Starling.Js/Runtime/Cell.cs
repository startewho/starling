namespace Starling.Js.Runtime;

/// <summary>
/// gap:closure-write-back (wp:M3-04c2). A shared, mutable storage box for a
/// JS binding that is captured by one or more nested functions. Reads and
/// writes from any function that holds a reference to the same
/// <see cref="Cell"/> see each other immediately — this is what makes a JS
/// closure a live binding rather than a snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Each var/let/const binding in an outer function is either a plain
/// <c>JsValue</c> slot (cheap, no boxing) or a <see cref="Cell"/> stored in
/// that slot (boxed once, then aliased through every nested closure that
/// captures the name). The compiler picks the representation per slot via a
/// simple "is referenced inside any nested function body?" pass — see
/// <c>JsCompiler.AnalyzeCapturedLocals</c>.
/// </para>
/// <para>
/// We model the cell as a <see cref="JsObject"/> subclass so it can travel
/// through the existing <see cref="JsValue"/>-typed eval stack and local
/// slots without introducing a parallel value type. It is never reachable
/// from user JS: nothing in the prototype chain or property bag exposes the
/// <see cref="Value"/> field; the dedicated opcodes (LoadCellLocal /
/// StoreCellLocal / StoreUpvalue) read and write it directly.
/// </para>
/// </remarks>
internal sealed class Cell : JsObject
{
    /// <summary>The current binding value. Mutated in-place by
    /// StoreCellLocal / StoreUpvalue and read by LoadCellLocal /
    /// LoadUpvalue.</summary>
    public JsValue Value;

    public Cell(JsValue initial) { Value = initial; }
}
