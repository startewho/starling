using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-402 Intl.DateTimeFormat: spec-order option resolution
/// (§11.1.2 CreateDateTimeFormat), a parts-native field formatter over
/// carried locale data (the build runs with invariant globalization), offset
/// and IANA time zones, and the range formatter with source annotations.</summary>
public static partial class IntlObj
{
    private readonly record struct DtPart(string Type, string Value);

    private sealed record DateTimeFormatState(
        string LocaleName,
        string DataLocale,
        string Calendar,
        string NumberingSystem,
        string TimeZoneId,
        int? FixedOffsetMinutes,
        TimeZoneInfo? NamedZone,
        string HourCycle,
        string? Weekday,
        string? Era,
        string? Year,
        string? Month,
        string? Day,
        string? DayPeriod,
        string? Hour,
        string? Minute,
        string? Second,
        int FractionalSecondDigits,
        string? TimeZoneName,
        string? DateStyle,
        string? TimeStyle);

    private sealed class IntlDateTimeFormatObject(JsObject prototype, DateTimeFormatState state) : JsObject(prototype)
    {
        public JsObject? BoundFormat;
        public DateTimeFormatState State { get; } = state;
    }

    private static IntlDateTimeFormatObject RequireDateTimeFormat(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsObject && thisV.AsObject is IntlDateTimeFormatObject obj)
        {
            return obj;
        }

