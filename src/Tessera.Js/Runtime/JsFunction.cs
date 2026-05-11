using Tessera.Js.Bytecode;

namespace Tessera.Js.Runtime;

/// <summary>
/// User-defined JS function. Wraps a compiled <see cref="Chunk"/> plus the
/// declared parameter count. Called from the VM via the regular
/// <see cref="Opcode.Call"/> opcode — the dispatcher inspects the callee
/// object and pushes a new frame for <see cref="JsFunction"/>, vs. invoking
/// the native body for <see cref="JsNativeFunction"/>.
/// </summary>
/// <remarks>
/// Closure / upvalue capture is wp:M3-04c. For now functions can read/write
/// only their own locals + globals — not enclosing-scope locals.
/// </remarks>
public sealed class JsFunction : JsObject
{
    public string Name { get; }
    public Chunk Body { get; }
    public int ArityDeclared { get; }

    public JsFunction(string name, Chunk body, int arityDeclared)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        ArityDeclared = arityDeclared;
    }

    public override string ToString()
        => $"function {Name}({ArityDeclared}) {{ [bytecode] }}";
}
