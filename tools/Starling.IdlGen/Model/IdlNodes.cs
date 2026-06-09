namespace Starling.IdlGen.Model;

// The abstract syntax tree for a Web IDL fragment. One IdlDocument per .idl
// file. These records mirror the Web IDL grammar (https://webidl.spec.whatwg.org/)
// closely enough to round-trip every construct the vendored specs use. Merge
// passes and the emitter run off this tree.

public sealed record IdlDocument
{
    public IReadOnlyList<IdlDefinition> Definitions { get; init; } = [];
}

// ---- Types -----------------------------------------------------------------

// A type reference. A union has a non-empty Union list and an empty Name.
// sequence<T>, Promise<T>, FrozenArray<T>, ObservableArray<T> carry one TypeArg;
// record<K,V> carries two.
public sealed record IdlType
{
    public string Name { get; init; } = "";
    public bool Nullable { get; init; }
    public IReadOnlyList<IdlType> TypeArgs { get; init; } = [];
    public IReadOnlyList<IdlType> Union { get; init; } = [];
    public IReadOnlyList<IdlExtendedAttribute> ExtendedAttributes { get; init; } = [];

    public bool IsUnion => Union.Count > 0;
}

// ---- Extended attributes ---------------------------------------------------

public enum IdlExtAttrKind
{
    NoArgs,        // [NewObject]
    Ident,         // [Exposed=Window], [PutForwards=name]
    IdentList,     // [Exposed=(Window,Worker)]
    ArgList,       // [Constructor(double x)]  (legacy)
    NamedArgList,  // [LegacyFactoryFunction=Image(DOMString src)]
    Wildcard,      // [Exposed=*]
}

public sealed record IdlExtendedAttribute
{
    public required string Name { get; init; }
    public IdlExtAttrKind Kind { get; init; } = IdlExtAttrKind.NoArgs;
    public string? Identifier { get; init; }
    public IReadOnlyList<string> Identifiers { get; init; } = [];
    public IReadOnlyList<IdlArgument> Arguments { get; init; } = [];
}

// ---- Arguments & defaults --------------------------------------------------

public sealed record IdlArgument
{
    public required string Name { get; init; }
    public required IdlType Type { get; init; }
    public bool Optional { get; init; }
    public bool Variadic { get; init; }
    public IdlDefaultValue? Default { get; init; }
    public IReadOnlyList<IdlExtendedAttribute> ExtendedAttributes { get; init; } = [];
}

public enum IdlDefaultKind
{
    String, Number, Boolean, Null, EmptySequence, EmptyDictionary, Infinity, NegInfinity, NaN,
}

public sealed record IdlDefaultValue
{
    public required IdlDefaultKind Kind { get; init; }
    public string? Value { get; init; }   // literal text for String / Number / Boolean
}

// ---- Members ---------------------------------------------------------------

public abstract record IdlMember
{
    public IReadOnlyList<IdlExtendedAttribute> ExtendedAttributes { get; init; } = [];
}

public sealed record IdlConstant : IdlMember
{
    public required string Name { get; init; }
    public required IdlType Type { get; init; }
    public required string Value { get; init; }
}

public sealed record IdlAttribute : IdlMember
{
    public required string Name { get; init; }
    public required IdlType Type { get; init; }
    public bool Readonly { get; init; }
    public bool Static { get; init; }
    public bool Stringifier { get; init; }
    public bool Inherit { get; init; }
}

public enum IdlSpecialKind { None, Getter, Setter, Deleter }

public sealed record IdlOperation : IdlMember
{
    public string? Name { get; init; }   // null for an anonymous special operation
    public required IdlType ReturnType { get; init; }
    public IReadOnlyList<IdlArgument> Arguments { get; init; } = [];
    public bool Static { get; init; }
    public IdlSpecialKind Special { get; init; } = IdlSpecialKind.None;
    public bool Stringifier { get; init; }
}

public sealed record IdlConstructor : IdlMember
{
    public IReadOnlyList<IdlArgument> Arguments { get; init; } = [];
}

public sealed record IdlStringifier : IdlMember;   // bare "stringifier;"

public sealed record IdlIterable : IdlMember
{
    public IdlType? KeyType { get; init; }          // null => value iterator
    public required IdlType ValueType { get; init; }
    public bool Async { get; init; }
    public IReadOnlyList<IdlArgument> Arguments { get; init; } = [];   // async iterable args
}

public sealed record IdlMaplike : IdlMember
{
    public required IdlType KeyType { get; init; }
    public required IdlType ValueType { get; init; }
    public bool Readonly { get; init; }
}

public sealed record IdlSetlike : IdlMember
{
    public required IdlType ValueType { get; init; }
    public bool Readonly { get; init; }
}

// ---- Definitions -----------------------------------------------------------

public abstract record IdlDefinition
{
    public required string Name { get; init; }
    public IReadOnlyList<IdlExtendedAttribute> ExtendedAttributes { get; init; } = [];
    public bool Partial { get; init; }
}

public sealed record IdlInterface : IdlDefinition
{
    public string? Inherits { get; init; }
    public bool Mixin { get; init; }
    public bool Callback { get; init; }    // callback interface
    public IReadOnlyList<IdlMember> Members { get; init; } = [];
}

public sealed record IdlNamespace : IdlDefinition
{
    public IReadOnlyList<IdlMember> Members { get; init; } = [];
}

public sealed record IdlDictionary : IdlDefinition
{
    public string? Inherits { get; init; }
    public IReadOnlyList<IdlDictionaryMember> Members { get; init; } = [];
}

public sealed record IdlDictionaryMember
{
    public required string Name { get; init; }
    public required IdlType Type { get; init; }
    public bool Required { get; init; }
    public IdlDefaultValue? Default { get; init; }
    public IReadOnlyList<IdlExtendedAttribute> ExtendedAttributes { get; init; } = [];
}

public sealed record IdlEnum : IdlDefinition
{
    public IReadOnlyList<string> Values { get; init; } = [];
}

public sealed record IdlCallback : IdlDefinition
{
    public required IdlType ReturnType { get; init; }
    public IReadOnlyList<IdlArgument> Arguments { get; init; } = [];
}

public sealed record IdlTypedef : IdlDefinition
{
    public required IdlType Type { get; init; }
}

// "Target includes Mixin;" — Name is the including interface.
public sealed record IdlIncludes : IdlDefinition
{
    public required string Mixin { get; init; }
}