        throw new JsThrow(realm.NewTypeError("Intl.DateTimeFormat method called on incompatible receiver"));
    }

    private static JsNativeFunction CreateDateTimeFormatCtor(JsRealm realm, JsObject proto)
    {
        var ctor = new JsNativeFunction(realm, "DateTimeFormat", 0, (newTarget, args) =>
        {
            var state = CreateDateTimeFormatState(
                realm,
                args.Length > 0 ? args[0] : JsValue.Undefined,
                args.Length > 1 ? args[1] : JsValue.Undefined,
                "any", "date");
            var instProto = IntlPrototypeFor(realm, newTarget, "DateTimeFormat", proto);
            return JsValue.Object(new IntlDateTimeFormatObject(instProto, state));
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
                dtf.BoundFormat ??= new JsNativeFunction(realm, "", 1, (_, fargs) =>
                {
                    var x = DtfClipDateArg(realm, fargs.Length > 0 ? fargs[0] : JsValue.Undefined);
                    return JsValue.String(DtfJoinParts(PartitionDateTimeParts(dtf.State, x)));
                }, isConstructor: false);
                return JsValue.Object(dtf.BoundFormat);
            }),
            null));
        IntrinsicHelpers.DefineMethod(realm, proto, "formatToParts", 1, (thisV, args) =>
        {
            var dtf = RequireDateTimeFormat(realm, thisV);
            var x = DtfClipDateArg(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var parts = PartitionDateTimeParts(dtf.State, x);
            var arr = new JsArray(realm);
            for (var i = 0; i < parts.Count; i++)
            {
                arr.Push(MakePart(realm, parts[i].Type, parts[i].Value));
            }

            return JsValue.Object(arr);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "formatRange", 2,
            (thisV, args) => DtfFormatRangeImpl(realm, thisV, args, toParts: false));
        IntrinsicHelpers.DefineMethod(realm, proto, "formatRangeToParts", 2,
            (thisV, args) => DtfFormatRangeImpl(realm, thisV, args, toParts: true));
        IntrinsicHelpers.DefineMethod(realm, proto, "resolvedOptions", 0,
            (thisV, _) => DtfResolvedOptions(realm, RequireDateTimeFormat(realm, thisV).State));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl.DateTimeFormat"), writable: false, enumerable: false, configurable: true));
        return ctor;
    }

    /// <summary>Date.prototype.toLocale{,Date,Time}String (ECMA-402 §19.4) —
    /// format a valid time value through a DateTimeFormat built with the
    /// method's required/defaults pair.</summary>
    internal static string FormatDateToLocale(JsRealm realm, double timeValueMs, JsValue[] args, string required, string defaults)
    {
        var state = CreateDateTimeFormatState(
            realm,
            args.Length > 0 ? args[0] : JsValue.Undefined,
            args.Length > 1 ? args[1] : JsValue.Undefined,
            required, defaults);
        return DtfJoinParts(PartitionDateTimeParts(state, Math.Truncate(timeValueMs)));
    }

    // =====================================================================
    //             §11.1.2 CreateDateTimeFormat (option order)
    // =====================================================================

    private static readonly string[] DtfNarrowShortLong = ["narrow", "short", "long"];
    private static readonly string[] DtfTwoDigitNumeric = ["2-digit", "numeric"];
    private static readonly string[] DtfMonthValues = ["2-digit", "numeric", "narrow", "short", "long"];
    private static readonly string[] DtfStyleValues = ["full", "long", "medium", "short"];
    private static readonly string[] DtfTimeZoneNameValues =
        ["short", "long", "shortOffset", "longOffset", "shortGeneric", "longGeneric"];
    private static readonly string[] DtfHourCycleValues = ["h11", "h12", "h23", "h24"];

    private static readonly HashSet<string> SupportedCalendars = new(StringComparer.Ordinal)
    {
        "buddhist", "chinese", "coptic", "dangi", "ethioaa", "ethiopic", "gregory", "hebrew",
        "indian", "islamic", "islamic-civil", "islamic-rgsa", "islamic-tbla", "islamic-umalqura",
        "iso8601", "japanese", "persian", "roc",
    };

    private static string CanonicalizeCalendarAlias(string value) => value switch
    {
        "islamicc" => "islamic-civil",
        "ethiopic-amete-alem" => "ethioaa",
        "gregorian" => "gregory",
        _ => value,
    };

    private static DateTimeFormatState CreateDateTimeFormatState(
        JsRealm realm, JsValue locales, JsValue optionsValue, string required, string defaults)
    {
        var requested = ReadRequestedLocales(realm, locales);
        var options = optionsValue.IsUndefined ? null : AbstractOperations.ToObject(realm, optionsValue);

        _ = GetOptionEnum(realm, options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var caOpt = GetOptionEnum(realm, options, "calendar", null, null);
        if (caOpt is not null)
        {
            if (!IsWellFormedNumberingSystem(caOpt))
            {
                throw new JsThrow(realm.NewRangeError($"Invalid calendar: \"{caOpt}\""));
            }

            caOpt = CanonicalizeCalendarAlias(caOpt.ToLowerInvariant());
        }

        var nuOpt = GetOptionEnum(realm, options, "numberingSystem", null, null);
        if (nuOpt is not null)
        {
            if (!IsWellFormedNumberingSystem(nuOpt))
            {
                throw new JsThrow(realm.NewRangeError($"Invalid numberingSystem: \"{nuOpt}\""));
            }

            nuOpt = nuOpt.ToLowerInvariant();
        }

        var hour12 = GetBooleanOption(realm, options, "hour12");
        var hcOpt = GetOptionEnum(realm, options, "hourCycle", DtfHourCycleValues, null);
        if (hour12 is not null)
        {
            hcOpt = null;
        }

        var (localeName, dataLocale, calendar, nu, extHc) =
            ResolveDtfLocale(requested, caOpt, nuOpt, hcOpt, hour12 is not null);

        var tzValue = OptGet(realm, options, "timeZone");
        string tzId;
        int? fixedOffset = null;
        TimeZoneInfo? namedZone = null;
        if (tzValue.IsUndefined)
        {
            tzId = DefaultTimeZone;
            fixedOffset = 0;
        }
        else
        {
            var tzText = AbstractOperations.ToStringJs(realm.ActiveVm, tzValue);
            if (TryParseOffsetTimeZone(tzText, out var normalizedOffset, out var offsetMinutes))
            {
                tzId = normalizedOffset;
                fixedOffset = offsetMinutes;
            }
            else if (TryResolveNamedTimeZone(tzText, out var resolvedId, out namedZone))
            {
                tzId = resolvedId;
                if (namedZone is null)
                {
                    fixedOffset = 0;
                }
            }
            else
            {
                throw new JsThrow(realm.NewRangeError($"Invalid time zone: \"{tzText}\""));
            }
        }

        var weekday = GetOptionEnum(realm, options, "weekday", DtfNarrowShortLong, null);
        var era = GetOptionEnum(realm, options, "era", DtfNarrowShortLong, null);
        var year = GetOptionEnum(realm, options, "year", DtfTwoDigitNumeric, null);
        var month = GetOptionEnum(realm, options, "month", DtfMonthValues, null);
        var day = GetOptionEnum(realm, options, "day", DtfTwoDigitNumeric, null);
        var dayPeriod = GetOptionEnum(realm, options, "dayPeriod", DtfNarrowShortLong, null);
        var hour = GetOptionEnum(realm, options, "hour", DtfTwoDigitNumeric, null);
        var minute = GetOptionEnum(realm, options, "minute", DtfTwoDigitNumeric, null);
        var second = GetOptionEnum(realm, options, "second", DtfTwoDigitNumeric, null);
        var fsd = GetNumberOptionSpec(realm, options, "fractionalSecondDigits", 1, 3, null);
        var tzName = GetOptionEnum(realm, options, "timeZoneName", DtfTimeZoneNameValues, null);
        var hasExplicit = weekday is not null || era is not null || year is not null || month is not null
            || day is not null || dayPeriod is not null || hour is not null || minute is not null
            || second is not null || fsd is not null || tzName is not null;
        _ = GetOptionEnum(realm, options, "formatMatcher", ["basic", "best fit"], "best fit");
        var dateStyle = GetOptionEnum(realm, options, "dateStyle", DtfStyleValues, null);
        var timeStyle = GetOptionEnum(realm, options, "timeStyle", DtfStyleValues, null);

        if (dateStyle is not null || timeStyle is not null)
        {
            if (hasExplicit)
            {
                throw new JsThrow(realm.NewTypeError("dateStyle/timeStyle cannot be combined with explicit format components"));
            }

            if (required == "date" && timeStyle is not null)
            {
                throw new JsThrow(realm.NewTypeError("timeStyle is not allowed here"));
            }

            if (required == "time" && dateStyle is not null)
            {
                throw new JsThrow(realm.NewTypeError("dateStyle is not allowed here"));
            }
        }
        else
        {
            var needDefaults = true;
            if ((required is "date" or "any")
                && (weekday is not null || year is not null || month is not null || day is not null))
            {
                needDefaults = false;
            }

            if ((required is "time" or "any")
                && (dayPeriod is not null || hour is not null || minute is not null || second is not null || fsd is not null))
            {
                needDefaults = false;
            }

            if (needDefaults && defaults is "date" or "all")
            {
                year = "numeric";
                month = "numeric";
                day = "numeric";
            }

            if (needDefaults && defaults is "time" or "all")
            {
                hour = "numeric";
                minute = "numeric";
                second = "numeric";
            }
        }

        var hcDefault = DtfDefaultHourCycle(dataLocale);
        string hc;
        if (hour12 == true)
        {
            hc = dataLocale == "ja" ? "h11" : "h12";
        }
        else if (hour12 == false)
        {
            hc = "h23";
        }
        else
        {
            hc = hcOpt ?? extHc ?? hcDefault;
        }

        // en best-fit skeletons render minutes/seconds as two digits whenever
        // another time field precedes them ("h:mm:ss", "mm:ss").
        if (hour is not null)
        {
            if (minute is not null)
            {
                minute = "2-digit";
            }

            if (second is not null)
            {
                second = "2-digit";
            }
        }
        else if (minute is not null && second is not null)
        {
            minute = "2-digit";
            second = "2-digit";
        }

        return new DateTimeFormatState(
            localeName, dataLocale, calendar, nu, tzId, fixedOffset, namedZone, hc,
            weekday, era, year, month, day, dayPeriod, hour, minute, second,
            fsd ?? 0, tzName, dateStyle, timeStyle);
    }

    private static (string Name, string DataLocale, string Calendar, string Nu, string? ExtHc) ResolveDtfLocale(
        List<string> requested, string? caOpt, string? nuOpt, string? hcOpt, bool hour12Set)
    {
        var baseName = DefaultLocale;
        string? extCa = null;
        string? extNu = null;
        string? extHc = null;
        foreach (var tag in requested)
        {
            if (!TryCreateLocale(tag, out var locale))
            {
                continue;
            }

            baseName = StripExtensions(locale.Name);
            extCa = ExtensionValue(tag, "ca");
            extNu = ExtensionValue(tag, "nu");
            extHc = ExtensionValue(tag, "hc");
            break;
        }

        if (extCa is not null)
        {
            extCa = CanonicalizeCalendarAlias(extCa);
        }

        static string WithCalendarFallback(string value)
            => value is "islamic" or "islamic-rgsa" ? "islamic-civil" : value;

        string calendar;
        bool reflectCa;
        if (caOpt is not null && SupportedCalendars.Contains(caOpt))
        {
            calendar = WithCalendarFallback(caOpt);
            reflectCa = string.Equals(extCa, caOpt, StringComparison.Ordinal);
        }
        else if (extCa is not null && SupportedCalendars.Contains(extCa))
        {
            calendar = WithCalendarFallback(extCa);
            reflectCa = true;
        }
        else
        {
            calendar = DefaultCalendar;
            reflectCa = false;
        }

        string nu;
        bool reflectNu;
        if (nuOpt is not null && NumberingSystemDigits.ContainsKey(nuOpt))
        {
            nu = nuOpt;
            reflectNu = string.Equals(extNu, nuOpt, StringComparison.Ordinal);
        }
        else if (extNu is not null && NumberingSystemDigits.ContainsKey(extNu))
        {
            nu = extNu;
            reflectNu = true;
        }
        else
        {
            nu = DefaultNumberingSystem;
            reflectNu = false;
        }

        var extHcValid = extHc is "h11" or "h12" or "h23" or "h24" ? extHc : null;
        var reflectHc = extHcValid is not null && !hour12Set
            && (hcOpt is null || string.Equals(hcOpt, extHcValid, StringComparison.Ordinal));

        var ext = new List<string>(3);
        if (reflectCa)
        {
            ext.Add("ca-" + calendar);
        }

        if (reflectHc)
        {
            ext.Add("hc-" + extHcValid);
        }

        if (reflectNu)
        {
            ext.Add("nu-" + nu);
        }

        var name = ext.Count == 0 ? baseName : baseName + "-u-" + string.Join('-', ext);
        var dash = baseName.IndexOf('-');
        var dataLocale = dash >= 0 ? baseName[..dash] : baseName;
        return (name, dataLocale, calendar, nu, extHcValid);
    }

    private static string DtfDefaultHourCycle(string dataLocale) => dataLocale switch
    {
        "ja" or "de" or "fr" or "it" or "ru" or "pl" or "sv" or "nl" or "pt" or "es" or "tr" => "h23",
        _ => "h12",
    };

    // =====================================================================
    //                              Time zones
    // =====================================================================

    private static bool TryParseOffsetTimeZone(string text, out string normalized, out int minutes)
    {
        normalized = string.Empty;
        minutes = 0;
        if (text.Length is not (3 or 5 or 6) || (text[0] != '+' && text[0] != '-'))
        {
            return false;
        }

        for (var i = 1; i < text.Length; i++)
        {
            if (i == 3 && text.Length == 6)
            {
                if (text[i] != ':')
                {
                    return false;
                }

                continue;
            }

            if (!char.IsAsciiDigit(text[i]))
            {
                return false;
            }
        }

        var hh = (text[1] - '0') * 10 + (text[2] - '0');
        var mm = 0;
        if (text.Length == 5)
        {
            mm = (text[3] - '0') * 10 + (text[4] - '0');
        }
        else if (text.Length == 6)
        {
            mm = (text[4] - '0') * 10 + (text[5] - '0');
        }

        if (hh > 23 || mm > 59)
        {
            return false;
        }

        var total = hh * 60 + mm;
        var negative = text[0] == '-' && total != 0;
        minutes = negative ? -total : total;
        normalized = $"{(negative ? '-' : '+')}{hh:D2}:{mm:D2}";
        return true;
    }

    private static readonly Lazy<Dictionary<string, string>> AvailableNamedTimeZones = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // GetSystemTimeZones omits Link names (Asia/Calcutta, Etc/GMT-3, ...),
        // so walk the zone database directory when one is present; identifiers
        // start with an uppercase ASCII letter, which also skips zone.tab,
        // leapseconds, posix/, right/ and friends.
        var zoneRoot = Environment.GetEnvironmentVariable("TZDIR");
        if (string.IsNullOrEmpty(zoneRoot) || !Directory.Exists(zoneRoot))
        {
            zoneRoot = "/usr/share/zoneinfo";
        }

        if (Directory.Exists(zoneRoot))
        {
            var rootLength = zoneRoot.Length + 1;
            foreach (var file in Directory.EnumerateFiles(zoneRoot, "*", SearchOption.AllDirectories))
            {
                var id = file[rootLength..];
                if (id.Length == 0 || !char.IsAsciiLetterUpper(id[0]) || id.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }

                map[id] = id;
            }
        }

        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            map[tz.Id] = tz.Id;
        }

        map["UTC"] = "UTC";
        map["Etc/UTC"] = "Etc/UTC";
        map["Etc/GMT"] = "Etc/GMT";
        map["GMT"] = "GMT";
        return map;
    });

    private static bool TryResolveNamedTimeZone(string request, out string id, out TimeZoneInfo? zone)
    {
        zone = null;
        if (!AvailableNamedTimeZones.Value.TryGetValue(request, out var resolved))
        {
            id = string.Empty;
            return false;
        }

        id = resolved;
        if (resolved is "UTC" or "Etc/UTC" or "Etc/GMT" or "GMT")
        {
            return true;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(resolved, out zone))
        {
            zone = null;
        }

        return true;
    }

    private static int DtfOffsetMinutes(DateTimeFormatState st, double ms)
    {
        if (st.FixedOffsetMinutes is int fixedOffset)
        {
            return fixedOffset;
        }

        if (st.NamedZone is null)
        {
            return 0;
        }

        var clamped = Math.Clamp(ms, -62_135_596_800_000d, 253_402_300_799_000d);
        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)clamped);
        return (int)st.NamedZone.GetUtcOffset(dto).TotalMinutes;
    }

    // =====================================================================
    //                      Time values and civil fields
    // =====================================================================

    private static double DtfClipDateArg(JsRealm realm, JsValue value)
    {
        if (value.IsUndefined)
        {
            return Math.Truncate((DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds);
        }

        return DtfTimeClip(realm, DtfCoerceTime(realm, value));
    }

    private static double DtfCoerceTime(JsRealm realm, JsValue value)
    {
        if (value.IsObject && value.AsObject is JsDate date)
        {
            return date.TimeValueMs;
        }

        var prim = AbstractOperations.ToPrimitive(realm.ActiveVm, value, "number");
        if (prim.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a number"));
        }

        return NumberCtor.ToNumber(prim);
    }

    private static double DtfTimeClip(JsRealm realm, double ms)
    {
        if (double.IsNaN(ms) || Math.Abs(ms) > 8.64e15)
        {
            throw new JsThrow(realm.NewRangeError("Invalid time value"));
        }

        var t = Math.Truncate(ms);
        return t == 0 ? 0d : t;
    }

    private readonly record struct DtfFields(long Year, int Month, int Day, int Weekday, int Hour, int Minute, int Second, int Millisecond);

    private static DtfFields DtfFieldsFromTime(double t)
    {
        var tl = (long)t;
        var day = DtfFloorDiv(tl, 86_400_000L);
        var msInDay = tl - day * 86_400_000L;
        var (year, month, dayOfMonth) = DtfCivilFromDays(day);
        return new DtfFields(
            year,
            month,
            dayOfMonth,
            (int)DtfFloorMod(day + 4, 7),
            (int)(msInDay / 3_600_000L),
            (int)(msInDay / 60_000L % 60),
            (int)(msInDay / 1_000L % 60),
            (int)(msInDay % 1_000L));
    }

    private static long DtfFloorDiv(long a, long b) => a >= 0 ? a / b : ~(~a / b);

    private static long DtfFloorMod(long a, long b)
    {
        var r = a % b;
        return r < 0 ? r + b : r;
    }

    private static (long Year, int Month, int Day) DtfCivilFromDays(long z)
    {
        z += 719_468;
        var era = (z >= 0 ? z : z - 146_096) / 146_097;
        var doe = z - era * 146_097;
        var yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
        var y = yoe + era * 400;
        var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        var mp = (5 * doy + 2) / 153;
        var d = (int)(doy - (153 * mp + 2) / 5 + 1);
        var m = (int)(mp < 10 ? mp + 3 : mp - 9);
        return (y + (m <= 2 ? 1 : 0), m, d);
    }

    private static long DtfDaysFromCivil(long y, int m, int d)
    {
        y -= m <= 2 ? 1 : 0;
        var era = (y >= 0 ? y : y - 399) / 400;
        var yoe = y - era * 400;
        var doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
        var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146_097 + doe - 719_468;
    }

    // =====================================================================
    //                        Calendar projections
    // =====================================================================

    private sealed record DtfCalendarDate(
        string? EraLong,
        string? EraShort,
        string? EraNarrow,
        long Year,
        int Month,
        bool LeapMonth,
        int Day,
        long? RelatedYear,
        string? YearName);

    private static readonly ChineseLunisolarCalendar DtfChineseCalendar = new();
    private static readonly KoreanLunisolarCalendar DtfKoreanCalendar = new();

    private static DtfCalendarDate DtfProjectCalendar(string calendar, DtfFields f)
    {
        switch (calendar)
        {
            case "chinese":
            case "dangi":
            {
                var (relatedYear, month, leap, day, yearName) = DtfLunarFields(calendar, f);
                return new DtfCalendarDate(null, null, null, relatedYear, month, leap, day, relatedYear, yearName);
            }

            case "buddhist":
                return new DtfCalendarDate("Buddhist Era", "BE", "BE", f.Year + 543, f.Month, false, f.Day, null, null);

            case "roc":
                return f.Year >= 1912
                    ? new DtfCalendarDate("Minguo", "Minguo", "Minguo", f.Year - 1911, f.Month, false, f.Day, null, null)
                    : new DtfCalendarDate("Before R.O.C.", "B.R.O.C.", "B.R.O.C.", 1912 - f.Year, f.Month, false, f.Day, null, null);

            case "japanese":
                return DtfJapaneseDate(f);

            case "islamic":
            case "islamic-civil":
            case "islamic-rgsa":
            case "islamic-umalqura":
            case "islamic-tbla":
            {
                var epochShift = calendar == "islamic-tbla" ? 1 : 0;
                var fixedDay = DtfDaysFromCivil(f.Year, f.Month, f.Day) + 719_163;
                var islamicDay = fixedDay - 227_015 + epochShift;
                var islamicYear = DtfFloorDiv(30 * islamicDay + 10_646, 10_631);
                return islamicYear >= 1
                    ? new DtfCalendarDate("AH", "AH", "AH", islamicYear, f.Month, false, f.Day, null, null)
                    : new DtfCalendarDate("BH", "BH", "BH", 1 - islamicYear, f.Month, false, f.Day, null, null);
            }

            case "ethiopic":
                return f.Year >= 8
                    ? new DtfCalendarDate("Amete Mihret", "AM", "AM", f.Year - 7, f.Month, false, f.Day, null, null)
                    : new DtfCalendarDate("Amete Alem", "AA", "AA", f.Year + 5492, f.Month, false, f.Day, null, null);

            case "ethioaa":
                return new DtfCalendarDate("Amete Alem", "AA", "AA", f.Year + 5492, f.Month, false, f.Day, null, null);

            case "coptic":
                return f.Year >= 284
                    ? new DtfCalendarDate("Anno Martyrum", "AM", "AM", f.Year - 283, f.Month, false, f.Day, null, null)
                    : new DtfCalendarDate("Anno Martyrum", "AM", "AM", 284 - f.Year, f.Month, false, f.Day, null, null);

            case "hebrew":
                return new DtfCalendarDate("Anno Mundi", "AM", "AM", f.Year + 3760, f.Month, false, f.Day, null, null);

            case "indian":
                return new DtfCalendarDate("Saka Era", "Saka", "Saka", f.Year - 78, f.Month, false, f.Day, null, null);

            case "persian":
                return new DtfCalendarDate("Anno Persico", "AP", "AP", f.Year - 621, f.Month, false, f.Day, null, null);

            default:
                return f.Year <= 0
                    ? new DtfCalendarDate("Before Christ", "BC", "B", 1 - f.Year, f.Month, false, f.Day, null, null)
                    : new DtfCalendarDate("Anno Domini", "AD", "A", f.Year, f.Month, false, f.Day, null, null);
        }
    }

    private static DtfCalendarDate DtfJapaneseDate(DtfFields f)
    {
        // (start date inclusive, long, narrow) — most recent first.
        (long Y, int M, int D, string Name, string Narrow)[] eras =
        [
            (2019, 5, 1, "Reiwa", "R"),
            (1989, 1, 8, "Heisei", "H"),
            (1926, 12, 25, "Showa", "S"),
            (1912, 7, 30, "Taisho", "T"),
            (1868, 10, 23, "Meiji", "M"),
        ];
        foreach (var e in eras)
        {
            if (f.Year > e.Y
                || (f.Year == e.Y && (f.Month > e.M || (f.Month == e.M && f.Day >= e.D))))
            {
                return new DtfCalendarDate(e.Name, e.Name, e.Narrow, f.Year - e.Y + 1, f.Month, false, f.Day, null, null);
            }
        }

        return f.Year <= 0
            ? new DtfCalendarDate("Before Christ", "BC", "B", 1 - f.Year, f.Month, false, f.Day, null, null)
            : new DtfCalendarDate("Anno Domini", "AD", "A", f.Year, f.Month, false, f.Day, null, null);
    }

    private static (long RelatedYear, int Month, bool Leap, int Day, string YearName) DtfLunarFields(string calendar, DtfFields f)
    {
        long relatedYear = f.Month <= 2 ? f.Year - 1 : f.Year;
        var month = f.Month;
        var leap = false;
        var day = f.Day;
        if (f.Year >= 1 && f.Year <= 9999)
        {
            var dt = new DateTime((int)f.Year, f.Month, f.Day);
            Calendar primary = calendar == "dangi" ? DtfKoreanCalendar : DtfChineseCalendar;
            Calendar secondary = calendar == "dangi" ? DtfChineseCalendar : DtfKoreanCalendar;
            Calendar? cal = null;
            if (dt >= primary.MinSupportedDateTime && dt <= primary.MaxSupportedDateTime)
            {
                cal = primary;
            }
            else if (dt >= secondary.MinSupportedDateTime && dt <= secondary.MaxSupportedDateTime)
            {
                cal = secondary;
            }

            if (cal is not null)
            {
                var y = cal.GetYear(dt);
                var m = cal.GetMonth(dt);
                day = cal.GetDayOfMonth(dt);
                var leapMonth = cal.GetLeapMonth(y);
                if (leapMonth > 0)
                {
                    if (m == leapMonth)
                    {
                        leap = true;
                        m--;
                    }
                    else if (m > leapMonth)
                    {
                        m--;
                    }
                }

                relatedYear = y;
                month = m;
            }
        }

        const string stems = "甲乙丙丁戊己庚辛壬癸";
        const string branches = "子丑寅卯辰巳午未申酉戌亥";
        var idx = (int)DtfFloorMod(relatedYear - 4, 60);
        var yearName = string.Concat(stems[idx % 10], branches[idx % 12]);
        return (relatedYear, month, leap, day, yearName);
    }

    // =====================================================================
    //                         Partitioning (en data)
    // =====================================================================

    private static readonly string[] DtfWeekdaysLong =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] DtfWeekdaysShort = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] DtfWeekdaysNarrow = ["S", "M", "T", "W", "T", "F", "S"];
    private static readonly string[] DtfMonthsLong =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] DtfMonthsShort =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly string[] DtfMonthsNarrow = ["J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D"];
    private static readonly string[] DtfLunarMonths =
        ["First Month", "Second Month", "Third Month", "Fourth Month", "Fifth Month", "Sixth Month",
         "Seventh Month", "Eighth Month", "Ninth Month", "Tenth Month", "Eleventh Month", "Twelfth Month"];

    private readonly record struct DtfComponents(
        string? Weekday, string? Era, string? Year, string? Month, string? Day,
        string? DayPeriod, string? Hour, string? Minute, string? Second, int Fsd, string? TimeZoneName);

    private static DtfComponents DtfEffectiveComponents(DateTimeFormatState st)
    {
        if (st.DateStyle is null && st.TimeStyle is null)
        {
            return new DtfComponents(st.Weekday, st.Era, st.Year, st.Month, st.Day,
                st.DayPeriod, st.Hour, st.Minute, st.Second, st.FractionalSecondDigits, st.TimeZoneName);
        }

        string? weekday = null;
        string? year = null;
        string? month = null;
        string? day = null;
        string? hour = null;
        string? minute = null;
        string? second = null;
        string? tzName = null;
        switch (st.DateStyle)
        {
            case "full":
                weekday = "long";
                month = "long";
                day = "numeric";
                year = "numeric";
                break;
            case "long":
                month = "long";
                day = "numeric";
                year = "numeric";
                break;
            case "medium":
                month = "short";
                day = "numeric";
                year = "numeric";
                break;
            case "short":
                month = "numeric";
                day = "numeric";
                year = "2-digit";
                break;
        }

        switch (st.TimeStyle)
        {
            case "full":
                hour = "numeric";
                minute = "2-digit";
                second = "2-digit";
                tzName = "long";
                break;
            case "long":
                hour = "numeric";
                minute = "2-digit";
                second = "2-digit";
                tzName = "short";
                break;
            case "medium":
                hour = "numeric";
                minute = "2-digit";
                second = "2-digit";
                break;
            case "short":
                hour = "numeric";
                minute = "2-digit";
                break;
        }

        return new DtfComponents(weekday, null, year, month, day, null, hour, minute, second, 0, tzName);
    }

    private static string DtfNum(DateTimeFormatState st, long value, string style)
    {
        var abs = Math.Abs(value);
        string text;
        if (style == "2-digit")
        {
            text = (abs % 100).ToString("D2", CultureInfo.InvariantCulture);
        }
        else
        {
            text = abs.ToString(CultureInfo.InvariantCulture);
        }

        return MapDigits(text, st.NumberingSystem);
    }

    private static string DtfFlexibleDayPeriod(int hour, int minute, int second, string width)
    {
        if (hour == 12 && minute == 0 && second == 0)
        {
            return width == "narrow" ? "n" : "noon";
        }

        if (hour < 6 || hour >= 21)
        {
            return "at night";
        }

        if (hour < 12)
        {
            return "in the morning";
        }

        if (hour < 18)
        {
            return "in the afternoon";
        }

        return "in the evening";
    }

    private static string DtfTimeZoneDisplay(DateTimeFormatState st, int offsetMinutes, string width)
    {
        if (st.FixedOffsetMinutes == 0 && st.NamedZone is null)
        {
            return width is "long" or "longGeneric" ? "Coordinated Universal Time" : "UTC";
        }

        var sign = offsetMinutes < 0 ? '-' : '+';
        var abs = Math.Abs(offsetMinutes);
        var hh = abs / 60;
        var mm = abs % 60;
        if (width is "long" or "longOffset" or "longGeneric")
        {
            return $"GMT{sign}{hh:D2}:{mm:D2}";
        }

        return mm == 0 ? $"GMT{sign}{hh}" : $"GMT{sign}{hh}:{mm:D2}";
    }

    private static List<DtPart> PartitionDateTimeParts(DateTimeFormatState st, double ms)
    {
        var offset = DtfOffsetMinutes(st, ms);
        var f = DtfFieldsFromTime(ms + offset * 60_000d);
        var c = DtfEffectiveComponents(st);
        var cd = DtfProjectCalendar(st.Calendar, f);
        var parts = new List<DtPart>(12);
        var isZh = st.DataLocale == "zh";
        var isLunisolar = st.Calendar is "chinese" or "dangi";
        var monthTextual = c.Month is "narrow" or "short" or "long";
        var hasDate = c.Weekday is not null || c.Era is not null || c.Year is not null
            || c.Month is not null || c.Day is not null;
        var hasTime = c.DayPeriod is not null || c.Hour is not null || c.Minute is not null
            || c.Second is not null || c.Fsd > 0;

        if (c.Weekday is not null)
        {
            var names = c.Weekday switch
            {
                "narrow" => DtfWeekdaysNarrow,
                "short" => DtfWeekdaysShort,
                _ => DtfWeekdaysLong,
            };
            parts.Add(new DtPart("weekday", names[f.Weekday]));
            if (c.Year is not null || c.Month is not null || c.Day is not null)
            {
                parts.Add(new DtPart("literal", ", "));
            }
        }

        AppendDateFields(st, parts, c, cd, f, isZh, isLunisolar, monthTextual);

        if (c.Era is not null && cd.EraLong is not null)
        {
            if (parts.Count > 0)
            {
                parts.Add(new DtPart("literal", " "));
            }

            parts.Add(new DtPart("era", c.Era switch
            {
                "narrow" => cd.EraNarrow!,
                "long" => cd.EraLong,
                _ => cd.EraShort!,
            }));
        }

        if (hasTime)
        {
            if (hasDate)
            {
                parts.Add(new DtPart("literal", ", "));
            }

            AppendTimeFields(st, parts, c, f);
        }

        if (c.TimeZoneName is not null)
        {
            if (parts.Count > 0)
            {
                parts.Add(new DtPart("literal", " "));
            }

            parts.Add(new DtPart("timeZoneName", DtfTimeZoneDisplay(st, offset, c.TimeZoneName)));
        }

        return parts;
    }

    private static void AppendDateFields(
        DateTimeFormatState st, List<DtPart> parts, DtfComponents c, DtfCalendarDate cd, DtfFields f,
        bool isZh, bool isLunisolar, bool monthTextual)
    {
        if (c.Year is null && c.Month is null && c.Day is null)
        {
            return;
        }

        var yearType = isLunisolar ? "relatedYear" : "year";
        var yearValue = isLunisolar ? cd.RelatedYear!.Value : cd.Year;

        if (isLunisolar && c.Year is not null && c.Month is null && c.Day is null)
        {
            // Year-only pattern for lunisolar calendars: related year + cyclic
            // year name ("2019己亥年" in zh; "2019(己亥)" elsewhere).
            parts.Add(new DtPart("relatedYear", DtfNum(st, yearValue, c.Year)));
            if (isZh)
            {
                parts.Add(new DtPart("yearName", cd.YearName!));
                parts.Add(new DtPart("literal", "年"));
            }
            else
            {
                parts.Add(new DtPart("literal", "("));
                parts.Add(new DtPart("yearName", cd.YearName!));
                parts.Add(new DtPart("literal", ")"));
            }

            return;
        }

        if (monthTextual && c.Month is not null)
        {
            string monthName;
            if (isLunisolar)
            {
                var baseName = DtfLunarMonths[Math.Clamp(cd.Month - 1, 0, 11)];
                monthName = cd.LeapMonth ? "Leap " + baseName : baseName;
            }
            else
            {
                var names = c.Month switch
                {
                    "narrow" => DtfMonthsNarrow,
                    "short" => DtfMonthsShort,
                    _ => DtfMonthsLong,
                };
                monthName = names[Math.Clamp(cd.Month - 1, 0, 11)];
            }

            parts.Add(new DtPart("month", monthName));
            if (c.Day is not null)
            {
                parts.Add(new DtPart("literal", " "));
                parts.Add(new DtPart("day", DtfNum(st, cd.Day, c.Day)));
            }

            if (c.Year is not null)
            {
                parts.Add(new DtPart("literal", c.Day is not null ? ", " : " "));
                parts.Add(new DtPart(yearType, DtfNum(st, yearValue, c.Year)));
            }

            return;
        }

        // Numeric date: en orders M/d/y; zh orders y/M/d.
        var fields = new List<DtPart>(3);
        if (c.Month is not null)
        {
            fields.Add(new DtPart("month", DtfNum(st, cd.Month, c.Month)));
        }

        if (c.Day is not null)
        {
            fields.Add(new DtPart("day", DtfNum(st, cd.Day, c.Day)));
        }

        var yearPart = c.Year is not null ? new DtPart(yearType, DtfNum(st, yearValue, c.Year)) : (DtPart?)null;
        if (yearPart is not null)
        {
            if (isZh)
            {
                fields.Insert(0, yearPart.Value);
            }
            else
            {
                fields.Add(yearPart.Value);
            }
        }

        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                parts.Add(new DtPart("literal", "/"));
            }

            parts.Add(fields[i]);
        }
    }

    private static void AppendTimeFields(DateTimeFormatState st, List<DtPart> parts, DtfComponents c, DtfFields f)
    {
        var hcIs12 = st.HourCycle is "h11" or "h12";
        var wroteField = false;
        if (c.Hour is not null)
        {
            var h = f.Hour;
            switch (st.HourCycle)
            {
                case "h11":
                    h = f.Hour % 12;
                    break;
                case "h12":
                    h = f.Hour % 12;
                    if (h == 0)
                    {
                        h = 12;
                    }

                    break;
                case "h24":
                    h = f.Hour == 0 ? 24 : f.Hour;
                    break;
            }

            parts.Add(new DtPart("hour", DtfNum(st, h, c.Hour)));
            wroteField = true;
        }

        if (c.Minute is not null)
        {
            if (wroteField)
            {
                parts.Add(new DtPart("literal", ":"));
            }

            parts.Add(new DtPart("minute", DtfNum(st, f.Minute, c.Minute)));
            wroteField = true;
        }

        if (c.Second is not null)
        {
            if (wroteField)
            {
                parts.Add(new DtPart("literal", ":"));
            }

            parts.Add(new DtPart("second", DtfNum(st, f.Second, c.Second)));
            wroteField = true;
        }

        if (c.Fsd > 0)
        {
            var frac = f.Millisecond.ToString("D3", CultureInfo.InvariantCulture)[..c.Fsd];
            if (wroteField)
            {
                var sep = st.NumberingSystem is "arab" or "arabext" ? "٫" : ".";
                parts.Add(new DtPart("literal", sep));
            }

            parts.Add(new DtPart("fractionalSecond", MapDigits(frac, st.NumberingSystem)));
            wroteField = true;
        }

        if (c.DayPeriod is not null)
        {
            if (wroteField)
            {
                parts.Add(new DtPart("literal", " "));
            }

            parts.Add(new DtPart("dayPeriod", DtfFlexibleDayPeriod(f.Hour, f.Minute, f.Second, c.DayPeriod)));
        }
        else if (c.Hour is not null && hcIs12)
        {
            parts.Add(new DtPart("literal", " "));
            parts.Add(new DtPart("dayPeriod", f.Hour < 12 ? "AM" : "PM"));
        }
    }

    private static string DtfJoinParts(List<DtPart> parts)
    {
        var sb = new System.Text.StringBuilder(32);
        for (var i = 0; i < parts.Count; i++)
        {
            sb.Append(parts[i].Value);
        }

        return sb.ToString();
    }

    // =====================================================================
    //           formatRange / formatRangeToParts (§11.5.8-11.5.10)
    // =====================================================================

    private const string DtfRangeSeparator = " – ";

    private static JsValue DtfFormatRangeImpl(JsRealm realm, JsValue thisV, JsValue[] args, bool toParts)
    {
        var dtf = RequireDateTimeFormat(realm, thisV);
        var a = args.Length > 0 ? args[0] : JsValue.Undefined;
        var b = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (a.IsUndefined || b.IsUndefined)
        {
            throw new JsThrow(realm.NewTypeError("Intl.DateTimeFormat range formatting requires two defined arguments"));
        }

        var xRaw = DtfCoerceTime(realm, a);
        var yRaw = DtfCoerceTime(realm, b);
        var x = DtfTimeClip(realm, xRaw);
        var y = DtfTimeClip(realm, yRaw);
        var st = dtf.State;
        var px = PartitionDateTimeParts(st, x);
        var py = PartitionDateTimeParts(st, y);
        var ranged = DtfBuildRangeParts(st, px, py);
        if (!toParts)
        {
            var sb = new System.Text.StringBuilder(48);
            for (var i = 0; i < ranged.Count; i++)
            {
                sb.Append(ranged[i].Part.Value);
            }

            return JsValue.String(sb.ToString());
        }

        var arr = new JsArray(realm);
        for (var i = 0; i < ranged.Count; i++)
        {
            var part = MakePart(realm, ranged[i].Part.Type, ranged[i].Part.Value).AsObject;
            part.Set("source", JsValue.String(ranged[i].Source));
            arr.Push(JsValue.Object(part));
        }

        return JsValue.Object(arr);
    }

    private static bool DtfSameFieldValue(List<DtPart> px, List<DtPart> py, string type)
    {
        string? a = null;
        string? b = null;
        for (var i = 0; i < px.Count; i++)
        {
            if (px[i].Type == type)
            {
                a = px[i].Value;
                break;
            }
        }

        for (var i = 0; i < py.Count; i++)
        {
            if (py[i].Type == type)
            {
                b = py[i].Value;
                break;
            }
        }

        return string.Equals(a, b, StringComparison.Ordinal);
    }

    private static List<(DtPart Part, string Source)> DtfBuildRangeParts(
        DateTimeFormatState st, List<DtPart> px, List<DtPart> py)
    {
        var result = new List<(DtPart, string)>(px.Count + py.Count + 1);
        var identical = px.Count == py.Count;
        if (identical)
        {
            for (var i = 0; i < px.Count; i++)
            {
                if (px[i] != py[i])
                {
                    identical = false;
                    break;
                }
            }
        }

        if (identical)
        {
            for (var i = 0; i < px.Count; i++)
            {
                result.Add((px[i], "shared"));
            }

            return result;
        }

        // CLDR-style collapsing applies to text-month pure-date patterns
        // ("Jan 3 – 5, 2019"); numeric and time patterns duplicate fully.
        var c = DtfEffectiveComponents(st);
        var collapse = c.Month is "narrow" or "short" or "long"
            && c.Weekday is null && c.Era is null && c.Hour is null && c.Minute is null
            && c.Second is null && c.Fsd == 0 && c.TimeZoneName is null
            && st.Calendar is not ("chinese" or "dangi")
            && DtfSameFieldValue(px, py, "year");
        if (collapse)
        {
            var prefix = 0;
            var max = Math.Min(px.Count, py.Count);
            while (prefix < max && px[prefix] == py[prefix])
            {
                prefix++;
            }

            var suffix = 0;
            while (suffix < max - prefix && px[px.Count - 1 - suffix] == py[py.Count - 1 - suffix])
            {
                suffix++;
            }

            if (prefix + suffix < px.Count && prefix + suffix < py.Count)
            {
                for (var i = 0; i < prefix; i++)
                {
                    result.Add((px[i], "shared"));
                }

                for (var i = prefix; i < px.Count - suffix; i++)
                {
                    result.Add((px[i], "startRange"));
                }

                result.Add((new DtPart("literal", DtfRangeSeparator), "shared"));
                for (var i = prefix; i < py.Count - suffix; i++)
                {
                    result.Add((py[i], "endRange"));
                }

                for (var i = px.Count - suffix; i < px.Count; i++)
                {
                    result.Add((px[i], "shared"));
                }

                return result;
            }
        }

        for (var i = 0; i < px.Count; i++)
        {
            result.Add((px[i], "startRange"));
        }

        result.Add((new DtPart("literal", DtfRangeSeparator), "shared"));
        for (var i = 0; i < py.Count; i++)
        {
            result.Add((py[i], "endRange"));
        }

        return result;
    }

    // =====================================================================
    //                     resolvedOptions (§11.3.7 order)
    // =====================================================================

    private static JsValue DtfResolvedOptions(JsRealm realm, DateTimeFormatState st)
    {
        var o = realm.NewOrdinaryObject();
        o.Set("locale", JsValue.String(st.LocaleName));
        o.Set("calendar", JsValue.String(st.Calendar));
        o.Set("numberingSystem", JsValue.String(st.NumberingSystem));
        o.Set("timeZone", JsValue.String(st.TimeZoneId));
        if (st.Hour is not null || st.TimeStyle is not null)
        {
            o.Set("hourCycle", JsValue.String(st.HourCycle));
            o.Set("hour12", JsValue.Boolean(st.HourCycle is "h11" or "h12"));
        }

        void Emit(string name, string? value)
        {
            if (value is not null)
            {
                o.Set(name, JsValue.String(value));
            }
        }

        Emit("weekday", st.Weekday);
        Emit("era", st.Era);
        Emit("year", st.Year);
        Emit("month", st.Month);
        Emit("day", st.Day);
        Emit("dayPeriod", st.DayPeriod);
        Emit("hour", st.Hour);
        Emit("minute", st.Minute);
        Emit("second", st.Second);
        if (st.FractionalSecondDigits > 0)
        {
            o.Set("fractionalSecondDigits", JsValue.Number(st.FractionalSecondDigits));
        }

        Emit("timeZoneName", st.TimeZoneName);
        Emit("dateStyle", st.DateStyle);
        Emit("timeStyle", st.TimeStyle);
        return JsValue.Object(o);
    }
}
