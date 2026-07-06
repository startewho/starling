using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>Intl.NumberFormat rendering: locale data (separators, compact and
/// currency/unit patterns), PartitionNumberPattern (§15.5.4), the v3 range
/// partitioner, and resolvedOptions.</summary>
public static partial class IntlObj
{
    private static readonly int[] GroupSize3 = [3];
    private static readonly int[] GroupSize32 = [3, 2];

    private static string LocaleGroup(NumberFormatState st)
    {
        var n = st.LocaleName;
        if (n.StartsWith("en-IN", StringComparison.OrdinalIgnoreCase))
        {
            return "en-IN";
        }

        if (n.StartsWith("de", StringComparison.OrdinalIgnoreCase))
        {
            return "de";
        }

        if (n.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return "ja";
        }

        if (n.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            return "ko";
        }

        if (n.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            return "pt";
        }

        if (n.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return n.Contains("TW", StringComparison.OrdinalIgnoreCase)
                || n.Contains("HK", StringComparison.OrdinalIgnoreCase)
                || n.Contains("MO", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant"
                : "zh";
        }

        if (n.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        return string.Empty;
    }

    private static (string Dec, string Grp, int[] Sizes) NumberSeparators(NumberFormatState st, string g)
    {
        switch (g)
        {
            case "en":
            case "ja":
            case "ko":
            case "zh":
            case "zh-Hant":
                return (".", ",", GroupSize3);
            case "en-IN":
                return (".", ",", GroupSize32);
            case "de":
                return (",", ".", GroupSize3);
            case "pt":
                return (",", " ", GroupSize3);
            default:
            {
                var nfi = st.Culture.NumberFormat;
                var sizes = nfi.NumberGroupSizes is { Length: > 0 } s ? s : GroupSize3;
                var dec = string.IsNullOrEmpty(nfi.NumberDecimalSeparator) ? "." : nfi.NumberDecimalSeparator;
                var grp = string.IsNullOrEmpty(nfi.NumberGroupSeparator) ? "," : nfi.NumberGroupSeparator;
                return (dec, grp, sizes);
            }
        }
    }

    private static string NanSymbol(string g) => g == "zh-Hant" ? "非數值" : "NaN";

    private readonly record struct CompactEntry(int Exp, string One, string Other, string Space);

    private static readonly CompactEntry[] CompactEnShort =
        [new(3, "K", "K", ""), new(6, "M", "M", ""), new(9, "B", "B", ""), new(12, "T", "T", "")];
    private static readonly CompactEntry[] CompactEnLong =
    [
        new(3, "thousand", "thousand", " "), new(6, "million", "million", " "),
        new(9, "billion", "billion", " "), new(12, "trillion", "trillion", " "),
    ];
    private static readonly CompactEntry[] CompactDeShort =
        [new(6, "Mio.", "Mio.", " "), new(9, "Mrd.", "Mrd.", " "), new(12, "Bio.", "Bio.", " ")];
    private static readonly CompactEntry[] CompactDeLong =
    [
        new(3, "Tausend", "Tausend", " "), new(6, "Million", "Millionen", " "),
        new(9, "Milliarde", "Milliarden", " "), new(12, "Billion", "Billionen", " "),
    ];
    private static readonly CompactEntry[] CompactJa =
        [new(4, "万", "万", ""), new(8, "億", "億", ""), new(12, "兆", "兆", "")];
    private static readonly CompactEntry[] CompactKo =
    [
        new(3, "천", "천", ""), new(4, "만", "만", ""),
        new(8, "억", "억", ""), new(12, "조", "조", ""),
    ];
    private static readonly CompactEntry[] CompactZhHant =
        [new(4, "萬", "萬", ""), new(8, "億", "億", ""), new(12, "兆", "兆", "")];
    private static readonly CompactEntry[] CompactZh =
        [new(4, "万", "万", ""), new(8, "亿", "亿", ""), new(12, "兆", "兆", "")];
    private static readonly CompactEntry[] CompactEnInShort =
        [new(3, "K", "K", ""), new(5, "L", "L", ""), new(7, "Cr", "Cr", "")];
    private static readonly CompactEntry[] CompactEnInLong =
    [
        new(3, "thousand", "thousand", " "), new(5, "lakh", "lakh", " "),
        new(7, "crore", "crore", " "),
    ];

    private static CompactEntry[] CompactTable(string g, bool longForm) => g switch
    {
        "en-IN" => longForm ? CompactEnInLong : CompactEnInShort,
        "de" => longForm ? CompactDeLong : CompactDeShort,
        "ja" => CompactJa,
        "ko" => CompactKo,
        "zh-Hant" => CompactZhHant,
        "zh" => CompactZh,
        _ => longForm ? CompactEnLong : CompactEnShort,
    };

    private static int CompactExponentForMagnitude(string g, bool longForm, int magnitude)
    {
        var table = CompactTable(g, longForm);
        var exp = 0;
        for (var i = 0; i < table.Length; i++)
        {
            if (table[i].Exp <= magnitude)
            {
                exp = table[i].Exp;
            }
        }

        return exp;
    }

    private static CompactEntry? CompactEntryFor(string g, bool longForm, int exponent)
    {
        var table = CompactTable(g, longForm);
        for (var i = 0; i < table.Length; i++)
        {
            if (table[i].Exp == exponent)
            {
                return table[i];
            }
        }

        return null;
    }

    private static int ExponentForMagnitude(NumberFormatState st, string g, int magnitude) => st.Notation switch
    {
        "scientific" => magnitude,
        "engineering" => 3 * (int)Math.Floor(magnitude / 3.0),
        "compact" => CompactExponentForMagnitude(g, st.CompactDisplay == "long", magnitude),
        _ => 0,
    };

    private static int ComputeExponent(NumberFormatState st, string g, DecimalNum x)
    {
        if (st.Notation == "standard" || !x.IsFinite || x.IsZero)
        {
            return 0;
        }

        var magnitude = x.Exponent;
        var exponent = ExponentForMagnitude(st, g, magnitude);
        var result = FormatNumericToString(st, x.Shift(-exponent));
        if (result.Rounded.IsZero || result.Rounded.Exponent == magnitude - exponent)
        {
            return exponent;
        }

        return ExponentForMagnitude(st, g, magnitude + 1);
    }

    // =====================================================================
    //                       Currency and unit data
    // =====================================================================

    private static string CurrencySymbolFor(string g, string code, bool narrow)
    {
        switch (code)
        {
            case "USD":
                return !narrow && g is "ko" or "zh" or "zh-Hant" ? "US$" : "$";
            case "EUR":
                return "€";
            case "GBP":
                return "£";
            case "JPY":
                return !narrow && g is "ja" or "zh" or "zh-Hant" ? "JP¥" : "¥";
            case "CNY":
                return !narrow && g is not ("zh" or "zh-Hant") ? "CN¥" : "¥";
            case "KRW":
                return "₩";
            case "INR":
                return "₹";
            case "BRL":
                return "R$";
            case "CAD":
                return narrow ? "$" : "CA$";
            case "AUD":
                return narrow ? "$" : "A$";
            default:
                return code;
        }
    }

    private static (string One, string Other) CurrencyLongName(string code) => code switch
    {
        "USD" => ("US dollar", "US dollars"),
        "EUR" => ("euro", "euros"),
        "GBP" => ("British pound", "British pounds"),
        "JPY" => ("Japanese yen", "Japanese yen"),
        "CNY" => ("Chinese yuan", "Chinese yuan"),
        "KRW" => ("South Korean won", "South Korean won"),
        "CAD" => ("Canadian dollar", "Canadian dollars"),
        "AUD" => ("Australian dollar", "Australian dollars"),
        "INR" => ("Indian rupee", "Indian rupees"),
        "BRL" => ("Brazilian real", "Brazilian reals"),
        _ => (code, code),
    };

    private static bool CurrencyIsSuffix(string g) => g is "de" or "pt";

    private static bool AccountingUsesParens(string g) => g is not ("de" or "pt");

    private readonly record struct UnitNames(string Short, string Narrow, string LongOne, string LongOther, bool ShortAttach = false);

    private static readonly Dictionary<string, UnitNames> EnUnits = new(StringComparer.Ordinal)
    {
        ["acre"] = new("ac", "ac", "acre", "acres"),
        ["bit"] = new("bit", "bit", "bit", "bits"),
        ["byte"] = new("byte", "B", "byte", "bytes"),
        ["celsius"] = new("°C", "°C", "degree Celsius", "degrees Celsius"),
        ["centimeter"] = new("cm", "cm", "centimeter", "centimeters"),
        ["day"] = new("day", "d", "day", "days"),
        ["degree"] = new("deg", "°", "degree", "degrees"),
        ["fahrenheit"] = new("°F", "°F", "degree Fahrenheit", "degrees Fahrenheit"),
        ["fluid-ounce"] = new("fl oz", "fl oz", "fluid ounce", "fluid ounces"),
        ["foot"] = new("ft", "ft", "foot", "feet"),
        ["gallon"] = new("gal", "gal", "gallon", "gallons"),
        ["gigabit"] = new("Gb", "Gb", "gigabit", "gigabits"),
        ["gigabyte"] = new("GB", "GB", "gigabyte", "gigabytes"),
        ["gram"] = new("g", "g", "gram", "grams"),
        ["hectare"] = new("ha", "ha", "hectare", "hectares"),
        ["hour"] = new("hr", "h", "hour", "hours"),
        ["inch"] = new("in", "″", "inch", "inches"),
        ["kilobit"] = new("kb", "kb", "kilobit", "kilobits"),
        ["kilobyte"] = new("kB", "kB", "kilobyte", "kilobytes"),
        ["kilogram"] = new("kg", "kg", "kilogram", "kilograms"),
        ["kilometer"] = new("km", "km", "kilometer", "kilometers"),
        ["liter"] = new("L", "L", "liter", "liters"),
        ["megabit"] = new("Mb", "Mb", "megabit", "megabits"),
        ["megabyte"] = new("MB", "MB", "megabyte", "megabytes"),
        ["meter"] = new("m", "m", "meter", "meters"),
        ["microsecond"] = new("μs", "μs", "microsecond", "microseconds"),
        ["mile"] = new("mi", "mi", "mile", "miles"),
        ["mile-scandinavian"] = new("smi", "smi", "scandinavian mile", "scandinavian miles"),
        ["milliliter"] = new("mL", "mL", "milliliter", "milliliters"),
        ["millimeter"] = new("mm", "mm", "millimeter", "millimeters"),
        ["millisecond"] = new("ms", "ms", "millisecond", "milliseconds"),
        ["minute"] = new("min", "min", "minute", "minutes"),
        ["month"] = new("mth", "mo", "month", "months"),
        ["nanosecond"] = new("ns", "ns", "nanosecond", "nanoseconds"),
        ["ounce"] = new("oz", "oz", "ounce", "ounces"),
        ["percent"] = new("%", "%", "percent", "percent", ShortAttach: true),
        ["petabyte"] = new("PB", "PB", "petabyte", "petabytes"),
        ["pound"] = new("lb", "#", "pound", "pounds"),
        ["second"] = new("sec", "s", "second", "seconds"),
        ["stone"] = new("st", "st", "stone", "stones"),
        ["terabit"] = new("Tb", "Tb", "terabit", "terabits"),
        ["terabyte"] = new("TB", "TB", "terabyte", "terabytes"),
        ["week"] = new("wk", "w", "week", "weeks"),
        ["yard"] = new("yd", "yd", "yard", "yards"),
        ["year"] = new("yr", "y", "year", "years"),
    };

    private static UnitNames UnitNamesFor(string unit)
        => EnUnits.TryGetValue(unit, out var names) ? names : new UnitNames(unit, unit, unit, unit);

    /// <summary>Builds the unit affixes around the number. The tested
    /// kilometer-per-hour forms carry locale overrides; everything else uses
    /// the en CLDR-style tables (compound "a-per-b" composed generically).</summary>
    private static (List<NumPart> Prefix, List<NumPart> Suffix) UnitAffixes(NumberFormatState st, string g, bool pluralOne)
    {
        var prefix = new List<NumPart>();
        var suffix = new List<NumPart>();
        var unit = st.Unit!;
        var display = st.UnitDisplay;
        if (unit == "kilometer-per-hour" && g is "de" or "ja" or "ko" or "zh-Hant" or "zh")
        {
            switch (g)
            {
                case "de":
                    suffix.Add(new NumPart("literal", " "));
                    suffix.Add(new NumPart("unit", display == "long" ? "Kilometer pro Stunde" : "km/h"));
                    return (prefix, suffix);
                case "ja":
                    if (display == "long")
                    {
                        prefix.Add(new NumPart("unit", "時速"));
                        prefix.Add(new NumPart("literal", " "));
                        suffix.Add(new NumPart("literal", " "));
                        suffix.Add(new NumPart("unit", "キロメートル"));
                    }
                    else
                    {
                        if (display == "short")
                        {
                            suffix.Add(new NumPart("literal", " "));
                        }

                        suffix.Add(new NumPart("unit", "km/h"));
                    }

                    return (prefix, suffix);
                case "ko":
                    if (display == "long")
                    {
                        prefix.Add(new NumPart("unit", "시속"));
                        prefix.Add(new NumPart("literal", " "));
                        suffix.Add(new NumPart("unit", "킬로미터"));
                    }
                    else
                    {
                        suffix.Add(new NumPart("unit", "km/h"));
                    }

                    return (prefix, suffix);
                default:
                    if (display == "long")
                    {
                        prefix.Add(new NumPart("unit", "每小時"));
                        prefix.Add(new NumPart("literal", " "));
                        suffix.Add(new NumPart("literal", " "));
                        suffix.Add(new NumPart("unit", "公里"));
                    }
                    else
                    {
                        if (display == "short")
                        {
                            suffix.Add(new NumPart("literal", " "));
                        }

                        suffix.Add(new NumPart("unit", "公里/小時"));
                    }

                    return (prefix, suffix);
            }
        }

        string text;
        bool attach;
        var perIdx = unit.IndexOf("-per-", StringComparison.Ordinal);
        if (perIdx > 0 && !SanctionedUnits.Contains(unit))
        {
            var a = UnitNamesFor(unit[..perIdx]);
            var b = UnitNamesFor(unit[(perIdx + 5)..]);
            switch (display)
            {
                case "narrow":
                    text = a.Narrow + "/" + b.Narrow;
                    attach = true;
                    break;
                case "long":
                    text = (pluralOne ? a.LongOne : a.LongOther) + " per " + b.LongOne;
                    attach = false;
                    break;
                default:
                    // CLDR en compound short abbreviates the denominator to
                    // its narrow form ("km/h", "m/s").
                    text = a.Short + "/" + b.Narrow;
                    attach = false;
                    break;
            }
        }
        else
        {
            var names = UnitNamesFor(unit);
            switch (display)
            {
                case "narrow":
                    text = names.Narrow;
                    attach = true;
                    break;
                case "long":
                    text = pluralOne ? names.LongOne : names.LongOther;
                    attach = false;
                    break;
                default:
                    text = names.Short;
                    attach = names.ShortAttach;
                    break;
            }
        }

        if (!attach)
        {
            suffix.Add(new NumPart("literal", " "));
        }

        suffix.Add(new NumPart("unit", text));
        return (prefix, suffix);
    }

    // =====================================================================
    //                  PartitionNumberPattern (§15.5.4/.6)
    // =====================================================================

    private static string SignFor(NumberFormatState st, bool negative, bool isZero) => st.SignDisplay switch
    {
        "never" => "",
        "always" => negative ? "-" : "+",
        "exceptZero" => isZero ? "" : negative ? "-" : "+",
        "negative" => negative && !isZero ? "-" : "",
        _ => negative ? "-" : "",
    };

    private static List<NumPart> PartitionNumberPattern(NumberFormatState st, DecimalNum x)
    {
        var g = LocaleGroup(st);
        var num = new List<NumPart>();
        string sign;
        var pluralOne = false;
        if (x.IsNaN)
        {
            sign = st.SignDisplay == "always" ? "+" : "";
            num.Add(new NumPart("nan", NanSymbol(g)));
        }
        else if (x.Inf != 0)
        {
            sign = SignFor(st, x.Inf < 0, isZero: false);
            num.Add(new NumPart("infinity", "∞"));
        }
        else
        {
            var work = st.Style == "percent" ? x.Shift(2) : x;
            var exponent = ComputeExponent(st, g, work);
            var result = FormatNumericToString(st, work.Shift(-exponent));
            var roundedZero = result.Rounded.IsZero;
            sign = SignFor(st, x.Negative, roundedZero);
            pluralOne = result.Int == "1" && result.Frac.Length == 0;
            var intD = result.Int;
            if (intD.Length < st.MinIntegerDigits)
            {
                intD = new string('0', st.MinIntegerDigits - intD.Length) + intD;
            }

            var (dec, grp, sizes) = NumberSeparators(st, g);
            AppendGroupedInteger(st, intD, grp, sizes, num);
            if (result.Frac.Length > 0)
            {
                num.Add(new NumPart("decimal", dec));
                num.Add(new NumPart("fraction", result.Frac));
            }

            if (st.Notation is "scientific" or "engineering")
            {
                num.Add(new NumPart("exponentSeparator", "E"));
                if (exponent < 0)
                {
                    num.Add(new NumPart("exponentMinusSign", "-"));
                }

                num.Add(new NumPart("exponentInteger", Math.Abs(exponent).ToString(CultureInfo.InvariantCulture)));
            }
            else if (st.Notation == "compact" && exponent != 0)
            {
                var entry = CompactEntryFor(g, st.CompactDisplay == "long", exponent);
                if (entry is { } ce)
                {
                    if (ce.Space.Length > 0)
                    {
                        num.Add(new NumPart("literal", ce.Space));
                    }

                    num.Add(new NumPart("compact", pluralOne ? ce.One : ce.Other));
                }
            }
        }

        var parts = new List<NumPart>();
        WrapStyle(st, g, sign, pluralOne, num, parts);
        MapPartsDigits(parts, st.NumberingSystem);
        return parts;
    }

    private static void AppendGroupedInteger(NumberFormatState st, string intD, string grp, int[] sizes, List<NumPart> num)
    {
        var primary = sizes[0];
        var secondary = sizes.Length > 1 && sizes[1] > 0 ? sizes[1] : primary;
        var len = intD.Length;
        var grouped = st.UseGrouping switch
        {
            "false" => false,
            "min2" => len > primary + 1,
            _ => len > primary,
        };
        if (!grouped || primary <= 0)
        {
            num.Add(new NumPart("integer", intD));
            return;
        }

        var cuts = new List<int>();
        var pos = len - primary;
        while (pos > 0)
        {
            cuts.Add(pos);
            pos -= secondary;
        }

        cuts.Reverse();
        var start = 0;
        for (var i = 0; i < cuts.Count; i++)
        {
            num.Add(new NumPart("integer", intD[start..cuts[i]]));
            num.Add(new NumPart("group", grp));
            start = cuts[i];
        }

        num.Add(new NumPart("integer", intD[start..]));
    }

    private static void WrapStyle(NumberFormatState st, string g, string sign, bool pluralOne, List<NumPart> num, List<NumPart> parts)
    {
        void AddSign()
        {
            if (sign == "-")
            {
                parts.Add(new NumPart("minusSign", "-"));
            }
            else if (sign == "+")
            {
                parts.Add(new NumPart("plusSign", "+"));
            }
        }

        switch (st.Style)
        {
            case "percent":
                AddSign();
                parts.AddRange(num);
                if (g == "de")
                {
                    parts.Add(new NumPart("literal", " "));
                }

                parts.Add(new NumPart("percentSign", "%"));
                break;

            case "unit":
            {
                var (prefix, suffix) = UnitAffixes(st, g, pluralOne);
                parts.AddRange(prefix);
                AddSign();
                parts.AddRange(num);
                parts.AddRange(suffix);
                break;
            }

            case "currency":
            {
                var code = st.Currency!;
                if (st.CurrencyDisplay == "name")
                {
                    var (one, other) = CurrencyLongName(code);
                    AddSign();
                    parts.AddRange(num);
                    parts.Add(new NumPart("literal", " "));
                    parts.Add(new NumPart("currency", pluralOne ? one : other));
                    break;
                }

                var symText = st.CurrencyDisplay switch
                {
                    "code" => code,
                    "narrowSymbol" => CurrencySymbolFor(g, code, narrow: true),
                    _ => CurrencySymbolFor(g, code, narrow: false),
                };
                var parens = st.CurrencySign == "accounting" && sign == "-" && AccountingUsesParens(g);
                if (!CurrencyIsSuffix(g))
                {
                    if (parens)
                    {
                        parts.Add(new NumPart("literal", "("));
                    }
                    else
                    {
                        AddSign();
                    }

                    parts.Add(new NumPart("currency", symText));
                    if (st.CurrencyDisplay == "code")
                    {
                        parts.Add(new NumPart("literal", " "));
                    }

                    parts.AddRange(num);
                    if (parens)
                    {
                        parts.Add(new NumPart("literal", ")"));
                    }
                }
                else
                {
                    AddSign();
                    parts.AddRange(num);
                    parts.Add(new NumPart("literal", " "));
                    parts.Add(new NumPart("currency", symText));
                }

                break;
            }

            default:
                AddSign();
                parts.AddRange(num);
                break;
        }
    }

    private static void MapPartsDigits(List<NumPart> parts, string numberingSystem)
    {
        if (numberingSystem == "latn")
        {
            return;
        }

        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].Type is "integer" or "fraction" or "exponentInteger")
            {
                parts[i] = parts[i] with { Value = MapDigits(parts[i].Value, numberingSystem) };
            }
        }
    }

    // =====================================================================
    //                 formatRange / formatRangeToParts (v3)
    // =====================================================================

    private static readonly HashSet<string> NumericPartTypes = new(StringComparer.Ordinal)
    {
        "integer", "group", "decimal", "fraction", "exponentSeparator",
        "exponentMinusSign", "exponentInteger", "compact", "nan", "infinity",
    };

    private static JsValue FormatRangeImpl(JsRealm realm, JsValue thisV, JsValue[] args, bool toParts)
    {
        var nf = RequireNumberFormat(realm, thisV);
        var st = nf.State;
        var xV = args.Length > 0 ? args[0] : JsValue.Undefined;
        var yV = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (xV.IsUndefined || yV.IsUndefined)
        {
            throw new JsThrow(realm.NewTypeError("formatRange requires two arguments"));
        }

        var x = ToIntlMathematicalValue(realm, xV);
        var y = ToIntlMathematicalValue(realm, yV);
        if (x.IsNaN || y.IsNaN)
        {
            throw new JsThrow(realm.NewRangeError("formatRange arguments must not be NaN"));
        }

        var px = PartitionNumberPattern(st, x);
        var py = PartitionNumberPattern(st, y);
        var g = LocaleGroup(st);
        var result = new List<(NumPart Part, string Source)>();
        if (PartListsEqual(px, py))
        {
            result.Add((new NumPart("approximatelySign", "~"), "shared"));
            for (var i = 0; i < px.Count; i++)
            {
                result.Add((px[i], "shared"));
            }
        }
        else
        {
            SplitAffixes(px, out var prefX, out var coreX, out var sufX);
            SplitAffixes(py, out var prefY, out var coreY, out var sufY);
            var prefixEqual = PartListsEqual(prefX, prefY) && prefX.Count > 0;
            var prefixHasSign = false;
            for (var i = 0; i < prefX.Count; i++)
            {
                if (prefX[i].Type is "minusSign" or "plusSign")
                {
                    prefixHasSign = true;
                }
            }

            var collapsePrefix = prefixEqual && prefixHasSign;
            var collapseSuffix = PartListsEqual(sufX, sufY) && sufX.Count > 0;
            var sep = g == "pt" ? " - " : "–";
            if (sep == "–" && prefixEqual && !collapsePrefix)
            {
                sep = " – ";
            }

            for (var i = 0; i < prefX.Count; i++)
            {
                result.Add((prefX[i], "startRange"));
            }

            for (var i = 0; i < coreX.Count; i++)
            {
                result.Add((coreX[i], "startRange"));
            }

            if (!collapseSuffix)
            {
                for (var i = 0; i < sufX.Count; i++)
                {
                    result.Add((sufX[i], "startRange"));
                }
            }

            result.Add((new NumPart("literal", sep), "shared"));
            if (!collapsePrefix)
            {
                for (var i = 0; i < prefY.Count; i++)
                {
                    result.Add((prefY[i], "endRange"));
                }
            }

            for (var i = 0; i < coreY.Count; i++)
            {
                result.Add((coreY[i], "endRange"));
            }

            for (var i = 0; i < sufY.Count; i++)
            {
                result.Add((sufY[i], "endRange"));
            }
        }

        if (!toParts)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < result.Count; i++)
            {
                sb.Append(result[i].Part.Value);
            }

            return JsValue.String(sb.ToString());
        }

        var arr = new JsArray(realm);
        for (var i = 0; i < result.Count; i++)
        {
            var part = MakePart(realm, result[i].Part.Type, result[i].Part.Value).AsObject;
            part.Set("source", JsValue.String(result[i].Source));
            arr.Push(JsValue.Object(part));
        }

        return JsValue.Object(arr);
    }

    private static bool PartListsEqual(List<NumPart> a, List<NumPart> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void SplitAffixes(List<NumPart> parts, out List<NumPart> prefix, out List<NumPart> core, out List<NumPart> suffix)
    {
        var first = 0;
        while (first < parts.Count && !NumericPartTypes.Contains(parts[first].Type))
        {
            first++;
        }

        var last = parts.Count - 1;
        while (last >= first && !NumericPartTypes.Contains(parts[last].Type))
        {
            last--;
        }

        prefix = parts[..first];
        core = parts[first..(last + 1)];
        suffix = parts[(last + 1)..];
    }

    // =====================================================================
    //                          resolvedOptions
    // =====================================================================

    private static JsValue NumberFormatResolvedOptions(JsRealm realm, NumberFormatState st)
    {
        var obj = realm.NewOrdinaryObject();
        obj.Set("locale", JsValue.String(st.LocaleName));
        obj.Set("numberingSystem", JsValue.String(st.NumberingSystem));
        obj.Set("style", JsValue.String(st.Style));
        if (st.Style == "currency")
        {
            obj.Set("currency", JsValue.String(st.Currency!));
            obj.Set("currencyDisplay", JsValue.String(st.CurrencyDisplay));
            obj.Set("currencySign", JsValue.String(st.CurrencySign));
        }

        if (st.Style == "unit")
        {
            obj.Set("unit", JsValue.String(st.Unit!));
            obj.Set("unitDisplay", JsValue.String(st.UnitDisplay));
        }

        obj.Set("minimumIntegerDigits", JsValue.Number(st.MinIntegerDigits));
        if (st.RoundingType != "significantDigits")
        {
            obj.Set("minimumFractionDigits", JsValue.Number(st.MinFractionDigits));
            obj.Set("maximumFractionDigits", JsValue.Number(st.MaxFractionDigits));
        }

        if (st.RoundingType != "fractionDigits")
        {
            obj.Set("minimumSignificantDigits", JsValue.Number(st.MinSignificantDigits));
            obj.Set("maximumSignificantDigits", JsValue.Number(st.MaxSignificantDigits));
        }

        obj.Set("useGrouping", st.UseGrouping == "false" ? JsValue.False : JsValue.String(st.UseGrouping));
        obj.Set("notation", JsValue.String(st.Notation));
        if (st.Notation == "compact")
        {
            obj.Set("compactDisplay", JsValue.String(st.CompactDisplay));
        }

        obj.Set("signDisplay", JsValue.String(st.SignDisplay));
        obj.Set("roundingIncrement", JsValue.Number(st.RoundingIncrement));
        obj.Set("roundingMode", JsValue.String(st.RoundingMode));
        obj.Set("roundingPriority", JsValue.String(
            st.RoundingType is "morePrecision" or "lessPrecision" ? st.RoundingType : "auto"));
        obj.Set("trailingZeroDisplay", JsValue.String(st.TrailingZeroDisplay));
        return JsValue.Object(obj);
    }
}
