using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-402 Intl.Segmenter (§18) and Intl.DisplayNames (§12) —
/// invariant-English locale data, .NET Globalization where it matches.</summary>
public static partial class IntlObj
{
    // =====================================================================
    //                          Intl.Segmenter
    // =====================================================================

    private sealed class IntlSegmenterObject(JsObject prototype, string locale, string granularity) : JsObject(prototype)
    {
        public string Locale { get; } = locale;
        public string Granularity { get; } = granularity;
    }

    private sealed class IntlSegmentsObject(JsObject prototype, string granularity, string input) : JsObject(prototype)
    {
        public string Granularity { get; } = granularity;
        public string Input { get; } = input;
    }

    private static void InstallSegmenter(JsRealm realm, JsObject intl)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        var ctor = new JsNativeFunction(realm, "Segmenter", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Intl.Segmenter requires 'new'"));
            }

            var locale = ResolveLocale(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var options = GetOptionsObject(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            _ = GetOptionEnum(realm, options, "localeMatcher", ["lookup", "best fit"], "best fit");
            var granularity = GetOptionEnum(realm, options, "granularity", ["grapheme", "word", "sentence"], "grapheme")!;
            var instProto = IntlPrototypeFor(realm, newTarget, "Segmenter", proto);
            return JsValue.Object(new IntlSegmenterObject(instProto, StripExtensions(locale.Name), granularity));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "Segmenter", "Intl.Segmenter");

        var segmentsProto = MakeSegmentsPrototype(realm);
        IntrinsicHelpers.DefineMethod(realm, proto, "segment", 1, (thisV, args) =>
        {
            var seg = thisV.IsObject && thisV.AsObject is IntlSegmenterObject so
                ? so
                : throw new JsThrow(realm.NewTypeError("Intl.Segmenter.prototype.segment called on incompatible receiver"));
            var arg = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (arg.IsSymbol)
            {
                throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a string"));
            }

            var input = AbstractOperations.ToStringJs(realm.ActiveVm, arg);
            return JsValue.Object(new IntlSegmentsObject(segmentsProto, seg.Granularity, input));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0, (thisV, _) =>
        {
            var seg = thisV.IsObject && thisV.AsObject is IntlSegmenterObject so
                ? so
                : throw new JsThrow(realm.NewTypeError("Intl.Segmenter.prototype.resolvedOptions called on incompatible receiver"));
            var o = realm.NewOrdinaryObject();
            o.Set("locale", JsValue.String(seg.Locale));
            o.Set("granularity", JsValue.String(seg.Granularity));
            return JsValue.Object(o);
        });
    }

    private static IntlSegmentsObject RequireSegments(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is IntlSegmentsObject so
            ? so
            : throw new JsThrow(realm.NewTypeError("%Segments.prototype% method called on incompatible receiver"));

    private static JsObject MakeSegmentsPrototype(JsRealm realm)
    {
        var segmentsProto = new JsObject(realm.ObjectPrototype);
        IntrinsicHelpers.DefineMethod(realm, segmentsProto, "containing", 1, (thisV, cargs) =>
        {
            var segments = RequireSegments(realm, thisV);
            var arg = cargs.Length > 0 ? cargs[0] : JsValue.Undefined;
            var prim = AbstractOperations.ToPrimitive(realm.ActiveVm, arg, "number");
            if (prim.IsSymbol || prim.IsBigInt)
            {
                throw new JsThrow(realm.NewTypeError("Cannot convert this value to a number"));
            }

            var n = JsValue.ToNumber(prim);
            var idx = double.IsNaN(n) ? 0 : (int)Math.Truncate(n);
            var input = segments.Input;
            if (idx < 0 || idx >= input.Length)
            {
                return JsValue.Undefined;
            }

            foreach (var s in Segment(segments.Granularity, input))
            {
                if (idx >= s.Start && idx < s.End)
                {
                    return MakeSegmentData(realm, segments.Granularity, input, s);
                }
            }

            return JsValue.Undefined;
        });
        var iterFn = new JsNativeFunction(realm, "[Symbol.iterator]", 0, (thisV, _) =>
        {
            var segments = RequireSegments(realm, thisV);
            var spans = Segment(segments.Granularity, segments.Input);
            var i = 0;
            var iter = realm.NewOrdinaryObject();
            IntrinsicHelpers.DefineMethod(realm, iter, "next", 0, (_, _) =>
            {
                var res = realm.NewOrdinaryObject();
                if (i < spans.Count)
                {
                    res.Set("value", MakeSegmentData(realm, segments.Granularity, segments.Input, spans[i]));
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
        segmentsProto.DefineOwnProperty(SymbolCtor.Iterator,
            PropertyDescriptor.Data(JsValue.Object(iterFn), writable: true, enumerable: false, configurable: true));
        segmentsProto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl.Segments"), writable: false, enumerable: false, configurable: true));
        return segmentsProto;
    }

    private readonly record struct SegmentSpan(int Start, int End, bool IsWordLike);

    /// <summary>Grapheme-cluster boundaries via UAX #29 text elements; lone
    /// surrogates fall out as single-unit clusters.</summary>
    private static List<int> GraphemeStarts(string input)
    {
        var starts = new List<int>(input.Length);
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(input);
        while (e.MoveNext())
        {
            starts.Add(e.ElementIndex);
        }

        if (starts.Count == 0 && input.Length > 0)
        {
            starts.Add(0);
        }

        return starts;
    }

    private static List<SegmentSpan> Segment(string granularity, string input)
    {
        var spans = new List<SegmentSpan>();
        if (input.Length == 0)
        {
            return spans;
        }

        var starts = GraphemeStarts(input);

        static bool IsWordLikeAt(string text, int index)
        {
            var c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                return System.Text.Rune.IsLetterOrDigit(new System.Text.Rune(c, text[index + 1]));
            }

            return !char.IsSurrogate(c) && char.IsLetterOrDigit(c);
        }

        static bool IsWhiteSpaceAt(string text, int index)
        {
            var c = text[index];
            return !char.IsSurrogate(c) && char.IsWhiteSpace(c);
        }

        switch (granularity)
        {
            case "word":
            {
                var i = 0;
                while (i < starts.Count)
                {
                    var startUnit = starts[i];
                    if (IsWordLikeAt(input, startUnit))
                    {
                        var j = i + 1;
                        while (j < starts.Count)
                        {
                            if (IsWordLikeAt(input, starts[j]))
                            {
                                j++;
                                continue;
                            }

                            // UAX #29 MidNum: "." or "," between digits stays
                            // inside the word ("1.23").
                            var chEnd = j + 1 < starts.Count ? starts[j + 1] : input.Length;
                            var cluster = input[starts[j]..chEnd];
                            if ((cluster == "." || cluster == ",")
                                && j + 1 < starts.Count
                                && char.IsAsciiDigit(input[starts[j] - 1])
                                && char.IsAsciiDigit(input[starts[j + 1]]))
                            {
                                j += 2;
                                continue;
                            }

                            break;
                        }

                        var endUnit = j < starts.Count ? starts[j] : input.Length;
                        spans.Add(new SegmentSpan(startUnit, endUnit, true));
                        i = j;
                    }
                    else if (IsWhiteSpaceAt(input, startUnit))
                    {
                        var j = i + 1;
                        while (j < starts.Count && IsWhiteSpaceAt(input, starts[j]))
                        {
                            j++;
                        }

                        var endUnit = j < starts.Count ? starts[j] : input.Length;
                        spans.Add(new SegmentSpan(startUnit, endUnit, false));
                        i = j;
                    }
                    else
                    {
                        var endUnit = i + 1 < starts.Count ? starts[i + 1] : input.Length;
                        spans.Add(new SegmentSpan(startUnit, endUnit, false));
                        i++;
                    }
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
                for (var i = 0; i < starts.Count; i++)
                {
                    var end = i + 1 < starts.Count ? starts[i + 1] : input.Length;
                    spans.Add(new SegmentSpan(starts[i], end, false));
                }

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

    // =====================================================================
    //                          Intl.DisplayNames
    // =====================================================================

    private sealed class IntlDisplayNamesObject(
        JsObject prototype, string locale, string type, string style, string fallback, string? languageDisplay) : JsObject(prototype)
    {
        public string Locale { get; } = locale;
        public string Type { get; } = type;
        public string Style { get; } = style;
        public string Fallback { get; } = fallback;
        public string? LanguageDisplay { get; } = languageDisplay;
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

            // §12.1.1 step 2 — OrdinaryCreateFromConstructor reads
            // newTarget.prototype before any option validation.
            var instProto = IntlPrototypeFor(realm, newTarget, "DisplayNames", proto);
            var locale = ResolveLocale(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            // §12.1.1 step 3 — options is REQUIRED (GetOptionsObject, then a
            // required `type`).
            if (args.Length < 2 || args[1].IsUndefined)
            {
                throw new JsThrow(realm.NewTypeError("Intl.DisplayNames requires an options argument with a 'type'"));
            }

            var options = GetOptionsObject(realm, args[1]);
            _ = GetOptionEnum(realm, options, "localeMatcher", ["lookup", "best fit"], "best fit");
            var style = GetOptionEnum(realm, options, "style", ["narrow", "short", "long"], "long")!;
            var type = GetOptionEnum(realm, options, "type",
                ["language", "region", "script", "currency", "calendar", "dateTimeField"], null)
                ?? throw new JsThrow(realm.NewTypeError("Intl.DisplayNames options must include a 'type'"));
            var fallback = GetOptionEnum(realm, options, "fallback", ["code", "none"], "code")!;
            var languageDisplay = GetOptionEnum(realm, options, "languageDisplay", ["dialect", "standard"], "dialect")!;
            return JsValue.Object(new IntlDisplayNamesObject(
                instProto, StripExtensions(locale.Name), type, style, fallback,
                type == "language" ? languageDisplay : null));
        }, isConstructor: true);
        WireIntlCtor(realm, intl, ctor, proto, "DisplayNames", "Intl.DisplayNames");

        IntrinsicHelpers.DefineMethod(realm, proto, "of", 1, (thisV, args) =>
        {
            var dn = thisV.IsObject && thisV.AsObject is IntlDisplayNamesObject d
                ? d
                : throw new JsThrow(realm.NewTypeError("Intl.DisplayNames.prototype.of called on incompatible receiver"));
            var code = AbstractOperations.ToStringJs(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
            code = CanonicalizeDisplayNamesCode(realm, dn.Type, code);
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
            o.Set("locale", JsValue.String(dn.Locale));
            o.Set("style", JsValue.String(dn.Style));
            o.Set("type", JsValue.String(dn.Type));
            o.Set("fallback", JsValue.String(dn.Fallback));
            if (dn.LanguageDisplay is not null)
            {
                o.Set("languageDisplay", JsValue.String(dn.LanguageDisplay));
            }

            return JsValue.Object(o);
        });
    }

    private static string CanonicalizeDisplayNamesCode(JsRealm realm, string type, string code)
    {
        switch (type)
        {
            case "language":
            {
                if (!TryParseUnicodeLocaleId(code, out var id)
                    || id.HasT || id.HasU || id.OtherExtensions.Count > 0 || id.PrivateUse is not null)
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid language code: '{code}'"));
                }

                CanonicalizeLocaleId(id);
                return id.BaseName();
            }

            case "region":
                if (!IsRegionSubtag(code))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid region code: '{code}'"));
                }

                return code.ToUpperInvariant();
            case "script":
                if (!IsScriptSubtag(code))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid script code: '{code}'"));
                }

                return TitleCaseSubtag(code);
            case "currency":
                if (!IsWellFormedCurrencyCode(code))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid currency code: '{code}'"));
                }

                return code.ToUpperInvariant();
            case "calendar":
                if (!IsWellFormedNumberingSystem(code))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid calendar: '{code}'"));
                }

                return CanonicalizeCalendarAlias(code.ToLowerInvariant());
            default:
                return code;
        }
    }

    private static string? DisplayNameOf(JsRealm realm, string type, string code)
    {
        switch (type)
        {
            case "language":
                return code switch
                {
                    "en" => "English",
                    "en-US" => "American English",
                    "en-GB" => "British English",
                    "de" => "German",
                    "fr" => "French",
                    "es" => "Spanish",
                    "it" => "Italian",
                    "ja" => "Japanese",
                    "ko" => "Korean",
                    "zh" => "Chinese",
                    "pt" => "Portuguese",
                    "ru" => "Russian",
                    "ar" => "Arabic",
                    "hi" => "Hindi",
                    "nl" => "Dutch",
                    "pl" => "Polish",
                    "th" => "Thai",
                    "tr" => "Turkish",
                    "sv" => "Swedish",
                    _ => null,
                };

            case "region":
                return code switch
                {
                    "US" => "United States",
                    "GB" => "United Kingdom",
                    "DE" => "Germany",
                    "FR" => "France",
                    "ES" => "Spain",
                    "IT" => "Italy",
                    "JP" => "Japan",
                    "KR" => "South Korea",
                    "CN" => "China",
                    "BR" => "Brazil",
                    "RU" => "Russia",
                    "IN" => "India",
                    "CA" => "Canada",
                    "AU" => "Australia",
                    "MX" => "Mexico",
                    "NL" => "Netherlands",
                    "419" => "Latin America",
                    _ => null,
                };

            case "script":
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
