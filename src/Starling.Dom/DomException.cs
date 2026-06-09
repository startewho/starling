namespace Starling.Dom;

/// <summary>
/// A DOM error raised by a spec algorithm in the DOM layer, carrying the
/// DOMException name (e.g. "HierarchyRequestError", "NotFoundError"). The
/// bindings layer translates it to a JS DOMException. Keeping the spec
/// validation and the error-name choice here — not in the bindings — lets the
/// generated bindings stay thin, the way Chromium's generated V8 bindings
/// dispatch to hand-written impl methods that raise the exception.
/// </summary>
public sealed class DomException : Exception
{
    /// <summary>The DOMException name, e.g. "HierarchyRequestError".</summary>
    public string Name { get; }

    public DomException() : base() => Name = "Error";
    public DomException(string message) : base(message) => Name = "Error";
    public DomException(string message, Exception innerException) : base(message, innerException) => Name = "Error";

    /// <summary>Create with a DOM error name and detail message.</summary>
    public DomException(string name, string message, string? unused) : base(message) => Name = name;

    public static DomException Create(string name, string message) => new(name, message, null);
}
