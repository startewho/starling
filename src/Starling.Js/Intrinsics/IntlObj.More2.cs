using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-402 Intl.Segmenter (§18) and Intl.DisplayNames (§12) —
/// invariant-English locale data, .NET Globalization where it matches.</summary>
public static partial class IntlObj
{
    // =====================================================================
    //                          Intl.Segmenter
    // =====================================================================

    private sealed class IntlSegmenterObject(JsObject prototype, string granularity) : JsObject(prototype)
    {
        public string Granularity { get; } = granularity;
    }

    private static void InstallSegmenter(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "Segmenter", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.Segmenter requires 'new'"));
            }

            _ = ReadRequestedLocales(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var options = ReadOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var granularity = GetStringOption(realm, options, "granularity", "grapheme", "word", "sentence") ?? "grapheme";
            var instProto = IntlPrototypeFor(realm, newTarget, "Segmenter", proto);
            return JsValue.Object(new IntlSegmenterObject(instProto, granularity));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "Segmenter", "Intl.Segmenter");

        IntrinsicHelpers.DefineMethod(realm, proto, "segment", 1, (thisV, args) =>
        {
            var seg = thisV.IsObject && thisV.AsObject is IntlSegmenterObject so
                ? so
                : throw new JsThrow(realm.NewTypeError("Intl.Segmenter.prototype.segment called on incompatible receiver"));
            var input = AbstractOperations.ToStringJs(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Object(MakeSegments(realm, seg.Granularity, input));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0, (thisV, _) =>
        {
            var seg = thisV.IsObject && thisV.AsObject is IntlSegmenterObject so
                ? so
                : throw new JsThrow(realm.NewTypeError("Intl.Segmenter.prototype.resolvedOptions called on incompatible receiver"));
            var o = realm.NewOrdinaryObject();
            o.Set("locale", JsValue.String("en"));
            o.Set("granularity", JsValue.String(seg.Granularity));
            return JsValue.Object(o);
        });
    }

    private readonly record struct SegmentSpan(int Start, int End, bool IsWordLike);

    private static List<SegmentSpan> Segment(string granularity, string input)
    {
        var spans = new List<SegmentSpan>();
        if (input.Length == 0)
        {
            return spans;
        }

        switch (granularity)
        {
            case "word":
            {
                var i = 0;
                while (i < input.Length)
                {
                    var start = i;
                    var wordLike = char.IsLetterOrDigit(input[i]);
                    while (i < input.Length && char.IsLetterOrDigit(input[i]) == wordLike
                           && (wordLike || !char.IsLetterOrDigit(input[i])))
                    {
                        if (!wordLike && i > start && !char.IsWhiteSpace(input[i]) != !char.IsWhiteSpace(input[start]))
                        {
                            break;
                        }

                        i++;
                        if (wordLike && i < input.Length && !char.IsLetterOrDigit(input[i]))
                        {
                            break;
                        }
                    }

                    if (i == start)
                    {
                        i++;
                    }

                    spans.Add(new SegmentSpan(start, i, wordLike));
                }

                break;
            }
            case "sentence":
            {
                var start = 0;
                for (var i = 0; i < input.Length; i++)
                {
                    if (input[i] is '.' or '!' or '?')
                    {
                        var end = i + 1;
                        while (end < input.Length && char.IsWhiteSpace(input[end]))
                        {
                            end++;
                        }

                        spans.Add(new SegmentSpan(start, end, false));
                        start = end;
                        i = end - 1;
                    }
                }

                if (start < input.Length)
                {
                    spans.Add(new SegmentSpan(start, input.Length, false));
                }

                break;
            }
            default: // grapheme
            {
                var e = StringInfo.GetTextElementEnumerator(input);
                var prev = 0;
                while (e.MoveNext())
                {
                    var idx = e.ElementIndex;
                    if (idx > prev)
                    {
                        spans.Add(new SegmentSpan(prev, idx, false));
                    }

                    prev = idx;
                }

                spans.Add(new SegmentSpan(prev, input.Length, false));
                break;
            }
        }

        return spans;
    }

    private static JsValue MakeSegmentData(JsRealm realm, string granularity, string input, SegmentSpan s)
    {
        var d = realm.NewOrdinaryObject();
        d.Set("segment", JsValue.String(input[s.Start..s.End]));
        d.Set("index", JsValue.Number(s.Start));
        d.Set("input", JsValue.String(input));
        if (granularity == "word")
        {
            d.Set("isWordLike", JsValue.Boolean(s.IsWordLike));
        }

        return JsValue.Object(d);
    }

    private static JsObject MakeSegments(JsRealm realm, string granularity, string input)
    {
        var segments = realm.NewOrdinaryObject();
        IntrinsicHelpers.DefineMethod(realm, segments, "containing", 1, (_, cargs) =>
        {
            var n = JsValue.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm,
                cargs.Length > 0 ? cargs[0] : JsValue.Undefined, "number"));
            var idx = double.IsNaN(n) ? 0 : (int)Math.Truncate(n);
            if (idx < 0 || idx >= input.Length)
            {
                return JsValue.Undefined;
            }

            foreach (var s in Segment(granularity, input))
            {
                if (idx >= s.Start && idx < s.End)
                {
                    return MakeSegmentData(realm, granularity, input, s);
                }
            }

            return JsValue.Undefined;
        });
        var iterFn = new JsNativeFunction(realm, "[Symbol.iterator]", 0, (_, _) =>
        {
            var spans = Segment(granularity, input);
            var i = 0;
            var iter = realm.NewOrdinaryObject();
            IntrinsicHelpers.DefineMethod(realm, iter, "next", 0, (_, _) =>
            {
                var res = realm.NewOrdinaryObject();
                if (i < spans.Count)
                {
                    res.Set("value", MakeSegmentData(realm, granularity, input, spans[i]));
                    res.Set("done", JsValue.False);
                    i++;
                }
                else
                {
                    res.Set("value", JsValue.Undefined);
                    res.Set("done", JsValue.True);
                }

                return JsValue.Object(res);
            });
            return JsValue.Object(iter);
        }, isConstructor: false);
        segments.DefineOwnProperty(SymbolCtor.Iterator,
            PropertyDescriptor.Data(JsValue.Object(iterFn), writable: true, enumerable: false, configurable: true));
        return segments;
    }

    // =====================================================================
    //                          Intl.DisplayNames
    // =====================================================================

    private sealed class IntlDisplayNamesObject(JsObject prototype, string type, string style, string fallback) : JsObject(prototype)
    {
        public string Type { get; } = type;
        public string Style { get; } = style;
        public string Fallback { get; } = fallback;
    }

    private static void InstallDisplayNames(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "DisplayNames", 2, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.DisplayNames requires 'new'"));
            }

            _ = ReadRequestedLocales(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            // §12.1.1 step 3 — options is REQUIRED (GetOptionsObject, then a
            // required `type`).
            if (args.Length < 2 || args[1].IsUndefined)
            {
                throw new JsThrow(realm.NewTypeError("Intl.DisplayNames requires an options argument with a 'type'"));
            }

            var options = ReadOptionsObject(realm, args[1]);
            var style = GetStringOption(realm, options, "style", "narrow", "short", "long") ?? "long";
            var type = GetStringOption(realm, options, "type",
                "language", "region", "script", "currency", "calendar", "dateTimeField")
                ?? throw new JsThrow(realm.NewTypeError("Intl.DisplayNames options must include a 'type'"));
            var fallback = GetStringOption(realm, options, "fallback", "code", "none") ?? "code";
            var instProto = IntlPrototypeFor(realm, newTarget, "DisplayNames", proto);
            return JsValue.Object(new IntlDisplayNamesObject(instProto, type, style, fallback));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "DisplayNames", "Intl.DisplayNames");

        IntrinsicHelpers.DefineMethod(realm, proto, "of", 1, (thisV, args) =>
        {
            var dn = thisV.IsObject && thisV.AsObject is IntlDisplayNamesObject d
                ? d
                : throw new JsThrow(realm.NewTypeError("Intl.DisplayNames.prototype.of called on incompatible receiver"));
            var code = AbstractOperations.ToStringJs(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var name = DisplayNameOf(realm, dn.Type, code);
            if (name is not null)
            {
                return JsValue.String(name);
            }

            return dn.Fallback == "code" ? JsValue.String(code) : JsValue.Undefined;
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0, (thisV, _) =>
        {
            var dn = thisV.IsObject && thisV.AsObject is IntlDisplayNamesObject d
                ? d
                : throw new JsThrow(realm.NewTypeError("Intl.DisplayNames.prototype.resolvedOptions called on incompatible receiver"));
            var o = realm.NewOrdinaryObject();
            o.Set("locale", JsValue.String("en"));
            o.Set("style", JsValue.String(dn.Style));
            o.Set("type", JsValue.String(dn.Type));
            o.Set("fallback", JsValue.String(dn.Fallback));
            return JsValue.Object(o);
        });
    }

    private static string? DisplayNameOf(JsRealm realm, string type, string code)
    {
        switch (type)
        {
            case "language":
                if (code.Length is < 2 or > 11 || code.Contains('_', StringComparison.Ordinal))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid language code: '{code}'"));
                }

                try
                {
                    var name = CultureInfo.GetCultureInfo(code).EnglishName;
                    return name.StartsWith("Unknown", StringComparison.Ordinal) ? null : name;
                }
                catch (CultureNotFoundException)
                {
                    return null;
                }

            case "region":
                if (code.Length != 2 && !(code.Length == 3 && code.All(char.IsAsciiDigit)))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid region code: '{code}'"));
                }

                try
                {
                    return new RegionInfo(code).EnglishName;
                }
                catch (ArgumentException)
                {
                    return null;
                }

            case "script":
                if (code.Length != 4 || !char.IsAsciiLetterUpper(code[0]))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid script code: '{code}'"));
                }

                return code switch
                {
                    "Latn" => "Latin",
                    "Cyrl" => "Cyrillic",
                    "Arab" => "Arabic",
                    "Hans" => "Simplified Han",
                    "Hant" => "Traditional Han",
                    "Grek" => "Greek",
                    "Hebr" => "Hebrew",
                    "Kana" => "Katakana",
                    "Hira" => "Hiragana",
                    "Deva" => "Devanagari",
                    _ => null,
                };

            case "currency":
                if (code.Length != 3 || !code.All(char.IsAsciiLetter))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid currency code: '{code}'"));
                }

                return code.ToUpperInvariant() switch
                {
                    "USD" => "US Dollar",
                    "EUR" => "Euro",
                    "GBP" => "British Pound",
                    "JPY" => "Japanese Yen",
                    "CNY" => "Chinese Yuan",
                    "CHF" => "Swiss Franc",
                    "CAD" => "Canadian Dollar",
                    "AUD" => "Australian Dollar",
                    _ => null,
                };

            case "calendar":
                return code switch
                {
                    "gregory" => "Gregorian Calendar",
                    "iso8601" => "ISO-8601 Calendar",
                    "buddhist" => "Buddhist Calendar",
                    "chinese" => "Chinese Calendar",
                    _ => null,
                };

            case "dateTimeField":
                return code switch
                {
                    "era" => "era", "year" => "year", "quarter" => "quarter",
                    "month" => "month", "weekOfYear" => "week", "weekday" => "day of the week",
                    "day" => "day", "dayPeriod" => "AM/PM", "hour" => "hour",
                    "minute" => "minute", "second" => "second", "timeZoneName" => "time zone",
                    _ => throw new JsThrow(realm.NewRangeError($"Invalid dateTimeField: '{code}'")),
                };

            default:
                return null;
        }
    }

}
