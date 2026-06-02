namespace Starling.Dom;

public sealed class DocumentType : Node
{
    public DocumentType(string name, string publicId = "", string systemId = "")
    {
        // DOM §4.6 doesn't require Name to be non-empty, and the HTML parser
        // explicitly emits empty-name doctypes for malformed input like
        // <!DOCTYPE > per WHATWG HTML §13.2.5.74. Reject null but allow "".
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        PublicId = publicId;
        SystemId = systemId;
    }

    public override NodeKind Kind => NodeKind.DocumentType;

    public override string NodeName => Name;

    public string Name { get; }

    public string PublicId { get; }

    public string SystemId { get; }
}
