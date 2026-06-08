using Starling.RegExp;
using Starling.Js.Runtime.Regex;

namespace Starling.Js.Runtime;

/// <summary>
/// ES2024 §22.2.7 RegExp instance — an exotic object holding a compiled regex
/// plus the mutable <c>lastIndex</c> slot. Inherits from
/// <see cref="JsRealm.RegExpPrototype"/>.
/// </summary>
public sealed class JsRegExp : JsObject
{
    public IRegexMatcher Compiled { get; }
    public string Source => EscapeSource(Compiled.Source);
    public RegexFlags Flags => Compiled.Flags;

    public JsRegExp(JsRealm realm, IRegexMatcher compiled) : base(realm.RegExpPrototype)
    {
        Compiled = compiled;
        // lastIndex is writable, non-enumerable, non-configurable per §22.2.7.1.
        DefineOwnProperty("lastIndex",
            PropertyDescriptor.Data(JsValue.Number(0), writable: true, enumerable: false, configurable: false));
    }

    public double LastIndex
    {
        get
        {
            var v = Get("lastIndex");
            return v.IsNumber ? v.AsNumber : JsValue.ToNumber(v);
        }
        set => Set("lastIndex", JsValue.Number(value));
    }

    public override string ToString() => $"/{Source}/{RegexFlagParser.ToFlagString(Flags)}";

    private static string EscapeSource(string source)
    {
        if (source.Length == 0) return "(?:)";

        var sb = new System.Text.StringBuilder(source.Length);
        var inClass = false;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '\\')
            {
                sb.Append(c);
                if (i + 1 < source.Length) sb.Append(source[++i]);
                continue;
            }

            if (c == '[') inClass = true;
            else if (c == ']') inClass = false;

            switch (c)
            {
                case '/' when !inClass:
                    sb.Append("\\/");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\u2028':
                    sb.Append("\\u2028");
                    break;
                case '\u2029':
                    sb.Append("\\u2029");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
