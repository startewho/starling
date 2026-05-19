using Tessera.Dom;
using Tessera.Js.Runtime;

namespace Tessera.Bindings;

/// <summary>
/// B5-1 stop-gap selector engine. Accepts a single simple selector — one of
/// <c>#id</c>, <c>.class</c>, or a bare tag name (<c>div</c>). Throws a JS
/// <c>SyntaxError</c> for anything more complex. The real CSS-selector engine
/// is tracked as a follow-up; this is just enough for boot-time framework
/// probes ("does <c>document.querySelector('#root')</c> exist?").
/// </summary>
internal static class QuerySelectorEngine
{
    public static Element? First(Node root, string selector, JsRealm realm)
    {
        var p = Parse(selector, realm);
        foreach (var e in root.DescendantElements())
            if (p.Matches(e)) return e;
        return null;
    }

    public static IEnumerable<Element> All(Node root, string selector, JsRealm realm)
    {
        var p = Parse(selector, realm);
        foreach (var e in root.DescendantElements())
            if (p.Matches(e)) yield return e;
    }

    private static SimpleSelector Parse(string raw, JsRealm realm)
    {
        if (string.IsNullOrEmpty(raw))
            throw new JsThrow(realm.NewSyntaxError("Selector is empty"));
        var s = raw.Trim();
        if (s.Length == 0)
            throw new JsThrow(realm.NewSyntaxError("Selector is empty"));
        if (s[0] == '#')
        {
            var id = s[1..];
            ValidateIdentifier(id, raw, realm);
            return new SimpleSelector(SelectorKind.Id, id);
        }
        if (s[0] == '.')
        {
            var cls = s[1..];
            ValidateIdentifier(cls, raw, realm);
            return new SimpleSelector(SelectorKind.Class, cls);
        }
        if (s == "*") return new SimpleSelector(SelectorKind.Universal, "*");
        ValidateIdentifier(s, raw, realm);
        return new SimpleSelector(SelectorKind.Tag, s);
    }

    private static void ValidateIdentifier(string value, string raw, JsRealm realm)
    {
        if (string.IsNullOrEmpty(value))
            throw new JsThrow(realm.NewSyntaxError($"'{raw}' is not a supported selector (B5-1: only '#id', '.class', and bare tag names are supported)"));
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') continue;
            throw new JsThrow(realm.NewSyntaxError(
                $"'{raw}' is not a supported selector (B5-1: only '#id', '.class', and bare tag names are supported)"));
        }
    }

    private enum SelectorKind { Tag, Id, Class, Universal }

    private readonly record struct SimpleSelector(SelectorKind Kind, string Value)
    {
        public bool Matches(Element e) => Kind switch
        {
            SelectorKind.Tag => e.LocalName.Equals(Value, StringComparison.OrdinalIgnoreCase),
            SelectorKind.Id => e.Id == Value,
            SelectorKind.Class => e.ClassList.Contains(Value),
            SelectorKind.Universal => true,
            _ => false,
        };
    }
}
