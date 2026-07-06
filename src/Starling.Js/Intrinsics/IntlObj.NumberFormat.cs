using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-402 Intl.NumberFormat: spec-order option resolution (§15.1),
/// an exact decimal formatting pipeline (§15.5) covering notation, rounding,
/// sign display, currency/unit styles, and the v3 range formatter.</summary>
public static partial class IntlObj
{
    private readonly record struct NumPart(string Type, string Value);

    private sealed record NumberFormatState(
        string LocaleName,
        CultureInfo Culture,
        string NumberingSystem,
        string Style,
        string? Currency,
        string CurrencyDisplay,
        string CurrencySign,
        string? Unit,
        string UnitDisplay,
        int MinIntegerDigits,
        int MinFractionDigits,
        int MaxFractionDigits,
        int MinSignificantDigits,
        int MaxSignificantDigits,
        string RoundingType,
        string Notation,
        string CompactDisplay,
        string UseGrouping,
        string SignDisplay,
        int RoundingIncrement,
        string RoundingMode,
        string TrailingZeroDisplay);

    private sealed class IntlNumberFormatObject(JsObject prototype, NumberFormatState state) : JsObject(prototype)
    {
        public JsObject? BoundFormat;
        public NumberFormatState State { get; } = state;
    }

    private static IntlNumberFormatObject RequireNumberFormat(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsObject && thisV.AsObject is IntlNumberFormatObject obj)
        {
            return obj;
        }

