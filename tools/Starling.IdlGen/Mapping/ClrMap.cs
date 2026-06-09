using System.Reflection;
using Starling.Dom;
using Starling.Dom.Events;

namespace Starling.IdlGen.Mapping;

// How a CLR member's type marshals to and from a JsValue. Only the mechanical
// cases are listed; anything else is left to manual bindings (the override
// layer) and reported by the emitter. Node covers any EventTarget-derived class,
// which wraps through DomWrappers.Wrap so object identity holds.
public enum ClrScalar { String, NullableString, Bool, NullableBool, Number, NullableNumber, Node }

public sealed record ClrProperty(string Name, ClrScalar Scalar, bool HasPublicSetter);

// A CLR method the emitter can bind mechanically. Params are restricted to the
// scalar kinds the emitter knows how to convert from a JsValue (string, bool).
// Return is one scalar kind, or null for void.
public sealed record ClrMethod(string Name, IReadOnlyList<ClrScalar> Params, ClrScalar? Return);

// Reflects over the Starling DOM assembly to resolve the CLR type and members an
// IDL interface and its attributes bind to. Name mapping is camelCase to
// PascalCase, with the result confirmed against the real type.
public sealed class ClrMap
{
    private readonly Dictionary<string, Type> _typesByName;

    public ClrMap()
    {
        _typesByName = typeof(Node).Assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true } or { IsInterface: true })
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public Type? FindType(string idlInterfaceName) =>
        _typesByName.GetValueOrDefault(idlInterfaceName);

    // Resolves an IDL attribute to a CLR property with a mechanically mappable
    // scalar type. Returns null when there is no matching property or its type
    // needs manual marshalling (interface, collection, dictionary, and so on).
    public ClrProperty? FindScalarProperty(Type clrType, string idlAttributeName)
    {
        string pascal = Pascal(idlAttributeName);
        var prop = clrType.GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance);
        if (prop is null) return null;

        var scalar = Classify(prop.PropertyType);
        if (scalar is null) return null;

        bool publicSetter = prop.SetMethod is { IsPublic: true };
        return new ClrProperty(prop.Name, scalar.Value, publicSetter);
    }

    // Resolves an IDL operation to a CLR method whose parameters are all string
    // or bool and whose return is void or a mappable scalar. Returns null when no
    // such method matches by name and arity (so the operation needs a manual
    // binding). Numeric and node parameters are deferred, so they fall through.
    public ClrMethod? FindScalarMethod(Type clrType, string idlName, int argCount)
    {
        string pascal = Pascal(idlName);
        foreach (var m in clrType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.IsSpecialName || !string.Equals(m.Name, pascal, StringComparison.Ordinal)) continue;
            var pars = m.GetParameters();
            if (pars.Length != argCount) continue;

            var ps = new List<ClrScalar>(pars.Length);
            bool ok = true;
            foreach (var p in pars)
            {
                var s = Classify(p.ParameterType);
                if (s is ClrScalar.String or ClrScalar.Bool) ps.Add(s.Value);
                else { ok = false; break; }
            }
            if (!ok) continue;

            ClrScalar? ret;
            if (m.ReturnType == typeof(void)) ret = null;
            else { var rs = Classify(m.ReturnType); if (rs is null) continue; ret = rs; }

            return new ClrMethod(m.Name, ps, ret);
        }
        return null;
    }

    private static ClrScalar? Classify(Type t)
    {
        if (t == typeof(string)) return ClrScalar.String;
        if (t == typeof(bool)) return ClrScalar.Bool;
        if (IsNumber(t)) return ClrScalar.Number;

        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null)
        {
            if (underlying == typeof(bool)) return ClrScalar.NullableBool;
            if (IsNumber(underlying)) return ClrScalar.NullableNumber;
        }

        // Any EventTarget-derived class wraps to a JS object through DomWrappers.
        if (typeof(EventTarget).IsAssignableFrom(t)) return ClrScalar.Node;

        // A reference type that is not string is treated as a nullable string only
        // when it is actually string?; the compiler models string? as string, so a
        // genuinely nullable string is indistinguishable here and handled as String.
        return null;
    }

    private static bool IsNumber(Type t) =>
        t == typeof(int) || t == typeof(uint) || t == typeof(short) || t == typeof(ushort)
        || t == typeof(long) || t == typeof(ulong) || t == typeof(byte) || t == typeof(sbyte)
        || t == typeof(double) || t == typeof(float);

    public static string Pascal(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
