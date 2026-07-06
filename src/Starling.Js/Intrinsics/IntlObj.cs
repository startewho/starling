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

    private static JsNativeFunction CreateCollatorCtor(JsRealm realm, JsObject proto)
    {
        var ctor = new JsNativeFunction(realm, "Collator", 2, (newTarget, args) =>
        {
            var state = CreateCollatorState(realm, args);
            var instProto = IntlPrototypeFor(realm, newTarget, "Collator", proto);
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
            var instProto = IntlPrototypeFor(realm, newTarget, "Locale", proto);
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
    private static IntlCollatorObject CreateCollatorInstance(JsRealm realm, JsObject proto, IntlCollatorState state)
        => new(proto, state);

    private static IntlLocaleObject CreateLocaleInstance(JsRealm realm, JsObject proto, IntlLocaleState state)
        // §14.3 — every Locale characteristic is a PROTOTYPE GETTER (see
        // CreateLocaleCtor), so instances carry no own data properties.
        => new(proto, state);

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
            "numberingSystem" => NumberingSystemDigits.Keys.Order(StringComparer.Ordinal).ToArray(),
            "timeZone" => new[] { "UTC", "America/New_York", "Europe/London" },
            "unit" => SanctionedUnits.Order(StringComparer.Ordinal).ToArray(),
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

    /// <summary>CanonicalizeLocaleList (§9.2.1): null throws, entries must be
    /// strings or objects, structurally invalid tags are a RangeError, and
    /// Unicode extensions are preserved on the canonical names.</summary>
    private static List<string> ReadRequestedLocales(JsRealm realm, JsValue value)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (value.IsUndefined)
        {
            return result;
        }

        if (value.IsString)
        {
            AddCanonicalLocale(realm, value.AsString, result, seen);
            return result;
        }

        if (value.IsObject && value.AsObject is IntlLocaleObject direct)
        {
            AddCanonicalLocale(realm, direct.State.Name, result, seen);
            return result;
        }

        var obj = AbstractOperations.ToObject(realm, value);
        var length = ToLength(AbstractOperations.Get(realm.ActiveVm, obj, "length"));
        for (long i = 0; i < length; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture);
            if (!AbstractOperations.HasProperty(obj, key))
            {
                continue;
            }

            var item = AbstractOperations.Get(realm.ActiveVm, obj, key);
            if (!item.IsString && !item.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("Locale list entries must be strings or objects"));
            }

            var tag = item.IsObject && item.AsObject is IntlLocaleObject lo
                ? lo.State.Name
                : AbstractOperations.ToStringJs(realm.ActiveVm, item);
            AddCanonicalLocale(realm, tag, result, seen);
        }
        return result;
    }

    private static void AddCanonicalLocale(JsRealm realm, string requested, List<string> result, HashSet<string> seen)
    {
        if (!IsStructurallyValidLanguageTag(requested))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid language tag: \"{requested}\""));
        }

        if (!TryCreateLocale(requested, out var locale))
        {
            return;
        }

        // Canonicalization PRESERVES Unicode extensions ("ar-u-nu-arab" stays
        // intact) — ResolveLocale re-parses the extension from this list.
        var name = locale.Name;
        var extIdx = requested.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (extIdx >= 0)
        {
            name += requested[extIdx..].ToLowerInvariant();
        }

        if (seen.Add(name))
        {
            result.Add(name);
        }
    }

    /// <summary>Structural unicode_locale_id check (no registry lookup):
    /// language[-script][-region](-variant)*(-singleton subtags..)*(-x-...).</summary>
    private static bool IsStructurallyValidLanguageTag(string tag)
    {
        if (tag.Length == 0)
        {
            return false;
        }

        var parts = tag.Split('-');
        var first = parts[0];
        if (!IsAsciiLetters(first) || first.Length is < 2 or > 8 || first.Length == 4)
        {
            return false;
        }

        var i = 1;
        if (first.Length <= 3)
        {
            var extlang = 0;
            while (i < parts.Length && extlang < 3 && parts[i].Length == 3 && IsAsciiLetters(parts[i]))
            {
                i++;
                extlang++;
            }
        }

        if (i < parts.Length && parts[i].Length == 4 && IsAsciiLetters(parts[i]))
        {
            i++;
        }

        if (i < parts.Length && IsRegionSubtag(parts[i]))
        {
            i++;
        }

        HashSet<string>? variants = null;
        while (i < parts.Length)
        {
            var v = parts[i];
            var isVariant = (v.Length is >= 5 and <= 8 && IsAsciiAlnum(v))
                || (v.Length == 4 && char.IsAsciiDigit(v[0]) && IsAsciiAlnum(v));
            if (!isVariant)
            {
                break;
            }

            variants ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!variants.Add(v))
            {
                return false;
            }

            i++;
        }

        HashSet<char>? singletons = null;
        while (i < parts.Length)
        {
            var sSub = parts[i];
            if (sSub.Length != 1 || !char.IsAsciiLetterOrDigit(sSub[0]))
            {
                return false;
            }

            var c = char.ToLowerInvariant(sSub[0]);
            singletons ??= [];
            if (!singletons.Add(c))
            {
                return false;
            }

            i++;
            if (c == 'x')
            {
                if (i >= parts.Length)
                {
                    return false;
                }

                while (i < parts.Length)
                {
                    if (parts[i].Length is < 1 or > 8 || !IsAsciiAlnum(parts[i]))
                    {
                        return false;
                    }

                    i++;
                }

                return true;
            }

            var subtags = 0;
            while (i < parts.Length && parts[i].Length is >= 2 and <= 8 && IsAsciiAlnum(parts[i]))
            {
                i++;
                subtags++;
            }

            if (subtags == 0)
            {
                return false;
            }
        }

        return i == parts.Length;
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
        "en" or "en-US" or "en-GB" or "en-IN" or "en-AU" or "en-CA" or
        "de" or "de-DE" or "de-AT" or "de-CH" or
        "fr" or "fr-FR" or "es" or "es-ES" or "it" or "it-IT" or
        "ja" or "ja-JP" or "ko" or "ko-KR" or
        "zh" or "zh-CN" or "zh-TW" or "zh-HK" or "zh-MO" or
        "pt" or "pt-BR" or "pt-PT" or "nl" or "nl-NL" or
        "ar" or "ar-SA" or "th" or "th-TH" or "ru" or "ru-RU" or
        "tr" or "tr-TR" or "pl" or "pl-PL" or "sv" or "sv-SE";

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

    /// <summary>Unicode decimal numbering systems (CLDR digit table). Each
    /// entry is ten code points; astral digits are surrogate pairs.</summary>
    private static readonly Dictionary<string, string[]> NumberingSystemDigits = new(StringComparer.Ordinal)
    {
        ["adlm"] = ["\uD83A\uDD50", "\uD83A\uDD51", "\uD83A\uDD52", "\uD83A\uDD53", "\uD83A\uDD54", "\uD83A\uDD55", "\uD83A\uDD56", "\uD83A\uDD57", "\uD83A\uDD58", "\uD83A\uDD59"],
        ["ahom"] = ["\uD805\uDF30", "\uD805\uDF31", "\uD805\uDF32", "\uD805\uDF33", "\uD805\uDF34", "\uD805\uDF35", "\uD805\uDF36", "\uD805\uDF37", "\uD805\uDF38", "\uD805\uDF39"],
        ["arab"] = ["\u0660", "\u0661", "\u0662", "\u0663", "\u0664", "\u0665", "\u0666", "\u0667", "\u0668", "\u0669"],
        ["arabext"] = ["\u06F0", "\u06F1", "\u06F2", "\u06F3", "\u06F4", "\u06F5", "\u06F6", "\u06F7", "\u06F8", "\u06F9"],
        ["bali"] = ["\u1B50", "\u1B51", "\u1B52", "\u1B53", "\u1B54", "\u1B55", "\u1B56", "\u1B57", "\u1B58", "\u1B59"],
        ["beng"] = ["\u09E6", "\u09E7", "\u09E8", "\u09E9", "\u09EA", "\u09EB", "\u09EC", "\u09ED", "\u09EE", "\u09EF"],
        ["bhks"] = ["\uD807\uDC50", "\uD807\uDC51", "\uD807\uDC52", "\uD807\uDC53", "\uD807\uDC54", "\uD807\uDC55", "\uD807\uDC56", "\uD807\uDC57", "\uD807\uDC58", "\uD807\uDC59"],
        ["brah"] = ["\uD804\uDC66", "\uD804\uDC67", "\uD804\uDC68", "\uD804\uDC69", "\uD804\uDC6A", "\uD804\uDC6B", "\uD804\uDC6C", "\uD804\uDC6D", "\uD804\uDC6E", "\uD804\uDC6F"],
        ["cakm"] = ["\uD804\uDD36", "\uD804\uDD37", "\uD804\uDD38", "\uD804\uDD39", "\uD804\uDD3A", "\uD804\uDD3B", "\uD804\uDD3C", "\uD804\uDD3D", "\uD804\uDD3E", "\uD804\uDD3F"],
        ["cham"] = ["\uAA50", "\uAA51", "\uAA52", "\uAA53", "\uAA54", "\uAA55", "\uAA56", "\uAA57", "\uAA58", "\uAA59"],
        ["deva"] = ["\u0966", "\u0967", "\u0968", "\u0969", "\u096A", "\u096B", "\u096C", "\u096D", "\u096E", "\u096F"],
        ["diak"] = ["\uD806\uDD50", "\uD806\uDD51", "\uD806\uDD52", "\uD806\uDD53", "\uD806\uDD54", "\uD806\uDD55", "\uD806\uDD56", "\uD806\uDD57", "\uD806\uDD58", "\uD806\uDD59"],
        ["fullwide"] = ["\uFF10", "\uFF11", "\uFF12", "\uFF13", "\uFF14", "\uFF15", "\uFF16", "\uFF17", "\uFF18", "\uFF19"],
        ["gara"] = ["\uD803\uDD40", "\uD803\uDD41", "\uD803\uDD42", "\uD803\uDD43", "\uD803\uDD44", "\uD803\uDD45", "\uD803\uDD46", "\uD803\uDD47", "\uD803\uDD48", "\uD803\uDD49"],
        ["gong"] = ["\uD807\uDDA0", "\uD807\uDDA1", "\uD807\uDDA2", "\uD807\uDDA3", "\uD807\uDDA4", "\uD807\uDDA5", "\uD807\uDDA6", "\uD807\uDDA7", "\uD807\uDDA8", "\uD807\uDDA9"],
        ["gonm"] = ["\uD807\uDD50", "\uD807\uDD51", "\uD807\uDD52", "\uD807\uDD53", "\uD807\uDD54", "\uD807\uDD55", "\uD807\uDD56", "\uD807\uDD57", "\uD807\uDD58", "\uD807\uDD59"],
        ["gujr"] = ["\u0AE6", "\u0AE7", "\u0AE8", "\u0AE9", "\u0AEA", "\u0AEB", "\u0AEC", "\u0AED", "\u0AEE", "\u0AEF"],
        ["gukh"] = ["\uD818\uDD30", "\uD818\uDD31", "\uD818\uDD32", "\uD818\uDD33", "\uD818\uDD34", "\uD818\uDD35", "\uD818\uDD36", "\uD818\uDD37", "\uD818\uDD38", "\uD818\uDD39"],
        ["guru"] = ["\u0A66", "\u0A67", "\u0A68", "\u0A69", "\u0A6A", "\u0A6B", "\u0A6C", "\u0A6D", "\u0A6E", "\u0A6F"],
        ["hanidec"] = ["\u3007", "\u4E00", "\u4E8C", "\u4E09", "\u56DB", "\u4E94", "\u516D", "\u4E03", "\u516B", "\u4E5D"],
        ["hmng"] = ["\uD81A\uDF50", "\uD81A\uDF51", "\uD81A\uDF52", "\uD81A\uDF53", "\uD81A\uDF54", "\uD81A\uDF55", "\uD81A\uDF56", "\uD81A\uDF57", "\uD81A\uDF58", "\uD81A\uDF59"],
        ["hmnp"] = ["\uD838\uDD40", "\uD838\uDD41", "\uD838\uDD42", "\uD838\uDD43", "\uD838\uDD44", "\uD838\uDD45", "\uD838\uDD46", "\uD838\uDD47", "\uD838\uDD48", "\uD838\uDD49"],
        ["java"] = ["\uA9D0", "\uA9D1", "\uA9D2", "\uA9D3", "\uA9D4", "\uA9D5", "\uA9D6", "\uA9D7", "\uA9D8", "\uA9D9"],
        ["kali"] = ["\uA900", "\uA901", "\uA902", "\uA903", "\uA904", "\uA905", "\uA906", "\uA907", "\uA908", "\uA909"],
        ["kawi"] = ["\uD807\uDF50", "\uD807\uDF51", "\uD807\uDF52", "\uD807\uDF53", "\uD807\uDF54", "\uD807\uDF55", "\uD807\uDF56", "\uD807\uDF57", "\uD807\uDF58", "\uD807\uDF59"],
        ["khmr"] = ["\u17E0", "\u17E1", "\u17E2", "\u17E3", "\u17E4", "\u17E5", "\u17E6", "\u17E7", "\u17E8", "\u17E9"],
        ["knda"] = ["\u0CE6", "\u0CE7", "\u0CE8", "\u0CE9", "\u0CEA", "\u0CEB", "\u0CEC", "\u0CED", "\u0CEE", "\u0CEF"],
        ["krai"] = ["\uD81B\uDD70", "\uD81B\uDD71", "\uD81B\uDD72", "\uD81B\uDD73", "\uD81B\uDD74", "\uD81B\uDD75", "\uD81B\uDD76", "\uD81B\uDD77", "\uD81B\uDD78", "\uD81B\uDD79"],
        ["lana"] = ["\u1A80", "\u1A81", "\u1A82", "\u1A83", "\u1A84", "\u1A85", "\u1A86", "\u1A87", "\u1A88", "\u1A89"],
        ["lanatham"] = ["\u1A90", "\u1A91", "\u1A92", "\u1A93", "\u1A94", "\u1A95", "\u1A96", "\u1A97", "\u1A98", "\u1A99"],
        ["laoo"] = ["\u0ED0", "\u0ED1", "\u0ED2", "\u0ED3", "\u0ED4", "\u0ED5", "\u0ED6", "\u0ED7", "\u0ED8", "\u0ED9"],
        ["latn"] = ["\u0030", "\u0031", "\u0032", "\u0033", "\u0034", "\u0035", "\u0036", "\u0037", "\u0038", "\u0039"],
        ["lepc"] = ["\u1C40", "\u1C41", "\u1C42", "\u1C43", "\u1C44", "\u1C45", "\u1C46", "\u1C47", "\u1C48", "\u1C49"],
        ["limb"] = ["\u1946", "\u1947", "\u1948", "\u1949", "\u194A", "\u194B", "\u194C", "\u194D", "\u194E", "\u194F"],
        ["mathbold"] = ["\uD835\uDFCE", "\uD835\uDFCF", "\uD835\uDFD0", "\uD835\uDFD1", "\uD835\uDFD2", "\uD835\uDFD3", "\uD835\uDFD4", "\uD835\uDFD5", "\uD835\uDFD6", "\uD835\uDFD7"],
        ["mathdbl"] = ["\uD835\uDFD8", "\uD835\uDFD9", "\uD835\uDFDA", "\uD835\uDFDB", "\uD835\uDFDC", "\uD835\uDFDD", "\uD835\uDFDE", "\uD835\uDFDF", "\uD835\uDFE0", "\uD835\uDFE1"],
        ["mathmono"] = ["\uD835\uDFF6", "\uD835\uDFF7", "\uD835\uDFF8", "\uD835\uDFF9", "\uD835\uDFFA", "\uD835\uDFFB", "\uD835\uDFFC", "\uD835\uDFFD", "\uD835\uDFFE", "\uD835\uDFFF"],
        ["mathsanb"] = ["\uD835\uDFEC", "\uD835\uDFED", "\uD835\uDFEE", "\uD835\uDFEF", "\uD835\uDFF0", "\uD835\uDFF1", "\uD835\uDFF2", "\uD835\uDFF3", "\uD835\uDFF4", "\uD835\uDFF5"],
        ["mathsans"] = ["\uD835\uDFE2", "\uD835\uDFE3", "\uD835\uDFE4", "\uD835\uDFE5", "\uD835\uDFE6", "\uD835\uDFE7", "\uD835\uDFE8", "\uD835\uDFE9", "\uD835\uDFEA", "\uD835\uDFEB"],
        ["mlym"] = ["\u0D66", "\u0D67", "\u0D68", "\u0D69", "\u0D6A", "\u0D6B", "\u0D6C", "\u0D6D", "\u0D6E", "\u0D6F"],
        ["modi"] = ["\uD805\uDE50", "\uD805\uDE51", "\uD805\uDE52", "\uD805\uDE53", "\uD805\uDE54", "\uD805\uDE55", "\uD805\uDE56", "\uD805\uDE57", "\uD805\uDE58", "\uD805\uDE59"],
        ["mong"] = ["\u1810", "\u1811", "\u1812", "\u1813", "\u1814", "\u1815", "\u1816", "\u1817", "\u1818", "\u1819"],
        ["mroo"] = ["\uD81A\uDE60", "\uD81A\uDE61", "\uD81A\uDE62", "\uD81A\uDE63", "\uD81A\uDE64", "\uD81A\uDE65", "\uD81A\uDE66", "\uD81A\uDE67", "\uD81A\uDE68", "\uD81A\uDE69"],
        ["mtei"] = ["\uABF0", "\uABF1", "\uABF2", "\uABF3", "\uABF4", "\uABF5", "\uABF6", "\uABF7", "\uABF8", "\uABF9"],
        ["mymr"] = ["\u1040", "\u1041", "\u1042", "\u1043", "\u1044", "\u1045", "\u1046", "\u1047", "\u1048", "\u1049"],
        ["mymrepka"] = ["\uD805\uDEDA", "\uD805\uDEDB", "\uD805\uDEDC", "\uD805\uDEDD", "\uD805\uDEDE", "\uD805\uDEDF", "\uD805\uDEE0", "\uD805\uDEE1", "\uD805\uDEE2", "\uD805\uDEE3"],
        ["mymrpao"] = ["\uD805\uDED0", "\uD805\uDED1", "\uD805\uDED2", "\uD805\uDED3", "\uD805\uDED4", "\uD805\uDED5", "\uD805\uDED6", "\uD805\uDED7", "\uD805\uDED8", "\uD805\uDED9"],
        ["mymrshan"] = ["\u1090", "\u1091", "\u1092", "\u1093", "\u1094", "\u1095", "\u1096", "\u1097", "\u1098", "\u1099"],
        ["mymrtlng"] = ["\uA9F0", "\uA9F1", "\uA9F2", "\uA9F3", "\uA9F4", "\uA9F5", "\uA9F6", "\uA9F7", "\uA9F8", "\uA9F9"],
        ["nagm"] = ["\uD839\uDCF0", "\uD839\uDCF1", "\uD839\uDCF2", "\uD839\uDCF3", "\uD839\uDCF4", "\uD839\uDCF5", "\uD839\uDCF6", "\uD839\uDCF7", "\uD839\uDCF8", "\uD839\uDCF9"],
        ["newa"] = ["\uD805\uDC50", "\uD805\uDC51", "\uD805\uDC52", "\uD805\uDC53", "\uD805\uDC54", "\uD805\uDC55", "\uD805\uDC56", "\uD805\uDC57", "\uD805\uDC58", "\uD805\uDC59"],
        ["nkoo"] = ["\u07C0", "\u07C1", "\u07C2", "\u07C3", "\u07C4", "\u07C5", "\u07C6", "\u07C7", "\u07C8", "\u07C9"],
        ["olck"] = ["\u1C50", "\u1C51", "\u1C52", "\u1C53", "\u1C54", "\u1C55", "\u1C56", "\u1C57", "\u1C58", "\u1C59"],
        ["onao"] = ["\uD839\uDDF1", "\uD839\uDDF2", "\uD839\uDDF3", "\uD839\uDDF4", "\uD839\uDDF5", "\uD839\uDDF6", "\uD839\uDDF7", "\uD839\uDDF8", "\uD839\uDDF9", "\uD839\uDDFA"],
        ["orya"] = ["\u0B66", "\u0B67", "\u0B68", "\u0B69", "\u0B6A", "\u0B6B", "\u0B6C", "\u0B6D", "\u0B6E", "\u0B6F"],
        ["osma"] = ["\uD801\uDCA0", "\uD801\uDCA1", "\uD801\uDCA2", "\uD801\uDCA3", "\uD801\uDCA4", "\uD801\uDCA5", "\uD801\uDCA6", "\uD801\uDCA7", "\uD801\uDCA8", "\uD801\uDCA9"],
        ["outlined"] = ["\uD833\uDCF0", "\uD833\uDCF1", "\uD833\uDCF2", "\uD833\uDCF3", "\uD833\uDCF4", "\uD833\uDCF5", "\uD833\uDCF6", "\uD833\uDCF7", "\uD833\uDCF8", "\uD833\uDCF9"],
        ["rohg"] = ["\uD803\uDD30", "\uD803\uDD31", "\uD803\uDD32", "\uD803\uDD33", "\uD803\uDD34", "\uD803\uDD35", "\uD803\uDD36", "\uD803\uDD37", "\uD803\uDD38", "\uD803\uDD39"],
        ["saur"] = ["\uA8D0", "\uA8D1", "\uA8D2", "\uA8D3", "\uA8D4", "\uA8D5", "\uA8D6", "\uA8D7", "\uA8D8", "\uA8D9"],
        ["segment"] = ["\uD83E\uDFF0", "\uD83E\uDFF1", "\uD83E\uDFF2", "\uD83E\uDFF3", "\uD83E\uDFF4", "\uD83E\uDFF5", "\uD83E\uDFF6", "\uD83E\uDFF7", "\uD83E\uDFF8", "\uD83E\uDFF9"],
        ["shrd"] = ["\uD804\uDDD0", "\uD804\uDDD1", "\uD804\uDDD2", "\uD804\uDDD3", "\uD804\uDDD4", "\uD804\uDDD5", "\uD804\uDDD6", "\uD804\uDDD7", "\uD804\uDDD8", "\uD804\uDDD9"],
        ["sind"] = ["\uD804\uDEF0", "\uD804\uDEF1", "\uD804\uDEF2", "\uD804\uDEF3", "\uD804\uDEF4", "\uD804\uDEF5", "\uD804\uDEF6", "\uD804\uDEF7", "\uD804\uDEF8", "\uD804\uDEF9"],
        ["sinh"] = ["\u0DE6", "\u0DE7", "\u0DE8", "\u0DE9", "\u0DEA", "\u0DEB", "\u0DEC", "\u0DED", "\u0DEE", "\u0DEF"],
        ["sora"] = ["\uD804\uDCF0", "\uD804\uDCF1", "\uD804\uDCF2", "\uD804\uDCF3", "\uD804\uDCF4", "\uD804\uDCF5", "\uD804\uDCF6", "\uD804\uDCF7", "\uD804\uDCF8", "\uD804\uDCF9"],
        ["sund"] = ["\u1BB0", "\u1BB1", "\u1BB2", "\u1BB3", "\u1BB4", "\u1BB5", "\u1BB6", "\u1BB7", "\u1BB8", "\u1BB9"],
        ["sunu"] = ["\uD806\uDFF0", "\uD806\uDFF1", "\uD806\uDFF2", "\uD806\uDFF3", "\uD806\uDFF4", "\uD806\uDFF5", "\uD806\uDFF6", "\uD806\uDFF7", "\uD806\uDFF8", "\uD806\uDFF9"],
        ["takr"] = ["\uD805\uDEC0", "\uD805\uDEC1", "\uD805\uDEC2", "\uD805\uDEC3", "\uD805\uDEC4", "\uD805\uDEC5", "\uD805\uDEC6", "\uD805\uDEC7", "\uD805\uDEC8", "\uD805\uDEC9"],
        ["talu"] = ["\u19D0", "\u19D1", "\u19D2", "\u19D3", "\u19D4", "\u19D5", "\u19D6", "\u19D7", "\u19D8", "\u19D9"],
        ["tamldec"] = ["\u0BE6", "\u0BE7", "\u0BE8", "\u0BE9", "\u0BEA", "\u0BEB", "\u0BEC", "\u0BED", "\u0BEE", "\u0BEF"],
        ["telu"] = ["\u0C66", "\u0C67", "\u0C68", "\u0C69", "\u0C6A", "\u0C6B", "\u0C6C", "\u0C6D", "\u0C6E", "\u0C6F"],
        ["thai"] = ["\u0E50", "\u0E51", "\u0E52", "\u0E53", "\u0E54", "\u0E55", "\u0E56", "\u0E57", "\u0E58", "\u0E59"],
        ["tibt"] = ["\u0F20", "\u0F21", "\u0F22", "\u0F23", "\u0F24", "\u0F25", "\u0F26", "\u0F27", "\u0F28", "\u0F29"],
        ["tirh"] = ["\uD805\uDCD0", "\uD805\uDCD1", "\uD805\uDCD2", "\uD805\uDCD3", "\uD805\uDCD4", "\uD805\uDCD5", "\uD805\uDCD6", "\uD805\uDCD7", "\uD805\uDCD8", "\uD805\uDCD9"],
        ["tnsa"] = ["\uD81A\uDEC0", "\uD81A\uDEC1", "\uD81A\uDEC2", "\uD81A\uDEC3", "\uD81A\uDEC4", "\uD81A\uDEC5", "\uD81A\uDEC6", "\uD81A\uDEC7", "\uD81A\uDEC8", "\uD81A\uDEC9"],
        ["tols"] = ["\uD807\uDDE0", "\uD807\uDDE1", "\uD807\uDDE2", "\uD807\uDDE3", "\uD807\uDDE4", "\uD807\uDDE5", "\uD807\uDDE6", "\uD807\uDDE7", "\uD807\uDDE8", "\uD807\uDDE9"],
        ["vaii"] = ["\uA620", "\uA621", "\uA622", "\uA623", "\uA624", "\uA625", "\uA626", "\uA627", "\uA628", "\uA629"],
        ["wara"] = ["\uD806\uDCE0", "\uD806\uDCE1", "\uD806\uDCE2", "\uD806\uDCE3", "\uD806\uDCE4", "\uD806\uDCE5", "\uD806\uDCE6", "\uD806\uDCE7", "\uD806\uDCE8", "\uD806\uDCE9"],
        ["wcho"] = ["\uD838\uDEF0", "\uD838\uDEF1", "\uD838\uDEF2", "\uD838\uDEF3", "\uD838\uDEF4", "\uD838\uDEF5", "\uD838\uDEF6", "\uD838\uDEF7", "\uD838\uDEF8", "\uD838\uDEF9"],
    };

    private static string MapDigits(string text, string numberingSystem)
    {
        if (numberingSystem == "latn" || !NumberingSystemDigits.TryGetValue(numberingSystem, out var digits))
        {
            return text;
        }

        var sb = new System.Text.StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is >= '0' and <= '9')
            {
                sb.Append(digits[text[i] - '0']);
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        return sb.ToString();
    }

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