        throw new JsThrow(realm.NewTypeError("Intl.NumberFormat method called on incompatible receiver"));
    }

    /// <summary>GetPrototypeFromConstructor (§10.1.14) for Intl constructors:
    /// when newTarget.prototype is not an object, the fallback prototype comes
    /// from the newTarget FUNCTION's realm, not the running one.</summary>
    private static JsObject IntlPrototypeFor(JsRealm realm, JsValue newTarget, string ctorName, JsObject currentProto)
    {
        if (!newTarget.IsObject || !AbstractOperations.IsConstructor(newTarget))
        {
            return currentProto;
        }

        var p = AbstractOperations.Get(realm.ActiveVm, newTarget.AsObject, "prototype");
        if (p.IsObject)
        {
            return p.AsObject;
        }

        var fnRealm = FunctionRealmOf(newTarget.AsObject);
        if (fnRealm is null || ReferenceEquals(fnRealm, realm))
        {
            return currentProto;
        }

        var intlV = AbstractOperations.Get(realm.ActiveVm, fnRealm.GlobalObject, "Intl");
        if (!intlV.IsObject)
        {
            return currentProto;
        }

        var ctorV = AbstractOperations.Get(realm.ActiveVm, intlV.AsObject, ctorName);
        if (!ctorV.IsObject)
        {
            return currentProto;
        }

        var protoV = AbstractOperations.Get(realm.ActiveVm, ctorV.AsObject, "prototype");
        return protoV.IsObject ? protoV.AsObject : currentProto;
    }

    /// <summary>GetFunctionRealm (§7.3.25), bound functions unwrapped.</summary>
    private static JsRealm? FunctionRealmOf(JsObject fn)
    {
        while (fn is JsBoundFunction bound)
        {
            fn = bound.Target;
        }

        return fn is JsFunction f ? f.Realm : null;
    }

    private static JsNativeFunction CreateNumberFormatCtor(JsRealm realm, JsObject proto)
    {
        var ctor = new JsNativeFunction(realm, "NumberFormat", 0, (newTarget, args) =>
        {
            var state = CreateNumberFormatState(
                realm,
                args.Length > 0 ? args[0] : JsValue.Undefined,
                args.Length > 1 ? args[1] : JsValue.Undefined);
            var instProto = IntlPrototypeFor(realm, newTarget, "NumberFormat", proto);
            return JsValue.Object(new IntlNumberFormatObject(instProto, state));
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
                    (_, args) => JsValue.String(FormatNumberValue(realm, nf.State, args.Length > 0 ? args[0] : JsValue.Undefined)),
                    isConstructor: false);
                return JsValue.Object(nf.BoundFormat);
            }),
            null));
        IntrinsicHelpers.DefineMethod(realm, proto, "formatToParts", 1, (thisV, args) =>
        {
            var nf = RequireNumberFormat(realm, thisV);
            var x = ToIntlMathematicalValue(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var parts = PartitionNumberPattern(nf.State, x);
            var arr = new JsArray(realm);
            for (var i = 0; i < parts.Count; i++)
            {
                arr.Push(MakePart(realm, parts[i].Type, parts[i].Value));
            }

            return JsValue.Object(arr);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "formatRange", 2,
            (thisV, args) => FormatRangeImpl(realm, thisV, args, toParts: false));
        IntrinsicHelpers.DefineMethod(realm, proto, "formatRangeToParts", 2,
            (thisV, args) => FormatRangeImpl(realm, thisV, args, toParts: true));
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0,
            (thisV, _) => NumberFormatResolvedOptions(realm, RequireNumberFormat(realm, thisV).State));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl.NumberFormat"), writable: false, enumerable: false, configurable: true));
        return ctor;
    }

    /// <summary>Number.prototype.toLocaleString (ECMA-402 §19.2.1): format the
    /// receiver with a NumberFormat built from the call's locales/options.</summary>
    internal static string FormatNumberToLocaleString(JsRealm realm, double value, JsValue[] args)
    {
        var state = CreateNumberFormatState(
            realm,
            args.Length > 0 ? args[0] : JsValue.Undefined,
            args.Length > 1 ? args[1] : JsValue.Undefined);
        var parts = PartitionNumberPattern(state, DecimalNum.FromDouble(value));
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            sb.Append(parts[i].Value);
        }

        return sb.ToString();
    }

    private static string FormatNumberValue(JsRealm realm, NumberFormatState st, JsValue value)
    {
        var x = ToIntlMathematicalValue(realm, value);
        var parts = PartitionNumberPattern(st, x);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            sb.Append(parts[i].Value);
        }

        return sb.ToString();
    }

    // =====================================================================
    //                 §15.1 InitializeNumberFormat (option order)
    // =====================================================================

    private static readonly string[] RoundingModeValues =
        ["ceil", "floor", "expand", "trunc", "halfCeil", "halfFloor", "halfExpand", "halfTrunc", "halfEven"];
    private static readonly int[] AllowedRoundingIncrements =
        [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000];

    private static NumberFormatState CreateNumberFormatState(JsRealm realm, JsValue locales, JsValue optionsValue)
    {
        var requested = ReadRequestedLocales(realm, locales);
        JsObject? options;
        if (optionsValue.IsUndefined)
        {
            options = null;
        }
        else
        {
            options = AbstractOperations.ToObject(realm, optionsValue);
        }

        _ = GetOptionEnum(realm, options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var nuOption = GetOptionEnum(realm, options, "numberingSystem", null, null);
        if (nuOption is not null && !IsWellFormedNumberingSystem(nuOption))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid numberingSystem: \"{nuOption}\""));
        }

        var (localeName, culture, nu) = ResolveNumberLocale(requested, nuOption);

        // SetNumberFormatUnitOptions (§15.1.3)
        var style = GetOptionEnum(realm, options, "style", ["decimal", "percent", "currency", "unit"], "decimal")!;
        var currency = GetOptionEnum(realm, options, "currency", null, null);
        if (currency is null)
        {
            if (style == "currency")
            {
                throw new JsThrow(realm.NewTypeError("currency must be provided when style is \"currency\""));
            }
        }
        else if (!IsWellFormedCurrencyCode(currency))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid currency code: \"{currency}\""));
        }

        var currencyDisplay = GetOptionEnum(realm, options, "currencyDisplay", ["code", "symbol", "narrowSymbol", "name"], "symbol")!;
        var currencySign = GetOptionEnum(realm, options, "currencySign", ["standard", "accounting"], "standard")!;
        var unit = GetOptionEnum(realm, options, "unit", null, null);
        if (unit is null)
        {
            if (style == "unit")
            {
                throw new JsThrow(realm.NewTypeError("unit must be provided when style is \"unit\""));
            }
        }
        else if (!IsWellFormedUnitIdentifier(unit))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid unit identifier: \"{unit}\""));
        }

        var unitDisplay = GetOptionEnum(realm, options, "unitDisplay", ["short", "narrow", "long"], "short")!;
        currency = currency?.ToUpperInvariant();

        var notation = GetOptionEnum(realm, options, "notation", ["standard", "scientific", "engineering", "compact"], "standard")!;

        // SetNumberFormatDigitOptions (§15.1.6)
        var mnid = GetNumberOptionSpec(realm, options, "minimumIntegerDigits", 1, 21, 1)!.Value;
        var mnfdRaw = OptGet(realm, options, "minimumFractionDigits");
        var mxfdRaw = OptGet(realm, options, "maximumFractionDigits");
        var mnsdRaw = OptGet(realm, options, "minimumSignificantDigits");
        var mxsdRaw = OptGet(realm, options, "maximumSignificantDigits");
        var roundingIncrement = GetNumberOptionSpec(realm, options, "roundingIncrement", 1, 5000, 1)!.Value;
        if (Array.IndexOf(AllowedRoundingIncrements, roundingIncrement) < 0)
        {
            throw new JsThrow(realm.NewRangeError($"Invalid roundingIncrement: {roundingIncrement}"));
        }

        var roundingMode = GetOptionEnum(realm, options, "roundingMode", RoundingModeValues, "halfExpand")!;
        var roundingPriority = GetOptionEnum(realm, options, "roundingPriority", ["auto", "morePrecision", "lessPrecision"], "auto")!;
        var trailingZeroDisplay = GetOptionEnum(realm, options, "trailingZeroDisplay", ["auto", "stripIfInteger"], "auto")!;

        int mnfdDefault, mxfdDefault;
        if (style == "currency" && notation == "standard")
        {
            var cDigits = CurrencyDigits(currency!);
            mnfdDefault = cDigits;
            mxfdDefault = cDigits;
        }
        else
        {
            mnfdDefault = 0;
            mxfdDefault = style == "percent" ? 0 : 3;
        }

        var hasSd = !(mnsdRaw.IsUndefined && mxsdRaw.IsUndefined);
        var hasFd = !(mnfdRaw.IsUndefined && mxfdRaw.IsUndefined);
        var needSd = true;
        var needFd = true;
        if (roundingPriority == "auto")
        {
            needSd = hasSd;
            needFd = !(needSd || (!hasFd && notation == "compact"));
        }

        var mnsd = 0;
        var mxsd = 0;
        var mnfd = 0;
        var mxfd = 0;
        if (needSd)
        {
            mnsd = DefaultNumberOption(realm, mnsdRaw, 1, 21, 1, "minimumSignificantDigits")!.Value;
            mxsd = DefaultNumberOption(realm, mxsdRaw, mnsd, 21, 21, "maximumSignificantDigits")!.Value;
        }

        if (needFd)
        {
            if (hasFd)
            {
                var mnfdOpt = DefaultNumberOption(realm, mnfdRaw, 0, 100, null, "minimumFractionDigits");
                var mxfdOpt = DefaultNumberOption(realm, mxfdRaw, 0, 100, null, "maximumFractionDigits");
                if (mnfdOpt is null)
                {
                    mnfd = Math.Min(mnfdDefault, mxfdOpt!.Value);
                    mxfd = mxfdOpt.Value;
                }
                else if (mxfdOpt is null)
                {
                    mnfd = mnfdOpt.Value;
                    mxfd = Math.Max(mxfdDefault, mnfd);
                }
                else if (mnfdOpt.Value > mxfdOpt.Value)
                {
                    throw new JsThrow(realm.NewRangeError("minimumFractionDigits is greater than maximumFractionDigits"));
                }
                else
                {
                    mnfd = mnfdOpt.Value;
                    mxfd = mxfdOpt.Value;
                }
            }
            else
            {
                mnfd = mnfdDefault;
                mxfd = mxfdDefault;
            }
        }

        string roundingType;
        if (!needSd && !needFd)
        {
            mnfd = 0;
            mxfd = 0;
            mnsd = 1;
            mxsd = 2;
            roundingType = "morePrecision";
        }
        else if (roundingPriority == "morePrecision" || roundingPriority == "lessPrecision")
        {
            roundingType = roundingPriority;
        }
        else if (hasSd)
        {
            roundingType = "significantDigits";
        }
        else
        {
            roundingType = "fractionDigits";
        }

        if (roundingIncrement != 1)
        {
            if (roundingType != "fractionDigits")
            {
                throw new JsThrow(realm.NewTypeError("roundingIncrement requires fraction-digits rounding"));
            }

            if (mxfd != mnfd)
            {
                throw new JsThrow(realm.NewRangeError("roundingIncrement requires maximumFractionDigits to equal minimumFractionDigits"));
            }
        }

        var compactDisplay = GetOptionEnum(realm, options, "compactDisplay", ["short", "long"], "short")!;
        var defaultUseGrouping = notation == "compact" ? "min2" : "auto";
        var useGrouping = GetUseGroupingOption(realm, options, defaultUseGrouping);
        var signDisplay = GetOptionEnum(realm, options, "signDisplay", ["auto", "never", "always", "exceptZero", "negative"], "auto")!;

        return new NumberFormatState(
            localeName, culture, nu, style, currency, currencyDisplay, currencySign, unit, unitDisplay,
            mnid, mnfd, mxfd, mnsd, mxsd, roundingType, notation, compactDisplay, useGrouping, signDisplay,
            roundingIncrement, roundingMode, trailingZeroDisplay);
    }

    private static JsValue OptGet(JsRealm realm, JsObject? options, string name)
        => options is null ? JsValue.Undefined : AbstractOperations.Get(realm.ActiveVm, options, name);

    private static string? GetOptionEnum(JsRealm realm, JsObject? options, string name, string[]? allowed, string? fallback)
    {
        var value = OptGet(realm, options, name);
        if (value.IsUndefined)
        {
            return fallback;
        }

        var text = AbstractOperations.ToStringJs(realm.ActiveVm, value);
        if (allowed is not null && Array.IndexOf(allowed, text) < 0)
        {
            throw new JsThrow(realm.NewRangeError($"Value \"{text}\" out of range for option \"{name}\""));
        }

        return text;
    }

    private static int? GetNumberOptionSpec(JsRealm realm, JsObject? options, string name, int min, int max, int? fallback)
        => DefaultNumberOption(realm, OptGet(realm, options, name), min, max, fallback, name);

    private static int? DefaultNumberOption(JsRealm realm, JsValue value, int min, int max, int? fallback, string name)
    {
        if (value.IsUndefined)
        {
            return fallback;
        }

        if (value.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError($"Cannot convert a Symbol value to a number for option \"{name}\""));
        }

        var number = NumberCtor.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm, value, "number"));
        if (double.IsNaN(number) || number < min || number > max)
        {
            throw new JsThrow(realm.NewRangeError($"Value out of range for option \"{name}\""));
        }

        return (int)Math.Floor(number);
    }

    private static string GetUseGroupingOption(JsRealm realm, JsObject? options, string fallback)
    {
        var value = OptGet(realm, options, "useGrouping");
        if (value.IsUndefined)
        {
            return fallback;
        }

        if (value.IsBoolean && JsValue.ToBoolean(value))
        {
            return "always";
        }

        if (!JsValue.ToBoolean(value))
        {
            return "false";
        }

        var text = AbstractOperations.ToStringJs(realm.ActiveVm, value);
        if (text is "true" or "false")
        {
            return fallback;
        }

        if (text is not ("min2" or "auto" or "always"))
        {
            throw new JsThrow(realm.NewRangeError($"Value \"{text}\" out of range for option \"useGrouping\""));
        }

        return text;
    }

    private static bool IsWellFormedNumberingSystem(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var seg in value.Split('-'))
        {
            if (seg.Length is < 3 or > 8 || !IsAsciiAlnum(seg))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWellFormedCurrencyCode(string value)
        => value.Length == 3 && IsAsciiLetters(value);

    private static readonly HashSet<string> SanctionedUnits = new(StringComparer.Ordinal)
    {
        "acre", "bit", "byte", "celsius", "centimeter", "day", "degree", "fahrenheit", "fluid-ounce",
        "foot", "gallon", "gigabit", "gigabyte", "gram", "hectare", "hour", "inch", "kilobit",
        "kilobyte", "kilogram", "kilometer", "liter", "megabit", "megabyte", "meter", "microsecond",
        "mile", "mile-scandinavian", "milliliter", "millimeter", "millisecond", "minute", "month",
        "nanosecond", "ounce", "percent", "petabyte", "pound", "second", "stone", "terabit",
        "terabyte", "week", "yard", "year",
    };

    private static bool IsWellFormedUnitIdentifier(string value)
    {
        if (SanctionedUnits.Contains(value))
        {
            return true;
        }

        var idx = value.IndexOf("-per-", StringComparison.Ordinal);
        if (idx <= 0)
        {
            return false;
        }

        var numerator = value[..idx];
        var denominator = value[(idx + 5)..];
        return SanctionedUnits.Contains(numerator) && SanctionedUnits.Contains(denominator);
    }

    private static int CurrencyDigits(string code) => code switch
    {
        "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or "RWF"
            or "UGX" or "UYI" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
        "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
        _ => 2,
    };

    private static bool IsAsciiAlnum(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static (string Name, CultureInfo Culture, string Nu) ResolveNumberLocale(List<string> requested, string? nuOption)
    {
        var baseName = DefaultLocale;
        var culture = CultureInfo.InvariantCulture;
        if (TryCreateLocale(DefaultLocale, out var defaultLocale))
        {
            culture = defaultLocale.Culture;
        }

        string? extNu = null;
        foreach (var tag in requested)
        {
            if (!TryCreateLocale(tag, out var locale))
            {
                continue;
            }

            baseName = StripExtensions(locale.Name);
            culture = locale.Culture;
            extNu = ExtensionValue(tag, "nu");
            break;
        }

        var extSupported = extNu is not null && NumberingSystemDigits.ContainsKey(extNu);
        string nu;
        bool reflectExt;
        if (nuOption is not null && NumberingSystemDigits.ContainsKey(nuOption))
        {
            nu = nuOption;
            reflectExt = extSupported && string.Equals(extNu, nuOption, StringComparison.Ordinal);
        }
        else if (extSupported)
        {
            nu = extNu!;
            reflectExt = true;
        }
        else
        {
            nu = DefaultNumberingSystem;
            reflectExt = false;
        }

        var name = reflectExt ? baseName + "-u-nu-" + nu : baseName;
        return (name, culture, nu);
    }

    private static string StripExtensions(string tag)
    {
        var idx = tag.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? tag[..idx] : tag;
    }

    /// <summary>Reads a Unicode-extension keyword value ("-u-..-key-value")
    /// from a raw requested tag; null when absent or empty.</summary>
    private static string? ExtensionValue(string tag, string key)
    {
        var norm = tag.Replace('_', '-');
        var uIdx = norm.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (uIdx < 0)
        {
            return null;
        }

        var parts = norm[(uIdx + 3)..].Split('-');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 1)
            {
                break;
            }

            if (string.Equals(parts[i], key, StringComparison.OrdinalIgnoreCase))
            {
                var values = new List<string>();
                for (var j = i + 1; j < parts.Length && parts[j].Length > 2; j++)
                {
                    values.Add(parts[j].ToLowerInvariant());
                }

                return values.Count == 0 ? null : string.Join('-', values);
            }
        }

        return null;
    }
}
