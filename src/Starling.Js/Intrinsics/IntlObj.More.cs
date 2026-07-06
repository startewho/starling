using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-402 Intl.ListFormat (§13), Intl.PluralRules (§16), and
/// Intl.RelativeTimeFormat (§17) with carried en/es/pl locale data.</summary>
public static partial class IntlObj
{
    /// <summary>GetOptionsObject (§10.1.4): undefined becomes an absent bag,
    /// non-objects are a TypeError.</summary>
    private static JsObject? GetOptionsObject(JsRealm realm, JsValue options)
    {
        if (options.IsUndefined)
        {
            return null;
        }

        if (!options.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("options must be an object or undefined"));
        }

        return options.AsObject;
    }

    // =====================================================================
    //                          Intl.ListFormat
    // =====================================================================

    private readonly record struct ListPatterns(string Two, string Middle, string End);

    private sealed class IntlListFormatObject(JsObject prototype, string locale, string type, string style) : JsObject(prototype)
    {
        public string Locale { get; } = locale;
        public string Type { get; } = type;
        public string Style { get; } = style;
    }

    private static ListPatterns ListPatternsFor(string locale, string type, string style)
    {
        var lang = locale.Split('-')[0];
        if (lang == "es")
        {
            return type switch
            {
                "disjunction" => new ListPatterns(" o ", ", ", " o "),
                "unit" => style switch
                {
                    "narrow" => new ListPatterns(" ", " ", " "),
                    "short" => new ListPatterns(" y ", ", ", ", "),
                    _ => new ListPatterns(" y ", ", ", " y "),
                },
                _ => new ListPatterns(" y ", ", ", " y "),
            };
        }

        return type switch
        {
            "disjunction" => new ListPatterns(" or ", ", ", ", or "),
            "unit" => style switch
            {
                "narrow" => new ListPatterns(" ", " ", " "),
                _ => new ListPatterns(", ", ", ", ", "),
            },
            _ => style switch
            {
                "short" => new ListPatterns(" & ", ", ", ", & "),
                "narrow" => new ListPatterns(", ", ", ", ", "),
                _ => new ListPatterns(" and ", ", ", ", and "),
            },
        };
    }

    private static void InstallListFormat(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        var ctor = new JsNativeFunction(realm, "ListFormat", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.ListFormat requires 'new'"));
            }

            var locale = ResolveLocale(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var options = GetOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            _ = GetOptionEnum(realm, options, "localeMatcher", ["lookup", "best fit"], "best fit");
            var type = GetOptionEnum(realm, options, "type", ["conjunction", "disjunction", "unit"], "conjunction")!;
            var style = GetOptionEnum(realm, options, "style", ["long", "short", "narrow"], "long")!;
            var instProto = IntlPrototypeFor(realm, newTarget, "ListFormat", proto);
            return JsValue.Object(new IntlListFormatObject(instProto, StripExtensions(locale.Name), type, style));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "ListFormat", "Intl.ListFormat");

        IntrinsicHelpers.DefineMethod(realm, proto, "format", 1, (thisV, args) =>
        {
            var lf = RequireListFormat(realm, thisV);
            var items = StringListFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var patterns = ListPatternsFor(lf.Locale, lf.Type, lf.Style);
            var sb = new System.Text.StringBuilder(32);
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(SeparatorBefore(patterns, items.Count, i));
                }

                sb.Append(items[i]);
            }

            return JsValue.String(sb.ToString());
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "formatToParts", 1, (thisV, args) =>
        {
            var lf = RequireListFormat(realm, thisV);
            var items = StringListFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var patterns = ListPatternsFor(lf.Locale, lf.Type, lf.Style);
            var parts = new JsArray(realm);
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    parts.Push(MakePart(realm, "literal", SeparatorBefore(patterns, items.Count, i)));
                }

                parts.Push(MakePart(realm, "element", items[i]));
            }

            return JsValue.Object(parts);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0, (thisV, _) =>
        {
            var lf = RequireListFormat(realm, thisV);
            var o = realm.NewOrdinaryObject();
            o.Set("locale", JsValue.String(lf.Locale));
            o.Set("type", JsValue.String(lf.Type));
            o.Set("style", JsValue.String(lf.Style));
            return JsValue.Object(o);
        });
    }

    private static IntlListFormatObject RequireListFormat(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is IntlListFormatObject lf
            ? lf
            : throw new JsThrow(realm.NewTypeError("Intl.ListFormat method called on incompatible receiver"));

    private static string SeparatorBefore(ListPatterns patterns, int count, int i)
    {
        if (count == 2)
        {
            return patterns.Two;
        }

        return i == count - 1 ? patterns.End : patterns.Middle;
    }

    /// <summary>§13.5.3 StringListFromIterable — every element must already be
    /// a String; a non-string closes the iterator and throws a TypeError.</summary>
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
                AbstractOperations.IteratorClose(vm, record, isThrowing: true);
                throw new JsThrow(realm.NewTypeError("Iterable yielded a non-string value in Intl.ListFormat.format"));
            }

            result.Add(v.AsString);
        }

        return result;
    }

    // =====================================================================
    //                          Intl.PluralRules
    // =====================================================================

    private sealed class IntlPluralRulesObject(JsObject prototype, string locale, string type) : JsObject(prototype)
    {
        public string Locale { get; } = locale;
        public string Type { get; } = type;
    }

    private static void InstallPluralRules(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        var ctor = new JsNativeFunction(realm, "PluralRules", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.PluralRules requires 'new'"));
            }

            var locale = ResolveLocale(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var options = ReadOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            _ = GetOptionEnum(realm, options, "localeMatcher", ["lookup", "best fit"], "best fit");
            var type = GetOptionEnum(realm, options, "type", ["cardinal", "ordinal"], "cardinal")!;
            var instProto = IntlPrototypeFor(realm, newTarget, "PluralRules", proto);
            return JsValue.Object(new IntlPluralRulesObject(instProto, StripExtensions(locale.Name), type));
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
            o.Set("locale", JsValue.String(pr.Locale));
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

    /// <summary>Polish cardinal plural rules over integer values.</summary>
    private static string SelectPluralPl(double n)
    {
        var abs = Math.Abs(n);
        if (abs == 1)
        {
            return "one";
        }

        if (abs != Math.Floor(abs))
        {
            return "other";
        }

        var mod10 = abs % 10;
        var mod100 = abs % 100;
        if (mod10 is >= 2 and <= 4 && mod100 is not (>= 12 and <= 14))
        {
            return "few";
        }

        return "many";
    }

    // =====================================================================
    //                       Intl.RelativeTimeFormat
    // =====================================================================

    private sealed class IntlRelativeTimeFormatObject(
        JsObject prototype, string locale, System.Globalization.CultureInfo culture, string numberingSystem,
        string style, string numeric) : JsObject(prototype)
    {
        public string Locale { get; } = locale;
        public System.Globalization.CultureInfo Culture { get; } = culture;
        public string NumberingSystem { get; } = numberingSystem;
        public string Style { get; } = style;
        public string Numeric { get; } = numeric;
    }

    private static readonly string[] RtfUnits =
        ["year", "quarter", "month", "week", "day", "hour", "minute", "second"];

    // (one, few, many) unit nouns per style for pl; (one, other) for en.
    private static readonly Dictionary<string, (string One, string Few, string Many, string Other)[]> PlRtfUnits = new(StringComparer.Ordinal)
    {
        // year, quarter, month, week, day, hour, minute, second — long, short, narrow.
        ["long"] =
        [
            ("rok", "lata", "lat", "roku"), ("kwartał", "kwartały", "kwartałów", "kwartału"),
            ("miesiąc", "miesiące", "miesięcy", "miesiąca"), ("tydzień", "tygodnie", "tygodni", "tygodnia"),
            ("dzień", "dni", "dni", "dnia"), ("godzinę", "godziny", "godzin", "godziny"),
            ("minutę", "minuty", "minut", "minuty"), ("sekundę", "sekundy", "sekund", "sekundy"),
        ],
        ["short"] =
        [
            ("rok", "lata", "lat", "roku"), ("kw.", "kw.", "kw.", "kw."), ("mies.", "mies.", "mies.", "mies."),
            ("tydz.", "tyg.", "tyg.", "tyg."), ("dzień", "dni", "dni", "dnia"), ("godz.", "godz.", "godz.", "godz."),
            ("min", "min", "min", "min"), ("sek.", "sek.", "sek.", "sek."),
        ],
        ["narrow"] =
        [
            ("rok", "lata", "lat", "roku"), ("kw.", "kw.", "kw.", "kw."), ("mies.", "mies.", "mies.", "mies."),
            ("tydz.", "tyg.", "tyg.", "tyg."), ("dzień", "dni", "dni", "dnia"), ("g.", "g.", "g.", "g."),
            ("min", "min", "min", "min"), ("s", "s", "s", "s"),
        ],
    };

    private static readonly Dictionary<string, (string One, string Other)[]> EnRtfUnits = new(StringComparer.Ordinal)
    {
        ["long"] =
        [
            ("year", "years"), ("quarter", "quarters"), ("month", "months"), ("week", "weeks"),
            ("day", "days"), ("hour", "hours"), ("minute", "minutes"), ("second", "seconds"),
        ],
        ["short"] =
        [
            ("yr.", "yr."), ("qtr.", "qtrs."), ("mo.", "mo."), ("wk.", "wk."),
            ("day", "days"), ("hr.", "hr."), ("min.", "min."), ("sec.", "sec."),
        ],
        ["narrow"] =
        [
            ("yr.", "yr."), ("qtr.", "qtrs."), ("mo.", "mo."), ("wk.", "wk."),
            ("day", "days"), ("hr.", "hr."), ("min.", "min."), ("sec.", "sec."),
        ],
    };

    private static void InstallRelativeTimeFormat(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        var ctor = new JsNativeFunction(realm, "RelativeTimeFormat", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.RelativeTimeFormat requires 'new'"));
            }

            var requested = ReadRequestedLocales(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var options = ReadOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            _ = GetOptionEnum(realm, options, "localeMatcher", ["lookup", "best fit"], "best fit");
            var nuOption = GetOptionEnum(realm, options, "numberingSystem", null, null);
            if (nuOption is not null)
            {
                if (!IsWellFormedNumberingSystem(nuOption))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid numberingSystem: \"{nuOption}\""));
                }

                nuOption = nuOption.ToLowerInvariant();
            }

            var (localeName, culture, nu) = ResolveNumberLocale(requested, nuOption);
            var style = GetOptionEnum(realm, options, "style", ["long", "short", "narrow"], "long")!;
            var numeric = GetOptionEnum(realm, options, "numeric", ["always", "auto"], "always")!;
            var instProto = IntlPrototypeFor(realm, newTarget, "RelativeTimeFormat", proto);
            return JsValue.Object(new IntlRelativeTimeFormatObject(instProto, localeName, culture, nu, style, numeric));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "RelativeTimeFormat", "Intl.RelativeTimeFormat");

        IntrinsicHelpers.DefineMethod(realm, proto, "format", 2, (thisV, args) =>
        {
            var rtf = RequireRelativeTimeFormat(realm, thisV);
            var (value, unit) = RtfArgs(realm, args);
            var parts = FormatRelativeParts(rtf, value, unit);
            var sb = new System.Text.StringBuilder(24);
            for (var i = 0; i < parts.Count; i++)
            {
                sb.Append(parts[i].Part.Value);
            }

            return JsValue.String(sb.ToString());
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "formatToParts", 2, (thisV, args) =>
        {
            var rtf = RequireRelativeTimeFormat(realm, thisV);
            var (value, unit) = RtfArgs(realm, args);
            var parts = FormatRelativeParts(rtf, value, unit);
            var arr = new JsArray(realm);
            for (var i = 0; i < parts.Count; i++)
            {
                var part = MakePart(realm, parts[i].Part.Type, parts[i].Part.Value).AsObject;
                if (parts[i].Unit is not null)
                {
                    part.Set("unit", JsValue.String(parts[i].Unit!));
                }

                arr.Push(JsValue.Object(part));
            }

            return JsValue.Object(arr);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0, (thisV, _) =>
        {
            var rtf = RequireRelativeTimeFormat(realm, thisV);
            var o = realm.NewOrdinaryObject();
            o.Set("locale", JsValue.String(rtf.Locale));
            o.Set("style", JsValue.String(rtf.Style));
            o.Set("numeric", JsValue.String(rtf.Numeric));
            o.Set("numberingSystem", JsValue.String(rtf.NumberingSystem));
            return JsValue.Object(o);
        });
    }

    private static IntlRelativeTimeFormatObject RequireRelativeTimeFormat(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is IntlRelativeTimeFormatObject rtf
            ? rtf
            : throw new JsThrow(realm.NewTypeError("Intl.RelativeTimeFormat method called on incompatible receiver"));

    private static (double Value, string Unit) RtfArgs(JsRealm realm, JsValue[] args)
    {
        var raw = args.Length > 0 ? args[0] : JsValue.Undefined;
        var prim = AbstractOperations.ToPrimitive(realm.ActiveVm, raw, "number");
        if (prim.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a number"));
        }

        var value = JsValue.ToNumber(prim);
        var unitValue = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (unitValue.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a string"));
        }

        var unitRaw = AbstractOperations.ToStringJs(realm.ActiveVm, unitValue);
        // §17.5.1 SingularRelativeTimeUnit — plural spellings are accepted.
        var unit = unitRaw.EndsWith('s') ? unitRaw[..^1] : unitRaw;
        if (Array.IndexOf(RtfUnits, unit) < 0)
        {
            throw new JsThrow(realm.NewRangeError($"Invalid unit argument for Intl.RelativeTimeFormat: '{unitRaw}'"));
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new JsThrow(realm.NewRangeError("Value out of range for Intl.RelativeTimeFormat"));
        }

        return (value, unit);
    }

    private static List<(NumPart Part, string? Unit)> FormatRelativeParts(IntlRelativeTimeFormatObject rtf, double value, string unit)
    {
        var result = new List<(NumPart, string?)>(6);
        var isPl = rtf.Locale.StartsWith("pl", StringComparison.Ordinal);
        var past = value < 0 || (value == 0 && double.IsNegative(value));

        if (rtf.Numeric == "auto" && !isPl && value == Math.Floor(value) && Math.Abs(value) <= 1)
        {
            // -0 selects the "0" entry like +0.
            var key = double.IsNegative(value) && value == 0 ? 0 : (int)value;
            var special = (unit, key) switch
            {
                ("day", -1) => "yesterday",
                ("day", 0) => "today",
                ("day", 1) => "tomorrow",
                ("second", 0) => "now",
                ("hour", 0) => "this hour",
                ("minute", 0) => "this minute",
                ("year" or "quarter" or "month" or "week", -1) => "last " + unit,
                ("year" or "quarter" or "month" or "week", 0) => "this " + unit,
                ("year" or "quarter" or "month" or "week", 1) => "next " + unit,
                _ => null,
            };
            if (special is not null)
            {
                result.Add((new NumPart("literal", special), null));
                return result;
            }
        }

        var abs = Math.Abs(value);
        var unitIndex = Array.IndexOf(RtfUnits, unit);
        string noun;
        if (isPl)
        {
            var forms = PlRtfUnits[rtf.Style][unitIndex];
            noun = SelectPluralPl(abs) switch
            {
                "one" => forms.One,
                "few" => forms.Few,
                "many" => forms.Many,
                _ => forms.Other,
            };
        }
        else
        {
            var forms = EnRtfUnits[rtf.Style][unitIndex];
            noun = abs == 1 ? forms.One : forms.Other;
        }

        var prefix = past ? (isPl ? "" : "") : (isPl ? "za " : "in ");
        var suffix = past ? (isPl ? " " + noun + " temu" : " " + noun + " ago") : " " + noun;
        if (prefix.Length > 0)
        {
            result.Add((new NumPart("literal", prefix), null));
        }

        var numberState = RtfNumberState(rtf);
        var numberParts = PartitionNumberPattern(numberState, DecimalNum.FromDouble(abs));
        for (var i = 0; i < numberParts.Count; i++)
        {
            result.Add((numberParts[i], unit));
        }

        result.Add((new NumPart("literal", suffix), null));
        return result;
    }

    private static NumberFormatState RtfNumberState(IntlRelativeTimeFormatObject rtf)
    {
        // pl (like many locales) sets minimumGroupingDigits=2, so 1000 has no
        // group separator while 10000 does.
        var grouping = rtf.Locale.StartsWith("pl", StringComparison.Ordinal) ? "min2" : "auto";
        return new NumberFormatState(
            rtf.Locale, rtf.Culture, rtf.NumberingSystem, "decimal", null, "symbol", "standard", null, "short",
            1, 0, 3, 0, 0, "fractionDigits", "standard", "short", grouping, "auto", 1, "halfExpand", "auto");
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
