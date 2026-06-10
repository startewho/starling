using Starling.IdlGen.Model;

namespace Starling.IdlGen.Merging;

// The consolidated Web IDL model after merging every parsed fragment. Partials
// are folded into their main definition, includes statements have copied mixin
// members into their interfaces, and definitions are keyed by name. The emitter
// and type mapper run off this, not the raw per-file documents.
public sealed class WebIdlModel
{
    public required IReadOnlyDictionary<string, IdlInterface> Interfaces { get; init; }
    public required IReadOnlyDictionary<string, IdlInterface> Mixins { get; init; }
    public required IReadOnlyDictionary<string, IdlDictionary> Dictionaries { get; init; }
    public required IReadOnlyDictionary<string, IdlEnum> Enums { get; init; }
    public required IReadOnlyDictionary<string, IdlCallback> Callbacks { get; init; }
    public required IReadOnlyDictionary<string, IdlNamespace> Namespaces { get; init; }
    public required IReadOnlyDictionary<string, IdlType> Typedefs { get; init; }

    // Interface names that an includes statement referenced but whose mixin was
    // not in the parsed set (cross-spec mixins we have not vendored yet).
    public required IReadOnlyList<string> UnresolvedIncludes { get; init; }

    // Resolves a type name through the typedef map, following chains. Returns the
    // underlying type, or the input unchanged when it is not a typedef.
    public IdlType ResolveTypedef(IdlType type)
    {
        var seen = new HashSet<string>();
        var current = type;
        while (!current.IsUnion
               && current.TypeArgs.Count == 0
               && Typedefs.TryGetValue(current.Name, out var target)
               && seen.Add(current.Name))
        {
            // Carry nullability and type-level extended attributes onto the target.
            current = target with
            {
                Nullable = target.Nullable || current.Nullable,
                ExtendedAttributes = current.ExtendedAttributes.Count > 0
                    ? [.. current.ExtendedAttributes, .. target.ExtendedAttributes]
                    : target.ExtendedAttributes,
            };
        }
        return current;
    }
}
