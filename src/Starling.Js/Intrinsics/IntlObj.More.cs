using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-402 constructors beyond the v1 core: Intl.ListFormat (§13),
/// Intl.PluralRules (§16), Intl.RelativeTimeFormat (§17). Locale data is the
/// invariant English set — enough for API-surface and en-semantics
/// conformance; other locales resolve to "en".</summary>
public static partial class IntlObj
{
    // =====================================================================
    //                          Intl.ListFormat
    // =====================================================================

    private sealed class IntlListFormatObject(JsObject prototype, string type, string style) : JsObject(prototype)
    {
        public string Type { get; } = type;
        public string Style { get; } = style;
    }

    private static void InstallListFormat(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "ListFormat", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.ListFormat requires 'new'"));
            }

            _ = ReadRequestedLocales(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var options = ReadOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var type = GetStringOption(realm, options, "type", "conjunction", "disjunction", "unit") ?? "conjunction";
            var style = GetStringOption(realm, options, "style", "long", "short", "narrow") ?? "long";
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new IntlListFormatObject(instProto, type, style));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "ListFormat", "Intl.ListFormat");

        IntrinsicHelpers.DefineMethod(realm, proto, "format", 1, (thisV, args) =>
        {
            var lf = RequireListFormat(realm, thisV);
            var items = StringListFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            return JsValue.String(FormatList(lf, items));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "formatToParts", 1, (thisV, args) =>
        {
            var lf = RequireListFormat(realm, thisV);
            var items = StringListFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var parts = new JsArray(realm);
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    parts.Push(MakePart(realm, "literal", SeparatorBefore(lf, items.Count, i)));
                }

                parts.Push(MakePart(realm, "element", items[i]));
            }

            return JsValue.Object(parts);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0, (thisV, _) =>
        {
            var lf = RequireListFormat(realm, thisV);
            var o = realm.NewOrdinaryObject();
            o.Set("locale", JsValue.String("en"));
            o.Set("type", JsValue.String(lf.Type));
            o.Set("style", JsValue.String(lf.Style));
            return JsValue.Object(o);
        });
    }

    private static IntlListFormatObject RequireListFormat(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is IntlListFormatObject lf
            ? lf
            : throw new JsThrow(realm.NewTypeError("Intl.ListFormat method called on incompatible receiver"));

    private static string SeparatorBefore(IntlListFormatObject lf, int count, int i)
    {
        // en CLDR: two items join with " and "/" or "; longer lists use
        // ", " with ", and "/", or " before the final element. Unit lists
        // use plain commas.
        var conj = lf.Type switch
        {
            "disjunction" => "or",
            "unit" => null,
            _ => "and",
        };
        if (conj is null)
        {
            return ", ";
        }

        if (i == count - 1)
        {
            return count == 2 ? " " + conj + " " : ", " + conj + " ";
        }

        return ", ";
    }

    private static string FormatList(IntlListFormatObject lf, List<string> items)
    {
        if (items.Count == 0)
        {
            return "";
        }

        var sb = new System.Text.StringBuilder(items[0]);
        for (var i = 1; i < items.Count; i++)
        {
            sb.Append(SeparatorBefore(lf, items.Count, i)).Append(items[i]);
        }

        return sb.ToString();
    }

    /// <summary>§13.5.3 StringListFromIterable — every element must already be
    /// a String (no coercion; a non-string is a TypeError).</summary>
    private static List<string> StringListFrom(JsRealm realm, JsValue iterable)
    {
        var result = new List<string>();
        if (iterable.IsUndefined)
        {
            return result;
        }

        var vm = realm.ActiveVm;
        var record = AbstractOperations.GetIterator(realm, vm, iterable);
        while (true)
        {
            var step = AbstractOperations.IteratorNext(realm, vm, record);
            if (AbstractOperations.IteratorComplete(vm, step))
            {
                break;
            }

            var v = AbstractOperations.IteratorValue(vm, step);
            if (!v.IsString)
            {
                throw new JsThrow(realm.NewTypeError("Iterable yielded a non-string value in Intl.ListFormat.format"));
            }

            result.Add(v.AsString);
        }

        return result;
    }

    // =====================================================================
    //                          Intl.PluralRules
    // =====================================================================

    private sealed class IntlPluralRulesObject(JsObject prototype, string type) : JsObject(prototype)
    {
        public string Type { get; } = type;
    }

    private static void InstallPluralRules(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "PluralRules", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.PluralRules requires 'new'"));
            }

            _ = ReadRequestedLocales(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var options = ReadOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var type = GetStringOption(realm, options, "type", "cardinal", "ordinal") ?? "cardinal";
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new IntlPluralRulesObject(instProto, type));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "PluralRules", "Intl.PluralRules");

        IntrinsicHelpers.DefineMethod(realm, proto, "select", 1, (thisV, args) =>
        {
            var pr = RequirePluralRules(realm, thisV);
            var n = JsValue.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm,
                args.Length > 0 ? args[0] : JsValue.Undefined, "number"));
            return JsValue.String(SelectPlural(pr.Type, n));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "selectRange", 2, (thisV, args) =>
        {
            var pr = RequirePluralRules(realm, thisV);
            var a = args.Length > 0 ? args[0] : JsValue.Undefined;
            var b = args.Length > 1 ? args[1] : JsValue.Undefined;
            if (a.IsUndefined || b.IsUndefined)
            {
                throw new JsThrow(realm.NewTypeError("Intl.PluralRules.prototype.selectRange requires two arguments"));
            }

            var end = JsValue.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm, b, "number"));
            if (double.IsNaN(end))
            {
                throw new JsThrow(realm.NewRangeError("Invalid selectRange bound"));
            }

            // en: the range category follows the END of the range.
            return JsValue.String(SelectPlural(pr.Type, end));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0, (thisV, _) =>
        {
            var pr = RequirePluralRules(realm, thisV);
            var o = realm.NewOrdinaryObject();
            o.Set("locale", JsValue.String("en"));
            o.Set("type", JsValue.String(pr.Type));
            var cats = new JsArray(realm);
            if (pr.Type == "ordinal")
            {
                foreach (var c in new[] { "few", "one", "other", "two" })
                {
                    cats.Push(JsValue.String(c));
                }
            }
            else
            {
                cats.Push(JsValue.String("one"));
                cats.Push(JsValue.String("other"));
            }

            o.Set("pluralCategories", JsValue.Object(cats));
            return JsValue.Object(o);
        });
    }

    private static IntlPluralRulesObject RequirePluralRules(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is IntlPluralRulesObject pr
            ? pr
            : throw new JsThrow(realm.NewTypeError("Intl.PluralRules method called on incompatible receiver"));

    private static string SelectPlural(string type, double n)
    {
        if (double.IsNaN(n))
        {
            return "other";
        }

        if (type == "ordinal")
        {
            var abs = Math.Abs(n);
            var mod100 = abs % 100;
            if (mod100 is >= 11 and <= 13)
            {
                return "other";
            }

            return (abs % 10) switch
            {
                1 => "one",
                2 => "two",
                3 => "few",
                _ => "other",
            };
        }

        // en cardinal: exactly 1 (integer) is "one"; everything else "other".
        return n == 1 ? "one" : "other";
    }

    // =====================================================================
    //                       Intl.RelativeTimeFormat
    // =====================================================================

    private sealed class IntlRelativeTimeFormatObject(JsObject prototype, string numeric, string style) : JsObject(prototype)
    {
        public string Numeric { get; } = numeric;
        public string Style { get; } = style;
    }

    private static readonly string[] RtfUnits =
        ["year", "quarter", "month", "week", "day", "hour", "minute", "second"];

    private static void InstallRelativeTimeFormat(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "RelativeTimeFormat", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.RelativeTimeFormat requires 'new'"));
            }

            _ = ReadRequestedLocales(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var options = ReadOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var numeric = GetStringOption(realm, options, "numeric", "always", "auto") ?? "always";
            var style = GetStringOption(realm, options, "style", "long", "short", "narrow") ?? "long";
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new IntlRelativeTimeFormatObject(instProto, numeric, style));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "RelativeTimeFormat", "Intl.RelativeTimeFormat");

        IntrinsicHelpers.DefineMethod(realm, proto, "format", 2, (thisV, args) =>
        {
            var rtf = RequireRelativeTimeFormat(realm, thisV);
            var (value, unit) = RtfArgs(realm, args);
            return JsValue.String(FormatRelative(rtf, value, unit));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "formatToParts", 2, (thisV, args) =>
        {
            var rtf = RequireRelativeTimeFormat(realm, thisV);
            var (value, unit) = RtfArgs(realm, args);
            var text = FormatRelative(rtf, value, unit);
            var parts = new JsArray(realm);
            // Split the numeric run out as an "integer" part with its unit.
            var numStr = Math.Abs(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var idx = text.IndexOf(numStr, StringComparison.Ordinal);
            if (idx < 0)
            {
                parts.Push(MakePart(realm, "literal", text));
            }
            else
            {
                if (idx > 0)
                {
                    parts.Push(MakePart(realm, "literal", text[..idx]));
                }

                var numPart = MakePart(realm, "integer", numStr).AsObject;
                numPart.Set("unit", JsValue.String(unit));
                parts.Push(JsValue.Object(numPart));
                if (idx + numStr.Length < text.Length)
                {
                    parts.Push(MakePart(realm, "literal", text[(idx + numStr.Length)..]));
                }
            }

            return JsValue.Object(parts);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0, (thisV, _) =>
        {
            var rtf = RequireRelativeTimeFormat(realm, thisV);
            var o = realm.NewOrdinaryObject();
            o.Set("locale", JsValue.String("en"));
            o.Set("style", JsValue.String(rtf.Style));
            o.Set("numeric", JsValue.String(rtf.Numeric));
            o.Set("numberingSystem", JsValue.String("latn"));
            return JsValue.Object(o);
        });
    }

    private static IntlRelativeTimeFormatObject RequireRelativeTimeFormat(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is IntlRelativeTimeFormatObject rtf
            ? rtf
            : throw new JsThrow(realm.NewTypeError("Intl.RelativeTimeFormat method called on incompatible receiver"));

    private static (double Value, string Unit) RtfArgs(JsRealm realm, JsValue[] args)
    {
        var value = JsValue.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm,
            args.Length > 0 ? args[0] : JsValue.Undefined, "number"));
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new JsThrow(realm.NewRangeError("Value out of range for Intl.RelativeTimeFormat"));
        }

        var unitRaw = AbstractOperations.ToStringJs(realm.ActiveVm,
            args.Length > 1 ? args[1] : JsValue.Undefined);
        // §17.5.1 SingularRelativeTimeUnit — plural spellings are accepted.
        var unit = unitRaw.EndsWith('s') ? unitRaw[..^1] : unitRaw;
        if (Array.IndexOf(RtfUnits, unit) < 0)
        {
            throw new JsThrow(realm.NewRangeError($"Invalid unit argument for Intl.RelativeTimeFormat: '{unitRaw}'"));
        }

        return (value, unit);
    }

    private static string FormatRelative(IntlRelativeTimeFormatObject rtf, double value, string unit)
    {
        if (rtf.Numeric == "auto")
        {
            var special = (unit, value) switch
            {
                ("day", -1) => "yesterday",
                ("day", 0) => "today",
                ("day", 1) => "tomorrow",
                ("second", 0) => "now",
                (_, -1) => "last " + unit,
                (_, 0) => "this " + unit,
                (_, 1) => "next " + unit,
                _ => null,
            };
            if (special is not null)
            {
                return special;
            }
        }

        var abs = Math.Abs(value);
        var absStr = abs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var unitWord = abs == 1 ? unit : unit + "s";
        return value < 0 || (value == 0 && double.IsNegative(value))
            ? absStr + " " + unitWord + " ago"
            : "in " + absStr + " " + unitWord;
    }

    // =====================================================================
    //                              Shared
    // =====================================================================

    private static void WireIntlCtor(JsRealm realm, JsObject intl, JsNativeFunction ctor, JsObject proto, string name, string tag)
    {
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        IntrinsicHelpers.DefineMethod(realm, ctor, "supportedLocalesOf", 1,
            (_, args) => SupportedLocalesOf(realm, args));
        DefineData(proto, "constructor", JsValue.Object(ctor));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String(tag), writable: false, enumerable: false, configurable: true));
        DefineData(intl, name, JsValue.Object(ctor));
    }

    private static JsObject? ReadOptionsObject(JsRealm realm, JsValue options)
    {
        if (options.IsUndefined)
        {
            return null;
        }

        return AbstractOperations.ToObject(realm, options);
    }

    private static JsValue MakePart(JsRealm realm, string type, string value)
    {
        var part = realm.NewOrdinaryObject();
        part.Set("type", JsValue.String(type));
        part.Set("value", JsValue.String(value));
        return JsValue.Object(part);
    }
}
