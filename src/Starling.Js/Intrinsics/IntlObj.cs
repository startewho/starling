using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// Compatibility-lite ECMA-402 surface for bundles that feature-detect Intl.
/// This is intentionally small and deterministic: UTC dates, a default en-US
/// locale, and .NET globalization-backed formatting for valid locale tags.
/// </summary>
public static partial class IntlObj
{
    private const string DefaultLocale = "en-US";
    private const string DefaultCalendar = "gregory";
    private const string DefaultNumberingSystem = "latn";
    private const string DefaultTimeZone = "UTC";

    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);

        var intl = realm.NewOrdinaryObject();
        var dateTimeFormatProto = realm.NewOrdinaryObject();
        var numberFormatProto = realm.NewOrdinaryObject();
        var collatorProto = realm.NewOrdinaryObject();
        var localeProto = realm.NewOrdinaryObject();

        var dateTimeFormatCtor = CreateDateTimeFormatCtor(realm, dateTimeFormatProto);
        var numberFormatCtor = CreateNumberFormatCtor(realm, numberFormatProto);
        var collatorCtor = CreateCollatorCtor(realm, collatorProto);
        var localeCtor = CreateLocaleCtor(realm, localeProto);

        DefineData(intl, "DateTimeFormat", JsValue.Object(dateTimeFormatCtor));
        DefineData(intl, "NumberFormat", JsValue.Object(numberFormatCtor));
        DefineData(intl, "Collator", JsValue.Object(collatorCtor));
        InstallListFormat(realm, intl);
        InstallPluralRules(realm, intl);
        InstallRelativeTimeFormat(realm, intl);
        InstallSegmenter(realm, intl);
        InstallDisplayNames(realm, intl);
        DefineData(intl, "Locale", JsValue.Object(localeCtor));
        IntrinsicHelpers.DefineMethod(realm, intl, "getCanonicalLocales", 1,
            (_, args) => MakeLocaleArray(realm, ReadRequestedLocales(realm, args.Length > 0 ? args[0] : JsValue.Undefined)));
        IntrinsicHelpers.DefineMethod(realm, intl, "supportedValuesOf", 1,
            (_, args) => SupportedValuesOf(realm, args.Length > 0 ? args[0] : JsValue.Undefined));
        intl.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl"), writable: false, enumerable: false, configurable: true));

        realm.GlobalObject.DefineOwnProperty("Intl",
            PropertyDescriptor.Data(JsValue.Object(intl), writable: true, enumerable: false, configurable: true));
    }

    private static JsNativeFunction CreateDateTimeFormatCtor(JsRealm realm, JsObject proto)
    {
        var ctor = new JsNativeFunction(realm, "DateTimeFormat", 2, (newTarget, args) =>
        {
            var state = CreateDateTimeFormatState(realm, args);
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(CreateDateTimeFormatInstance(realm, instProto, state));
        }, isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        IntrinsicHelpers.DefineMethod(realm, ctor, "supportedLocalesOf", 1,
            (_, args) => SupportedLocalesOf(realm, args));
        proto.DefineOwnProperty("format", PropertyDescriptor.Accessor(
            new JsNativeFunction(realm, "get format", 0, (thisV, _) =>
            {
                var dtf = RequireDateTimeFormat(realm, thisV);
                dtf.BoundFormat ??= new JsNativeFunction(realm, "", 1,
                    (_, args) => JsValue.String(dtf.Format(realm, args.Length > 0 ? args[0] : JsValue.Undefined)),
                    isConstructor: false);
                return JsValue.Object(dtf.BoundFormat);
            }),
            null));
        // §11.3.5 formatToParts — one scanner over the formatted text: digit
        // runs classified positionally (en pattern month/day/year, then
        // hour/minute/second), everything else literal.
        IntrinsicHelpers.DefineMethod(realm, proto, "formatToParts", 1, (thisV, args) =>
        {
            var dtf = RequireDateTimeFormat(realm, thisV);
            var text = dtf.Format(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var parts = new JsArray(realm);
            var fieldOrder = new[] { "month", "day", "year", "hour", "minute", "second" };
            var fieldIdx = 0;
            var i = 0;
            while (i < text.Length)
            {
                if (char.IsAsciiDigit(text[i]))
                {
                    var start = i;
                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                    {
                        i++;
                    }

                    var type = fieldIdx < fieldOrder.Length ? fieldOrder[fieldIdx++] : "literal";
                    parts.Push(MakePart(realm, type, text[start..i]));
                }
                else
                {
                    var start = i;
                    while (i < text.Length && !char.IsAsciiDigit(text[i]))
                    {
                        i++;
                    }

                    var chunk = text[start..i];
                    parts.Push(MakePart(realm, chunk is "AM" or "PM" ? "dayPeriod" : "literal", chunk));
                }
            }

            return JsValue.Object(parts);
        });
        // §11.3.6/.7 formatRange/formatRangeToParts — both bounds required.
        IntrinsicHelpers.DefineMethod(realm, proto, "formatRange", 2, (thisV, args) =>
        {
            var dtf = RequireDateTimeFormat(realm, thisV);
            if (args.Length < 2 || args[0].IsUndefined || args[1].IsUndefined)
            {
                throw new JsThrow(realm.NewTypeError("Intl.DateTimeFormat.prototype.formatRange requires two arguments"));
            }

            var a = dtf.Format(realm, args[0]);
            var b = dtf.Format(realm, args[1]);
            return JsValue.String(a == b ? a : a + " \u2013 " + b);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0,
            (thisV, _) => RequireDateTimeFormat(realm, thisV).ResolvedOptions(realm));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl.DateTimeFormat"), writable: false, enumerable: false, configurable: true));
        return ctor;
    }

    private static JsNativeFunction CreateNumberFormatCtor(JsRealm realm, JsObject proto)
    {
        var ctor = new JsNativeFunction(realm, "NumberFormat", 2, (newTarget, args) =>
        {
            var state = CreateNumberFormatState(realm, args);
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(CreateNumberFormatInstance(realm, instProto, state));
        }, isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        IntrinsicHelpers.DefineMethod(realm, ctor, "supportedLocalesOf", 1,
            (_, args) => SupportedLocalesOf(realm, args));
        proto.DefineOwnProperty("format", PropertyDescriptor.Accessor(
            new JsNativeFunction(realm, "get format", 0, (thisV, _) =>
            {
                var nf = RequireNumberFormat(realm, thisV);
                nf.BoundFormat ??= new JsNativeFunction(realm, "", 1,
                    (_, args) => JsValue.String(nf.Format(args.Length > 0 ? args[0] : JsValue.Undefined)),
                    isConstructor: false);
                return JsValue.Object(nf.BoundFormat);
            }),
            null));
        IntrinsicHelpers.DefineMethod(realm, proto, "formatToParts", 1, (thisV, args) =>
        {
            var text = RequireNumberFormat(realm, thisV).Format(args.Length > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Object(NumberPartsFrom(realm, text));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0,
            (thisV, _) => RequireNumberFormat(realm, thisV).ResolvedOptions(realm));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl.NumberFormat"), writable: false, enumerable: false, configurable: true));
        return ctor;
    }

    private static JsNativeFunction CreateCollatorCtor(JsRealm realm, JsObject proto)
    {
        var ctor = new JsNativeFunction(realm, "Collator", 2, (newTarget, args) =>
        {
            var state = CreateCollatorState(realm, args);
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(CreateCollatorInstance(realm, instProto, state));
        }, isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        IntrinsicHelpers.DefineMethod(realm, ctor, "supportedLocalesOf", 1,
            (_, args) => SupportedLocalesOf(realm, args));
        proto.DefineOwnProperty("compare", PropertyDescriptor.Accessor(
            new JsNativeFunction(realm, "get compare", 0, (thisV, _) =>
            {
                var col = RequireCollator(realm, thisV);
                col.BoundCompare ??= new JsNativeFunction(realm, "", 2,
                    (_, args) => JsValue.Number(col.Compare(args)), isConstructor: false);
                return JsValue.Object(col.BoundCompare);
            }),
            null));
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0,
            (thisV, _) => RequireCollator(realm, thisV).ResolvedOptions(realm));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl.Collator"), writable: false, enumerable: false, configurable: true));
        return ctor;
    }

    private static JsNativeFunction CreateLocaleCtor(JsRealm realm, JsObject proto)
    {
        var ctor = new JsNativeFunction(realm, "Locale", 1, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Intl.Locale requires 'new'"));
            }

            var state = CreateLocaleState(
                realm,
                args.Length > 0 ? args[0] : JsValue.Undefined,
                args.Length > 1 ? args[1] : JsValue.Undefined);
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(CreateLocaleInstance(realm, instProto, state));
        }, isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0,
            (thisV, _) => JsValue.String(RequireLocale(realm, thisV).State.Name));
        IntrinsicHelpers.DefineMethod(realm, proto, "minimize", 0,
            (thisV, _) => JsValue.Object(CreateLocaleInstance(
                realm,
                proto,
                RequireLocale(realm, thisV).State.Minimized())));
        // §14.3.14 maximize — AddLikelySubtags. Without a CLDR likely-subtags
        // table the identity mapping is the honest minimal form.
        IntrinsicHelpers.DefineMethod(realm, proto, "maximize", 0,
            (thisV, _) => JsValue.Object(CreateLocaleInstance(
                realm,
                proto,
                RequireLocale(realm, thisV).State)));
        // §14.3 — the characteristic properties are prototype ACCESSORS
        // (get-only, non-enumerable, configurable).
        void Getter(string name, Func<IntlLocaleState, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0,
                    (thisV, _) => read(RequireLocale(realm, thisV).State)),
                null));
        Getter("baseName", st => JsValue.String(st.BaseName));
        Getter("language", st => JsValue.String(st.Language));
        Getter("script", st => st.Script is null ? JsValue.Undefined : JsValue.String(st.Script));
        Getter("region", st => st.Region is null ? JsValue.Undefined : JsValue.String(st.Region));
        Getter("calendar", st => st.Calendar is null ? JsValue.Undefined : JsValue.String(st.Calendar));
        Getter("hourCycle", st => st.HourCycle is null ? JsValue.Undefined : JsValue.String(st.HourCycle));
        Getter("numeric", st => JsValue.Boolean(st.Numeric));
        Getter("caseFirst", _ => JsValue.Undefined);
        Getter("collation", _ => JsValue.Undefined);
        Getter("numberingSystem", _ => JsValue.String("latn"));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl.Locale"), writable: false, enumerable: false, configurable: true));
        return ctor;
    }

    // §11.5.5/§15.5.3/§10.3.3 — format/compare are GETTERS on the prototype
    // returning a per-instance cached bound function ([[BoundFormat]] /
    // [[BoundCompare]]); instances carry no own property.
    private static IntlDateTimeFormatObject CreateDateTimeFormatInstance(JsRealm realm, JsObject proto, IntlDateTimeFormatState state)
        => new(proto, state);

    private static IntlNumberFormatObject CreateNumberFormatInstance(JsRealm realm, JsObject proto, IntlNumberFormatState state)
        => new(proto, state);

    private static IntlCollatorObject CreateCollatorInstance(JsRealm realm, JsObject proto, IntlCollatorState state)
        => new(proto, state);

    private static IntlLocaleObject CreateLocaleInstance(JsRealm realm, JsObject proto, IntlLocaleState state)
        // §14.3 — every Locale characteristic is a PROTOTYPE GETTER (see
        // CreateLocaleCtor), so instances carry no own data properties.
        => new(proto, state);

    private static IntlDateTimeFormatState CreateDateTimeFormatState(JsRealm realm, JsValue[] args)
    {
        var locale = ResolveLocale(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var options = args.Length > 1 && args[1].IsObject ? args[1].AsObject : null;
        var year = GetStringOption(realm, options, "year", "numeric", "2-digit");
        var month = GetStringOption(realm, options, "month", "numeric", "2-digit", "short", "long");
        var day = GetStringOption(realm, options, "day", "numeric", "2-digit");
        var hour = GetStringOption(realm, options, "hour", "numeric", "2-digit");
        var minute = GetStringOption(realm, options, "minute", "numeric", "2-digit");
        var second = GetStringOption(realm, options, "second", "numeric", "2-digit");
        var hasAnyField = year is not null || month is not null || day is not null || hour is not null || minute is not null || second is not null;
        if (!hasAnyField)
        {
            year = "numeric";
            month = "numeric";
            day = "numeric";
        }
        var hour12 = GetBooleanOption(realm, options, "hour12") ?? false;
        return new IntlDateTimeFormatState(locale, year, month, day, hour, minute, second, hour12);
    }

    private static IntlNumberFormatState CreateNumberFormatState(JsRealm realm, JsValue[] args)
    {
        var locale = ResolveLocale(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var options = args.Length > 1 && args[1].IsObject ? args[1].AsObject : null;
        var style = GetStringOption(realm, options, "style", "decimal", "percent", "currency") ?? "decimal";
        var defaultDigits = style == "currency" ? 2 : style == "percent" ? 0 : 3;
        var minDefault = style == "currency" ? 2 : 0;
        var minFraction = GetNumberOption(realm, options, "minimumFractionDigits", 0, 20, minDefault);
        var maxFraction = GetNumberOption(realm, options, "maximumFractionDigits", 0, 20, defaultDigits);
        if (maxFraction < minFraction)
        {
            maxFraction = minFraction;
        }

        var currency = GetStringOption(realm, options, "currency") ?? "USD";
        var useGrouping = GetBooleanOption(realm, options, "useGrouping") ?? true;
        var minInteger = GetNumberOption(realm, options, "minimumIntegerDigits", 1, 21, 1);
        return new IntlNumberFormatState(locale, style, currency.ToUpperInvariant(), minFraction, maxFraction, useGrouping, minInteger);
    }

    private static IntlCollatorState CreateCollatorState(JsRealm realm, JsValue[] args)
    {
        var locale = ResolveLocale(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var options = args.Length > 1 && args[1].IsObject ? args[1].AsObject : null;
        var usage = GetStringOption(realm, options, "usage", "sort", "search") ?? "sort";
        var sensitivity = GetStringOption(realm, options, "sensitivity", "base", "accent", "case", "variant") ?? "variant";
        var numeric = GetBooleanOption(realm, options, "numeric") ?? false;
        return new IntlCollatorState(locale, usage, sensitivity, numeric);
    }

    private static IntlLocaleState CreateLocaleState(JsRealm realm, JsValue tagValue, JsValue optionsValue)
    {
        var tag = AbstractOperations.ToStringJs(realm.ActiveVm, tagValue);
        var state = ParseLocaleState(realm, tag);
        var options = optionsValue.IsObject ? optionsValue.AsObject : null;
        var calendar = GetStringOption(realm, options, "calendar") ?? state.Calendar;
        var hourCycle = GetStringOption(realm, options, "hourCycle", "h11", "h12", "h23", "h24") ?? state.HourCycle;
        var numeric = GetBooleanOption(realm, options, "numeric") ?? state.Numeric;
        return state with
        {
            Calendar = calendar,
            HourCycle = hourCycle,
            Numeric = numeric,
            Name = BuildLocaleName(state.BaseName, calendar, hourCycle, numeric)
        };
    }

    private static JsValue SupportedLocalesOf(JsRealm realm, JsValue[] args)
        => MakeLocaleArray(realm, ReadRequestedLocales(realm, args.Length > 0 ? args[0] : JsValue.Undefined));

    private static JsValue SupportedValuesOf(JsRealm realm, JsValue keyValue)
    {
        var key = AbstractOperations.ToStringJs(realm.ActiveVm, keyValue);
        var values = key switch
        {
            "calendar" => new[] { "gregory", "buddhist", "chinese", "iso8601" },
            "collation" => new[] { "default", "emoji", "eor" },
            "currency" => new[] { "EUR", "GBP", "JPY", "USD" },
            "numberingSystem" => new[] { "latn", "arab", "hanidec" },
            "timeZone" => new[] { "UTC", "America/New_York", "Europe/London" },
            "unit" => new[] { "meter", "second", "kilometer", "byte" },
            _ => throw new JsThrow(realm.NewRangeError("invalid key for Intl.supportedValuesOf"))
        };
        return MakeStringArray(realm, values);
    }

    private static JsValue MakeLocaleArray(JsRealm realm, List<string> locales)
        => MakeStringArray(realm, locales);

    private static JsValue MakeStringArray(JsRealm realm, IReadOnlyList<string> values)
    {
        var arr = new JsArray(realm);
        for (var i = 0; i < values.Count; i++)
        {
            arr.Push(JsValue.String(values[i]));
        }

        return JsValue.Object(arr);
    }

    private static List<string> ReadRequestedLocales(JsRealm realm, JsValue value)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (value.IsUndefined)
        {
            return result;
        }

        if (value.IsString)
        {
            AddSupportedLocale(value.AsString, result, seen);
            return result;
        }
        if (!value.IsObject)
        {
            return result;
        }

        var obj = value.AsObject;
        var length = ToLength(AbstractOperations.Get(realm.ActiveVm, obj, "length"));
        for (var i = 0; i < length; i++)
        {
            var item = AbstractOperations.Get(realm.ActiveVm, obj, i.ToString(CultureInfo.InvariantCulture));
            if (item.IsUndefined)
            {
                continue;
            }

            AddSupportedLocale(AbstractOperations.ToStringJs(realm.ActiveVm, item), result, seen);
        }
        return result;
    }

    private static void AddSupportedLocale(string requested, List<string> result, HashSet<string> seen)
    {
        if (!TryCreateLocale(requested, out var locale))
        {
            return;
        }

        // §9.2.1 CanonicalizeLocaleList PRESERVES Unicode extensions
        // ("ar-u-nu-arab" stays intact) — and ResolveLocale re-parses the
        // extension from this list, so stripping it here would lose the
        // requested numbering system.
        var name = locale.Name;
        var ext = requested.Replace('_', '-');
        var extIdx = ext.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (extIdx >= 0)
        {
            name += ext[extIdx..].ToLowerInvariant();
        }

        if (seen.Add(name))
        {
            result.Add(name);
        }
    }

    private static IntlLocale ResolveLocale(JsRealm realm, JsValue value)
    {
        var requested = ReadRequestedLocales(realm, value);
        if (requested.Count > 0 && TryCreateLocale(requested[0], out var locale))
        {
            return locale;
        }

        return TryCreateLocale(DefaultLocale, out var defaultLocale)
            ? defaultLocale
            : new IntlLocale(DefaultLocale, CultureInfo.InvariantCulture);
    }

    private static bool TryCreateLocale(string locale, out IntlLocale result)
    {
        result = new IntlLocale(DefaultLocale, CultureInfo.InvariantCulture);
        // BCP-47 `-u-nu-<system>` selects the numbering system; capture it
        // before the extension is stripped for CultureInfo resolution.
        var nu = "latn";
        var nuIdx = locale.IndexOf("-nu-", StringComparison.OrdinalIgnoreCase);
        if (nuIdx >= 0)
        {
            var rest = locale[(nuIdx + 4)..];
            var end = rest.IndexOf('-');
            var candidate = (end >= 0 ? rest[..end] : rest).ToLowerInvariant();
            if (NumberingSystemDigits.ContainsKey(candidate))
            {
                nu = candidate;
            }
        }

        var normalized = CanonicalLocaleName(NormalizeLocale(locale));
        if (normalized.Length == 0)
        {
            return false;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            var name = string.IsNullOrEmpty(culture.Name) ? normalized : culture.Name;
            result = new IntlLocale(name, culture, nu);
            return true;
        }
        catch (CultureNotFoundException)
        {
            if (!IsKnownFallbackLocale(normalized))
            {
                return false;
            }

            result = new IntlLocale(normalized, CultureInfo.InvariantCulture, nu);
            return true;
        }
    }

    private static string NormalizeLocale(string locale)
    {
        var trimmed = locale.Trim().Replace('_', '-');
        var extension = trimmed.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (extension >= 0)
        {
            trimmed = trimmed[..extension];
        }

        return trimmed;
    }

    private static string CanonicalLocaleName(string locale)
    {
        if (locale.Length == 0)
        {
            return string.Empty;
        }

        var parts = locale.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = i == 0
                ? parts[i].ToLowerInvariant()
                : parts[i].Length == 2
                    ? parts[i].ToUpperInvariant()
                    : parts[i];
        }
        return string.Join('-', parts);
    }

    private static bool IsKnownFallbackLocale(string locale) => locale is
        "en" or "en-US" or "en-GB" or
        "de-DE" or "fr-FR" or "es-ES" or "it-IT" or
        "ja-JP" or "zh-CN" or "ko-KR" or "pt-BR" or "nl-NL";

    private static IntlLocaleState ParseLocaleState(JsRealm realm, string tag)
    {
        if (tag.Length == 0 || tag.AsSpan().IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            throw new JsThrow(realm.NewRangeError("invalid locale tag"));
        }

        var parts = tag.Replace('_', '-').Split('-', StringSplitOptions.None);
        if (parts.Length == 0 || parts[0].Length is < 2 or > 8 || !IsAsciiLetters(parts[0]))
        {
            throw new JsThrow(realm.NewRangeError("invalid locale tag"));
        }

        var language = parts[0].ToLowerInvariant();
        string? script = null;
        string? region = null;
        string? calendar = null;
        string? hourCycle = null;
        var numeric = false;
        var i = 1;

        if (i < parts.Length && parts[i].Length == 4 && IsAsciiLetters(parts[i]))
        {
            script = char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant();
            i++;
        }

        if (i < parts.Length && IsRegionSubtag(parts[i]))
        {
            region = parts[i].ToUpperInvariant();
            i++;
        }

        var baseParts = new List<string> { language };
        if (script is not null)
        {
            baseParts.Add(script);
        }

        if (region is not null)
        {
            baseParts.Add(region);
        }

        var baseName = string.Join('-', baseParts);

        for (; i < parts.Length; i++)
        {
            if (!string.Equals(parts[i], "u", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            i++;
            while (i < parts.Length)
            {
                var key = parts[i].ToLowerInvariant();
                if (key.Length != 2)
                {
                    break;
                }

                i++;
                var values = new List<string>();
                while (i < parts.Length && parts[i].Length != 2)
                {
                    if (parts[i].Length == 0)
                    {
                        throw new JsThrow(realm.NewRangeError("invalid locale tag"));
                    }

                    values.Add(parts[i].ToLowerInvariant());
                    i++;
                }

                var value = values.Count == 0 ? string.Empty : string.Join('-', values);
                if (key == "ca" && value.Length > 0)
                {
                    calendar = value;
                }
                else if (key == "hc" && value is "h11" or "h12" or "h23" or "h24")
                {
                    hourCycle = value;
                }
                else if (key == "kn")
                {
                    numeric = value.Length == 0 || value == "true";
                }
            }
            break;
        }

        return new IntlLocaleState(
            BuildLocaleName(baseName, calendar, hourCycle, numeric),
            baseName,
            language,
            script,
            region,
            calendar,
            hourCycle,
            numeric);
    }

    private static string BuildLocaleName(string baseName, string? calendar, string? hourCycle, bool numeric)
    {
        var extensions = new List<string>();
        if (calendar is not null)
        {
            extensions.Add("ca-" + calendar);
        }

        if (hourCycle is not null)
        {
            extensions.Add("hc-" + hourCycle);
        }

        if (numeric)
        {
            extensions.Add("kn");
        }

        return extensions.Count == 0 ? baseName : baseName + "-u-" + string.Join('-', extensions);
    }

    private static bool IsRegionSubtag(string value)
        => value.Length == 2 && IsAsciiLetters(value) || value.Length == 3 && IsAsciiDigits(value);

    private static bool IsAsciiLetters(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAsciiDigits(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c < '0' || c > '9')
            {
                return false;
            }
        }
        return true;
    }

    private static string? GetStringOption(JsRealm realm, JsObject? options, string name, params string[] allowed)
    {
        if (options is null)
        {
            return null;
        }

        var value = AbstractOperations.Get(realm.ActiveVm, options, name);
        if (value.IsUndefined)
        {
            return null;
        }

        var text = AbstractOperations.ToStringJs(realm.ActiveVm, value);
        if (allowed.Length == 0)
        {
            return text;
        }

        for (var i = 0; i < allowed.Length; i++)
        {
            if (string.Equals(text, allowed[i], StringComparison.Ordinal))
            {
                return text;
            }
        }

        return null;
    }

    private static bool? GetBooleanOption(JsRealm realm, JsObject? options, string name)
    {
        if (options is null)
        {
            return null;
        }

        var value = AbstractOperations.Get(realm.ActiveVm, options, name);
        return value.IsUndefined ? null : JsValue.ToBoolean(value);
    }

    private static int GetNumberOption(JsRealm realm, JsObject? options, string name, int min, int max, int fallback)
    {
        if (options is null)
        {
            return fallback;
        }

        var value = AbstractOperations.Get(realm.ActiveVm, options, name);
        if (value.IsUndefined)
        {
            return fallback;
        }

        var number = NumberCtor.ToNumber(value);
        if (double.IsNaN(number))
        {
            return fallback;
        }

        var integer = (int)Math.Truncate(number);
        return Math.Clamp(integer, min, max);
    }

    private static long ToLength(JsValue value)
    {
        var number = NumberCtor.ToNumber(value);
        if (double.IsNaN(number) || number <= 0)
        {
            return 0;
        }

        if (number >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (long)Math.Truncate(number);
    }

    private static IntlDateTimeFormatObject RequireDateTimeFormat(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsObject && thisV.AsObject is IntlDateTimeFormatObject obj)
        {
            return obj;
        }

        throw new JsThrow(realm.NewTypeError("Intl.DateTimeFormat method called on incompatible receiver"));
    }

    private static IntlNumberFormatObject RequireNumberFormat(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsObject && thisV.AsObject is IntlNumberFormatObject obj)
        {
            return obj;
        }

        throw new JsThrow(realm.NewTypeError("Intl.NumberFormat method called on incompatible receiver"));
    }

    private static IntlCollatorObject RequireCollator(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsObject && thisV.AsObject is IntlCollatorObject obj)
        {
            return obj;
        }

        throw new JsThrow(realm.NewTypeError("Intl.Collator method called on incompatible receiver"));
    }

    private static IntlLocaleObject RequireLocale(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsObject && thisV.AsObject is IntlLocaleObject obj)
        {
            return obj;
        }

        throw new JsThrow(realm.NewTypeError("Intl.Locale method called on incompatible receiver"));
    }

    private static void DefineData(JsObject obj, string name, JsValue value)
        => obj.DefineOwnProperty(name, PropertyDescriptor.Data(value, writable: true, enumerable: false, configurable: true));

    private readonly record struct IntlLocale(string Name, CultureInfo Culture, string NumberingSystem = "latn");

    /// <summary>Unicode numbering-system digit sets (CLDR). Formatted output
    /// maps ASCII digits into the requested system's digits.</summary>
    private static readonly Dictionary<string, string> NumberingSystemDigits = new(StringComparer.Ordinal)
    {
        ["latn"] = "0123456789",
        ["arab"] = "\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669",
        ["arabext"] = "\u06F0\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7\u06F8\u06F9",
        ["thai"] = "\u0E50\u0E51\u0E52\u0E53\u0E54\u0E55\u0E56\u0E57\u0E58\u0E59",
        ["hanidec"] = "\u3007\u4E00\u4E8C\u4E09\u56DB\u4E94\u516D\u4E03\u516B\u4E5D",
        ["deva"] = "\u0966\u0967\u0968\u0969\u096A\u096B\u096C\u096D\u096E\u096F",
        ["beng"] = "\u09E6\u09E7\u09E8\u09E9\u09EA\u09EB\u09EC\u09ED\u09EE\u09EF",
    };

    private static string MapDigits(string text, string numberingSystem)
    {
        if (numberingSystem == "latn" || !NumberingSystemDigits.TryGetValue(numberingSystem, out var digits))
        {
            return text;
        }

        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
            {
                chars[i] = digits[chars[i] - '0'];
            }
        }

        return new string(chars);
    }

    private sealed record IntlDateTimeFormatState(
        IntlLocale Locale,
        string? Year,
        string? Month,
        string? Day,
        string? Hour,
        string? Minute,
        string? Second,
        bool Hour12);

    private sealed record IntlNumberFormatState(
        IntlLocale Locale,
        string Style,
        string Currency,
        int MinimumFractionDigits,
        int MaximumFractionDigits,
        bool UseGrouping,
        int MinimumIntegerDigits = 1);

    private sealed record IntlCollatorState(IntlLocale Locale, string Usage, string Sensitivity, bool Numeric);

    private sealed record IntlLocaleState(
        string Name,
        string BaseName,
        string Language,
        string? Script,
        string? Region,
        string? Calendar,
        string? HourCycle,
        bool Numeric)
    {
        public IntlLocaleState Minimized()
            => Region == "US" && Script is null
                ? this with { Name = Language, BaseName = Language, Region = null }
                : this;
    }

    private sealed class IntlLocaleObject(JsObject prototype, IntlLocaleState state) : JsObject(prototype)
    {
        public IntlLocaleState State { get; } = state;
    }

    private sealed class IntlDateTimeFormatObject(JsObject prototype, IntlDateTimeFormatState state) : JsObject(prototype)
    {
        public JsObject? BoundFormat;

        public string Format(JsRealm realm, JsValue value)
        {
            var ms = DateMilliseconds(value);
            if (double.IsNaN(ms) || double.IsInfinity(ms))
            {
                throw new JsThrow(realm.NewRangeError("Invalid time value"));
            }

            try
            {
                var date = DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime;
                var pattern = DateTimePattern(state);
                return date.ToString(pattern, state.Locale.Culture);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new JsThrow(realm.NewRangeError("Invalid time value"));
            }
        }

        public JsValue ResolvedOptions(JsRealm realm)
        {
            var obj = realm.NewOrdinaryObject();
            obj.Set("locale", JsValue.String(state.Locale.Name));
            obj.Set("calendar", JsValue.String(DefaultCalendar));
            obj.Set("numberingSystem", JsValue.String(DefaultNumberingSystem));
            obj.Set("timeZone", JsValue.String(DefaultTimeZone));
            if (state.Year is not null)
            {
                obj.Set("year", JsValue.String(state.Year));
            }

            if (state.Month is not null)
            {
                obj.Set("month", JsValue.String(state.Month));
            }

            if (state.Day is not null)
            {
                obj.Set("day", JsValue.String(state.Day));
            }

            if (state.Hour is not null)
            {
                obj.Set("hour", JsValue.String(state.Hour));
                obj.Set("hourCycle", JsValue.String(state.Hour12 ? "h12" : "h23"));
                obj.Set("hour12", JsValue.Boolean(state.Hour12));
            }
            if (state.Minute is not null)
            {
                obj.Set("minute", JsValue.String(state.Minute));
            }

            if (state.Second is not null)
            {
                obj.Set("second", JsValue.String(state.Second));
            }

            return JsValue.Object(obj);
        }

        private static double DateMilliseconds(JsValue value)
        {
            if (value.IsUndefined)
            {
                return 0;
            }

            if (value.IsObject && value.AsObject is JsDate date)
            {
                return date.TimeValueMs;
            }

            return NumberCtor.ToNumber(value);
        }

        private static string DateTimePattern(IntlDateTimeFormatState state)
        {
            var hasDate = state.Year is not null || state.Month is not null || state.Day is not null;
            var hasTime = state.Hour is not null || state.Minute is not null || state.Second is not null;
            if (hasDate && !hasTime)
            {
                return DatePattern(state);
            }

            if (hasTime && !hasDate)
            {
                return TimePattern(state);
            }

            if (hasDate && hasTime)
            {
                return DatePattern(state) + ", " + TimePattern(state);
            }

            return ShortDatePattern(state.Locale);
        }


        private static string ShortDatePattern(IntlLocale locale) => locale.Name switch
        {
            "en-US" => "M/d/yyyy",
            "en-GB" => "dd/MM/yyyy",
            _ => locale.Culture.DateTimeFormat.ShortDatePattern,
        };

        private static string DatePattern(IntlDateTimeFormatState state)
        {
            var parts = new List<string>(3);
            if (state.Month is not null)
            {
                parts.Add(state.Month switch
                {
                    "2-digit" => "MM",
                    "short" => "MMM",
                    "long" => "MMMM",
                    _ => "M",
                });
            }

            if (state.Day is not null)
            {
                parts.Add(state.Day == "2-digit" ? "dd" : "d");
            }

            if (state.Year is not null)
            {
                parts.Add(state.Year == "2-digit" ? "yy" : "yyyy");
            }

            return parts.Count == 0 ? ShortDatePattern(state.Locale) : string.Join("/", parts);
        }

        private static string TimePattern(IntlDateTimeFormatState state)
        {
            var parts = new List<string>(3);
            if (state.Hour is not null)
            {
                parts.Add(state.Hour12 ? (state.Hour == "2-digit" ? "hh" : "h") : (state.Hour == "2-digit" ? "HH" : "H"));
            }

            if (state.Minute is not null)
            {
                parts.Add(state.Minute == "2-digit" ? "mm" : "m");
            }

            if (state.Second is not null)
            {
                parts.Add(state.Second == "2-digit" ? "ss" : "s");
            }

            var pattern = parts.Count == 0 ? "HH:mm:ss" : string.Join(":", parts);
            return state.Hour12 ? pattern + " tt" : pattern;
        }
    }

    private sealed class IntlNumberFormatObject(JsObject prototype, IntlNumberFormatState state) : JsObject(prototype)
    {
        public JsObject? BoundFormat;

        public string Format(JsValue value)
        {
            var number = value.IsUndefined ? double.NaN : NumberCtor.ToNumber(value);
            if (double.IsNaN(number))
            {
                return "NaN";
            }

            if (double.IsPositiveInfinity(number))
            {
                return "∞";
            }

            if (double.IsNegativeInfinity(number))
            {
                return "-∞";
            }

            var nfi = (NumberFormatInfo)state.Locale.Culture.NumberFormat.Clone();
            if (!state.UseGrouping)
            {
                nfi.NumberGroupSeparator = string.Empty;
                nfi.CurrencyGroupSeparator = string.Empty;
                nfi.PercentGroupSeparator = string.Empty;
            }

            var text = state.Style switch
            {
                "currency" => FormatCurrency(number, nfi),
                "percent" => FormatDecimal(number * 100, nfi) + nfi.PercentSymbol,
                _ => FormatDecimal(number, nfi),
            };
            return MapDigits(text, state.Locale.NumberingSystem);
        }

        public JsValue ResolvedOptions(JsRealm realm)
        {
            var obj = realm.NewOrdinaryObject();
            obj.Set("locale", JsValue.String(state.Locale.Name));
            obj.Set("numberingSystem", JsValue.String(state.Locale.NumberingSystem));
            obj.Set("style", JsValue.String(state.Style));
            obj.Set("minimumIntegerDigits", JsValue.Number(state.MinimumIntegerDigits));
            if (state.Style == "currency")
            {
                obj.Set("currency", JsValue.String(state.Currency));
                obj.Set("currencyDisplay", JsValue.String("symbol"));
            }
            obj.Set("minimumFractionDigits", JsValue.Number(state.MinimumFractionDigits));
            obj.Set("maximumFractionDigits", JsValue.Number(state.MaximumFractionDigits));
            obj.Set("useGrouping", JsValue.Boolean(state.UseGrouping));
            return JsValue.Object(obj);
        }

        private string FormatCurrency(double number, NumberFormatInfo nfi)
        {
            nfi.CurrencySymbol = CurrencySymbol(state.Currency);
            nfi.CurrencyDecimalDigits = state.MaximumFractionDigits;
            return number.ToString("C" + state.MaximumFractionDigits.ToString(CultureInfo.InvariantCulture), nfi);
        }

        private string FormatDecimal(double number, NumberFormatInfo nfi)
        {
            var text = number.ToString(NumberPattern(), nfi);
            // §15.5.4 — negative zero keeps its sign ("-0"). .NET drops it.
            if (number == 0 && double.IsNegative(number) && !text.StartsWith('-'))
            {
                text = "-" + text;
            }

            return text;
        }

        private string NumberPattern()
        {
            // minimumIntegerDigits pads the integer part with leading zeros.
            var intPart = new string('0', Math.Max(1, state.MinimumIntegerDigits));
            var pattern = state.UseGrouping ? "#," + intPart : intPart;
            if (state.MaximumFractionDigits == 0)
            {
                return pattern;
            }

            pattern += "." + new string('0', state.MinimumFractionDigits);
            if (state.MaximumFractionDigits > state.MinimumFractionDigits)
            {
                pattern += new string('#', state.MaximumFractionDigits - state.MinimumFractionDigits);
            }

            return pattern;
        }

        private static string CurrencySymbol(string currency) => currency switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            "JPY" => "¥",
            "CNY" => "¥",
            "KRW" => "₩",
            _ => currency + " ",
        };
    }

    private sealed class IntlCollatorObject(JsObject prototype, IntlCollatorState state) : JsObject(prototype)
    {
        public JsObject? BoundCompare;

        public int Compare(JsValue[] args)
        {
            var left = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
            var right = args.Length > 1 ? JsValue.ToStringValue(args[1]) : "undefined";
            var result = state.Locale.Culture.CompareInfo.Compare(left, right, CompareOptionsForSensitivity(state.Sensitivity));
            return Math.Sign(result);
        }

        public JsValue ResolvedOptions(JsRealm realm)
        {
            var obj = realm.NewOrdinaryObject();
            obj.Set("locale", JsValue.String(state.Locale.Name));
            obj.Set("usage", JsValue.String(state.Usage));
            obj.Set("sensitivity", JsValue.String(state.Sensitivity));
            obj.Set("ignorePunctuation", JsValue.Boolean(false));
            obj.Set("collation", JsValue.String("default"));
            obj.Set("numeric", JsValue.Boolean(state.Numeric));
            obj.Set("caseFirst", JsValue.String("false"));
            return JsValue.Object(obj);
        }

        private static CompareOptions CompareOptionsForSensitivity(string sensitivity) => sensitivity switch
        {
            "base" => CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace,
            "accent" => CompareOptions.IgnoreCase,
            "case" => CompareOptions.IgnoreNonSpace,
            _ => CompareOptions.None,
        };
    }
}
