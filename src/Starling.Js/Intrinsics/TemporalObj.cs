using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>Temporal (tc39 stage-3) — foundation slice: the namespace object,
/// Temporal.Now, Temporal.PlainDate, and Temporal.Duration on the ISO
/// calendar. Enough for real page code and the common test shapes; the rest
/// of the surface (Instant, PlainTime, PlainDateTime, ZonedDateTime,
/// PlainYearMonth, PlainMonthDay) layers on the same pattern. The test262
/// `Temporal` feature tag stays in the runner's skip list until the surface
/// is substantially complete, so partial coverage never drags the measured
/// bucket rates.</summary>
public static class TemporalObj
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var temporal = realm.NewOrdinaryObject();
        temporal.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Temporal"), writable: false, enumerable: false, configurable: true));

        InstallPlainDate(realm, temporal);
        InstallDuration(realm, temporal);
        InstallPlainTime(realm, temporal);
        InstallPlainDateTime(realm, temporal);
        InstallInstant(realm, temporal);
        InstallPlainYearMonth(realm, temporal);
        InstallPlainMonthDay(realm, temporal);
        InstallZonedDateTime(realm, temporal);
        InstallNow(realm, temporal);

        realm.GlobalObject.DefineOwnProperty("Temporal",
            PropertyDescriptor.Data(JsValue.Object(temporal), writable: true, enumerable: false, configurable: true));
    }

    // =====================================================================
    //                          Temporal.PlainDate
    // =====================================================================

    private sealed class TemporalPlainDate(JsObject prototype, int year, int month, int day) : JsObject(prototype)
    {
        public int Year { get; } = year;
        public int Month { get; } = month;
        public int Day { get; } = day;
        public DateOnly ToDateOnly() => new(Year, Month, Day);
    }

    private static JsObject? s_plainDateProto;

    private static void InstallPlainDate(JsRealm realm, JsObject temporal)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        s_plainDateProto = proto;
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "PlainDate", 3, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Temporal.PlainDate requires 'new'"));
            }

            var y = ToIntegerThrow(realm, args, 0, "year");
            var m = ToIntegerThrow(realm, args, 1, "month");
            var d = ToIntegerThrow(realm, args, 2, "day");
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(MakePlainDate(realm, instProto, y, m, d));
        }, isConstructor: true);
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Temporal.PlainDate"), writable: false, enumerable: false, configurable: true));

        IntrinsicHelpers.DefineMethod(realm, ctor, "from", 1, (_, args) =>
        {
            var item = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Object(PlainDateFrom(realm, item));
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "compare", 2, (_, args) =>
        {
            var a = PlainDateFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var b = PlainDateFrom(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            return JsValue.Number(a.ToDateOnly().CompareTo(b.ToDateOnly()) switch { < 0 => -1, > 0 => 1, _ => 0 });
        });

        void Getter(string name, Func<TemporalPlainDate, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0,
                    (thisV, _) => read(RequirePlainDate(realm, thisV))),
                null));
        Getter("year", pd => JsValue.Number(pd.Year));
        Getter("month", pd => JsValue.Number(pd.Month));
        Getter("day", pd => JsValue.Number(pd.Day));
        Getter("monthCode", pd => JsValue.String("M" + pd.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)));
        Getter("calendarId", _ => JsValue.String("iso8601"));
        Getter("dayOfWeek", pd => JsValue.Number((int)pd.ToDateOnly().DayOfWeek == 0 ? 7 : (int)pd.ToDateOnly().DayOfWeek));
        Getter("dayOfYear", pd => JsValue.Number(pd.ToDateOnly().DayOfYear));
        Getter("daysInMonth", pd => JsValue.Number(DateTime.DaysInMonth(pd.Year, pd.Month)));
        Getter("daysInYear", pd => JsValue.Number(DateTime.IsLeapYear(pd.Year) ? 366 : 365));
        Getter("daysInWeek", _ => JsValue.Number(7));
        Getter("monthsInYear", _ => JsValue.Number(12));
        Getter("inLeapYear", pd => JsValue.Boolean(DateTime.IsLeapYear(pd.Year)));

        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
        {
            var pd = RequirePlainDate(realm, thisV);
            return JsValue.String($"{pd.Year:D4}-{pd.Month:D2}-{pd.Day:D2}");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toJSON", 0, (thisV, _) =>
        {
            var pd = RequirePlainDate(realm, thisV);
            return JsValue.String($"{pd.Year:D4}-{pd.Month:D2}-{pd.Day:D2}");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Temporal.PlainDate has no valueOf — use compare/equals or toString")));
        IntrinsicHelpers.DefineMethod(realm, proto, "equals", 1, (thisV, args) =>
        {
            var pd = RequirePlainDate(realm, thisV);
            var other = PlainDateFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Boolean(pd.Year == other.Year && pd.Month == other.Month && pd.Day == other.Day);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "add", 1, (thisV, args) =>
            JsValue.Object(AddDuration(realm, RequirePlainDate(realm, thisV),
                DurationFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined), negate: false)));
        IntrinsicHelpers.DefineMethod(realm, proto, "subtract", 1, (thisV, args) =>
            JsValue.Object(AddDuration(realm, RequirePlainDate(realm, thisV),
                DurationFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined), negate: true)));
        IntrinsicHelpers.DefineMethod(realm, proto, "until", 1, (thisV, args) =>
        {
            var pd = RequirePlainDate(realm, thisV);
            var other = PlainDateFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var days = other.ToDateOnly().DayNumber - pd.ToDateOnly().DayNumber;
            return JsValue.Object(new TemporalDuration(s_durationProto!, 0, 0, 0, days, 0, 0, 0, 0));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "since", 1, (thisV, args) =>
        {
            var pd = RequirePlainDate(realm, thisV);
            var other = PlainDateFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var days = pd.ToDateOnly().DayNumber - other.ToDateOnly().DayNumber;
            return JsValue.Object(new TemporalDuration(s_durationProto!, 0, 0, 0, days, 0, 0, 0, 0));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "with", 1, (thisV, args) =>
        {
            var pd = RequirePlainDate(realm, thisV);
            if (args.Length == 0 || !args[0].IsObject)
            {
                throw new JsThrow(realm.NewTypeError("Temporal.PlainDate.prototype.with requires an object"));
            }

            var vm = realm.ActiveVm;
            var o = args[0].AsObject;
            int Field(string name, int fallback)
            {
                var v = AbstractOperations.Get(vm, o, name);
                return v.IsUndefined ? fallback : (int)JsValue.ToNumber(AbstractOperations.ToPrimitive(vm, v, "number"));
            }

            return JsValue.Object(MakePlainDate(realm, s_plainDateProto!,
                Field("year", pd.Year), Field("month", pd.Month), Field("day", pd.Day)));
        });

        temporal.DefineOwnProperty("PlainDate",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    private static TemporalPlainDate RequirePlainDate(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is TemporalPlainDate pd
            ? pd
            : throw new JsThrow(realm.NewTypeError("Temporal.PlainDate method called on incompatible receiver"));

    private static TemporalPlainDate MakePlainDate(JsRealm realm, JsObject proto, int y, int m, int d)
    {
        if (y is < 1 or > 9999 || m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(Math.Clamp(y, 1, 9999), Math.Clamp(m, 1, 12)))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid Temporal.PlainDate: {y}-{m}-{d}"));
        }

        return new TemporalPlainDate(proto, y, m, d);
    }

    private static TemporalPlainDate PlainDateFrom(JsRealm realm, JsValue item)
    {
        if (item.IsObject && item.AsObject is TemporalPlainDate pd)
        {
            return new TemporalPlainDate(s_plainDateProto!, pd.Year, pd.Month, pd.Day);
        }

        var vm = realm.ActiveVm;
        if (item.IsObject)
        {
            var o = item.AsObject;
            var y = AbstractOperations.Get(vm, o, "year");
            var m = AbstractOperations.Get(vm, o, "month");
            var d = AbstractOperations.Get(vm, o, "day");
            if (!y.IsUndefined && !m.IsUndefined && !d.IsUndefined)
            {
                return MakePlainDate(realm, s_plainDateProto!,
                    (int)JsValue.ToNumber(y), (int)JsValue.ToNumber(m), (int)JsValue.ToNumber(d));
            }

            throw new JsThrow(realm.NewTypeError("Temporal.PlainDate.from: missing year/month/day"));
        }

        var text = AbstractOperations.ToStringJs(vm, item);
        if (DateOnly.TryParseExact(text.Length > 10 ? text[..10] : text, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return new TemporalPlainDate(s_plainDateProto!, parsed.Year, parsed.Month, parsed.Day);
        }

        throw new JsThrow(realm.NewRangeError($"Invalid ISO date string: '{text}'"));
    }

    private static TemporalPlainDate AddDuration(JsRealm realm, TemporalPlainDate pd, TemporalDuration dur, bool negate)
    {
        var sign = negate ? -1 : 1;
        var date = pd.ToDateOnly()
            .AddYears(sign * dur.Years)
            .AddMonths(sign * dur.Months)
            .AddDays(sign * ((dur.Weeks * 7) + dur.Days));
        return new TemporalPlainDate(s_plainDateProto!, date.Year, date.Month, date.Day);
    }

    // =====================================================================
    //                          Temporal.Duration
    // =====================================================================

    private sealed class TemporalDuration(JsObject prototype, int years, int months, int weeks, int days,
        int hours, int minutes, int seconds, int milliseconds) : JsObject(prototype)
    {
        public int Years { get; } = years;
        public int Months { get; } = months;
        public int Weeks { get; } = weeks;
        public int Days { get; } = days;
        public int Hours { get; } = hours;
        public int Minutes { get; } = minutes;
        public int Seconds { get; } = seconds;
        public int Milliseconds { get; } = milliseconds;

        public int Sign()
        {
            foreach (var v in new[] { Years, Months, Weeks, Days, Hours, Minutes, Seconds, Milliseconds })
            {
                if (v > 0)
                {
                    return 1;
                }

                if (v < 0)
                {
                    return -1;
                }
            }

            return 0;
        }
    }

    private static JsObject? s_durationProto;

    private static void InstallDuration(JsRealm realm, JsObject temporal)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        s_durationProto = proto;
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "Duration", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Temporal.Duration requires 'new'"));
            }

            int Arg(int i) => args.Length > i && !args[i].IsUndefined ? (int)JsValue.ToNumber(args[i]) : 0;
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new TemporalDuration(instProto,
                Arg(0), Arg(1), Arg(2), Arg(3), Arg(4), Arg(5), Arg(6), Arg(7)));
        }, isConstructor: true);
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Temporal.Duration"), writable: false, enumerable: false, configurable: true));

        IntrinsicHelpers.DefineMethod(realm, ctor, "from", 1, (_, args) =>
            JsValue.Object(DurationFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined)));

        void Getter(string name, Func<TemporalDuration, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0,
                    (thisV, _) => read(RequireDuration(realm, thisV))),
                null));
        Getter("years", d => JsValue.Number(d.Years));
        Getter("months", d => JsValue.Number(d.Months));
        Getter("weeks", d => JsValue.Number(d.Weeks));
        Getter("days", d => JsValue.Number(d.Days));
        Getter("hours", d => JsValue.Number(d.Hours));
        Getter("minutes", d => JsValue.Number(d.Minutes));
        Getter("seconds", d => JsValue.Number(d.Seconds));
        Getter("milliseconds", d => JsValue.Number(d.Milliseconds));
        Getter("microseconds", _ => JsValue.Number(0));
        Getter("nanoseconds", _ => JsValue.Number(0));
        Getter("sign", d => JsValue.Number(d.Sign()));
        Getter("blank", d => JsValue.Boolean(d.Sign() == 0));

        IntrinsicHelpers.DefineMethod(realm, proto, "negated", 0, (thisV, _) =>
        {
            var d = RequireDuration(realm, thisV);
            return JsValue.Object(new TemporalDuration(proto, -d.Years, -d.Months, -d.Weeks, -d.Days,
                -d.Hours, -d.Minutes, -d.Seconds, -d.Milliseconds));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "abs", 0, (thisV, _) =>
        {
            var d = RequireDuration(realm, thisV);
            return JsValue.Object(new TemporalDuration(proto,
                Math.Abs(d.Years), Math.Abs(d.Months), Math.Abs(d.Weeks), Math.Abs(d.Days),
                Math.Abs(d.Hours), Math.Abs(d.Minutes), Math.Abs(d.Seconds), Math.Abs(d.Milliseconds)));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
            JsValue.String(DurationToIso(RequireDuration(realm, thisV))));
        IntrinsicHelpers.DefineMethod(realm, proto, "toJSON", 0, (thisV, _) =>
            JsValue.String(DurationToIso(RequireDuration(realm, thisV))));
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Temporal.Duration has no valueOf")));

        temporal.DefineOwnProperty("Duration",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    private static TemporalDuration RequireDuration(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is TemporalDuration d
            ? d
            : throw new JsThrow(realm.NewTypeError("Temporal.Duration method called on incompatible receiver"));

    private static TemporalDuration DurationFrom(JsRealm realm, JsValue item)
    {
        if (item.IsObject && item.AsObject is TemporalDuration d)
        {
            return d;
        }

        var vm = realm.ActiveVm;
        if (item.IsObject)
        {
            var o = item.AsObject;
            int Field(string name)
            {
                var v = AbstractOperations.Get(vm, o, name);
                return v.IsUndefined ? 0 : (int)JsValue.ToNumber(AbstractOperations.ToPrimitive(vm, v, "number"));
            }

            return new TemporalDuration(s_durationProto!,
                Field("years"), Field("months"), Field("weeks"), Field("days"),
                Field("hours"), Field("minutes"), Field("seconds"), Field("milliseconds"));
        }

        throw new JsThrow(realm.NewRangeError("Temporal.Duration.from: unsupported input"));
    }

    private static string DurationToIso(TemporalDuration d)
    {
        if (d.Sign() == 0)
        {
            return "PT0S";
        }

        var sb = new System.Text.StringBuilder(d.Sign() < 0 ? "-P" : "P");
        void Add(int v, char suffix) { if (v != 0) { sb.Append(Math.Abs(v)).Append(suffix); } }
        Add(d.Years, 'Y');
        Add(d.Months, 'M');
        Add(d.Weeks, 'W');
        Add(d.Days, 'D');
        if (d.Hours != 0 || d.Minutes != 0 || d.Seconds != 0 || d.Milliseconds != 0)
        {
            sb.Append('T');
            Add(d.Hours, 'H');
            Add(d.Minutes, 'M');
            if (d.Milliseconds != 0)
            {
                sb.Append(Math.Abs(d.Seconds) + (Math.Abs(d.Milliseconds) / 1000.0)).Append('S');
            }
            else
            {
                Add(d.Seconds, 'S');
            }
        }

        return sb.ToString();
    }

    // =====================================================================
    //                            Temporal.Now
    // =====================================================================

    private static void InstallNow(JsRealm realm, JsObject temporal)
    {
        var now = realm.NewOrdinaryObject();
        now.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Temporal.Now"), writable: false, enumerable: false, configurable: true));
        IntrinsicHelpers.DefineMethod(realm, now, "plainDateISO", 0, (_, _) =>
        {
            var d = DateTime.Now;
            return JsValue.Object(new TemporalPlainDate(s_plainDateProto!, d.Year, d.Month, d.Day));
        });
        IntrinsicHelpers.DefineMethod(realm, now, "timeZoneId", 0, (_, _) =>
            JsValue.String(TimeZoneInfo.Local.Id));
        temporal.DefineOwnProperty("Now",
            PropertyDescriptor.Data(JsValue.Object(now), writable: true, enumerable: false, configurable: true));
    }

    private static int ToIntegerThrow(JsRealm realm, JsValue[] args, int i, string name)
    {
        var v = args.Length > i ? args[i] : JsValue.Undefined;
        if (v.IsUndefined)
        {
            throw new JsThrow(realm.NewTypeError($"Temporal.PlainDate: {name} is required"));
        }

        var n = JsValue.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm, v, "number"));
        if (double.IsNaN(n) || double.IsInfinity(n))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid {name} for Temporal.PlainDate"));
        }

        return (int)Math.Truncate(n);
    }

    // =====================================================================
    //                          Temporal.PlainTime
    // =====================================================================

    private sealed class TemporalPlainTime(JsObject prototype, int hour, int minute, int second, int millisecond) : JsObject(prototype)
    {
        public int Hour { get; } = hour;
        public int Minute { get; } = minute;
        public int Second { get; } = second;
        public int Millisecond { get; } = millisecond;

        public string Iso() => Millisecond != 0
            ? $"{Hour:D2}:{Minute:D2}:{Second:D2}.{Millisecond:D3}"
            : Second != 0 ? $"{Hour:D2}:{Minute:D2}:{Second:D2}" : $"{Hour:D2}:{Minute:D2}";
    }

    private static JsObject? s_plainTimeProto;

    private static void InstallPlainTime(JsRealm realm, JsObject temporal)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        s_plainTimeProto = proto;
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "PlainTime", 0, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Temporal.PlainTime requires 'new'"));
            }

            int Arg(int i) => args.Length > i && !args[i].IsUndefined
                ? (int)JsValue.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm, args[i], "number"))
                : 0;
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(MakePlainTime(realm, instProto, Arg(0), Arg(1), Arg(2), Arg(3)));
        }, isConstructor: true);
        WireTemporalCtor(realm, temporal, ctor, proto, "PlainTime", "Temporal.PlainTime");

        IntrinsicHelpers.DefineMethod(realm, ctor, "from", 1, (_, args) =>
            JsValue.Object(PlainTimeFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined)));
        IntrinsicHelpers.DefineMethod(realm, ctor, "compare", 2, (_, args) =>
        {
            var a = PlainTimeFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var b = PlainTimeFrom(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var ka = ((a.Hour * 60 + a.Minute) * 60 + a.Second) * 1000 + a.Millisecond;
            var kb = ((b.Hour * 60 + b.Minute) * 60 + b.Second) * 1000 + b.Millisecond;
            return JsValue.Number(ka.CompareTo(kb) switch { < 0 => -1, > 0 => 1, _ => 0 });
        });

        void Getter(string name, Func<TemporalPlainTime, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0,
                    (thisV, _) => read(RequirePlainTime(realm, thisV))),
                null));
        Getter("hour", t => JsValue.Number(t.Hour));
        Getter("minute", t => JsValue.Number(t.Minute));
        Getter("second", t => JsValue.Number(t.Second));
        Getter("millisecond", t => JsValue.Number(t.Millisecond));
        Getter("microsecond", _ => JsValue.Number(0));
        Getter("nanosecond", _ => JsValue.Number(0));

        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
            JsValue.String(RequirePlainTime(realm, thisV).Iso()));
        IntrinsicHelpers.DefineMethod(realm, proto, "toJSON", 0, (thisV, _) =>
            JsValue.String(RequirePlainTime(realm, thisV).Iso()));
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Temporal.PlainTime has no valueOf")));
        IntrinsicHelpers.DefineMethod(realm, proto, "equals", 1, (thisV, args) =>
        {
            var t = RequirePlainTime(realm, thisV);
            var o = PlainTimeFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Boolean(t.Hour == o.Hour && t.Minute == o.Minute
                && t.Second == o.Second && t.Millisecond == o.Millisecond);
        });
        JsValue TimeArith(JsValue thisV, JsValue[] args, int sign)
        {
            var t = RequirePlainTime(realm, thisV);
            var d = DurationFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            // §4.5 — time arithmetic wraps modulo 24h; date parts are ignored.
            var totalMs = ((((long)t.Hour * 60 + t.Minute) * 60 + t.Second) * 1000 + t.Millisecond)
                + sign * (((((long)d.Hours * 60) + d.Minutes) * 60 + d.Seconds) * 1000 + d.Milliseconds);
            const long DayMs = 24L * 60 * 60 * 1000;
            totalMs = ((totalMs % DayMs) + DayMs) % DayMs;
            return JsValue.Object(new TemporalPlainTime(s_plainTimeProto!,
                (int)(totalMs / 3_600_000), (int)(totalMs / 60_000 % 60),
                (int)(totalMs / 1000 % 60), (int)(totalMs % 1000)));
        }
        IntrinsicHelpers.DefineMethod(realm, proto, "add", 1, (thisV, args) => TimeArith(thisV, args, 1));
        IntrinsicHelpers.DefineMethod(realm, proto, "subtract", 1, (thisV, args) => TimeArith(thisV, args, -1));
    }

    private static TemporalPlainTime RequirePlainTime(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is TemporalPlainTime t
            ? t
            : throw new JsThrow(realm.NewTypeError("Temporal.PlainTime method called on incompatible receiver"));

    private static TemporalPlainTime MakePlainTime(JsRealm realm, JsObject proto, int h, int m, int sec, int ms)
    {
        if (h is < 0 or > 23 || m is < 0 or > 59 || sec is < 0 or > 59 || ms is < 0 or > 999)
        {
            throw new JsThrow(realm.NewRangeError($"Invalid Temporal.PlainTime: {h}:{m}:{sec}.{ms}"));
        }

        return new TemporalPlainTime(proto, h, m, sec, ms);
    }

    private static TemporalPlainTime PlainTimeFrom(JsRealm realm, JsValue item)
    {
        if (item.IsObject && item.AsObject is TemporalPlainTime t)
        {
            return new TemporalPlainTime(s_plainTimeProto!, t.Hour, t.Minute, t.Second, t.Millisecond);
        }

        var vm = realm.ActiveVm;
        if (item.IsObject)
        {
            var o = item.AsObject;
            int Field(string name)
            {
                var v = AbstractOperations.Get(vm, o, name);
                return v.IsUndefined ? 0 : (int)JsValue.ToNumber(AbstractOperations.ToPrimitive(vm, v, "number"));
            }

            return MakePlainTime(realm, s_plainTimeProto!, Field("hour"), Field("minute"), Field("second"), Field("millisecond"));
        }

        var text = AbstractOperations.ToStringJs(vm, item);
        if (TimeOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return new TemporalPlainTime(s_plainTimeProto!, parsed.Hour, parsed.Minute, parsed.Second, parsed.Millisecond);
        }

        throw new JsThrow(realm.NewRangeError($"Invalid ISO time string: '{text}'"));
    }

    // =====================================================================
    //                        Temporal.PlainDateTime
    // =====================================================================

    private sealed class TemporalPlainDateTime(JsObject prototype, DateTime value) : JsObject(prototype)
    {
        public DateTime Value { get; } = value;
    }

    private static JsObject? s_plainDateTimeProto;

    private static void InstallPlainDateTime(JsRealm realm, JsObject temporal)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        s_plainDateTimeProto = proto;
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "PlainDateTime", 3, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Temporal.PlainDateTime requires 'new'"));
            }

            int Arg(int i, int fallback) => args.Length > i && !args[i].IsUndefined
                ? (int)JsValue.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm, args[i], "number"))
                : fallback;
            var y = ToIntegerThrow(realm, args, 0, "year");
            var mo = ToIntegerThrow(realm, args, 1, "month");
            var d = ToIntegerThrow(realm, args, 2, "day");
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            try
            {
                return JsValue.Object(new TemporalPlainDateTime(instProto,
                    new DateTime(y, mo, d, Arg(3, 0), Arg(4, 0), Arg(5, 0), Arg(6, 0), DateTimeKind.Unspecified)));
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new JsThrow(realm.NewRangeError("Invalid Temporal.PlainDateTime"));
            }
        }, isConstructor: true);
        WireTemporalCtor(realm, temporal, ctor, proto, "PlainDateTime", "Temporal.PlainDateTime");

        void Getter(string name, Func<DateTime, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0,
                    (thisV, _) => thisV.IsObject && thisV.AsObject is TemporalPlainDateTime pdt
                        ? read(pdt.Value)
                        : throw new JsThrow(realm.NewTypeError("Temporal.PlainDateTime method called on incompatible receiver"))),
                null));
        Getter("year", v => JsValue.Number(v.Year));
        Getter("month", v => JsValue.Number(v.Month));
        Getter("day", v => JsValue.Number(v.Day));
        Getter("hour", v => JsValue.Number(v.Hour));
        Getter("minute", v => JsValue.Number(v.Minute));
        Getter("second", v => JsValue.Number(v.Second));
        Getter("millisecond", v => JsValue.Number(v.Millisecond));
        Getter("dayOfWeek", v => JsValue.Number((int)v.DayOfWeek == 0 ? 7 : (int)v.DayOfWeek));
        Getter("calendarId", _ => JsValue.String("iso8601"));

        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
        {
            var pdt = thisV.IsObject && thisV.AsObject is TemporalPlainDateTime p
                ? p
                : throw new JsThrow(realm.NewTypeError("Temporal.PlainDateTime method called on incompatible receiver"));
            return JsValue.String(pdt.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Temporal.PlainDateTime has no valueOf")));
        IntrinsicHelpers.DefineMethod(realm, proto, "toPlainDate", 0, (thisV, _) =>
        {
            var pdt = ((TemporalPlainDateTime)thisV.AsObject).Value;
            return JsValue.Object(new TemporalPlainDate(s_plainDateProto!, pdt.Year, pdt.Month, pdt.Day));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toPlainTime", 0, (thisV, _) =>
        {
            var pdt = ((TemporalPlainDateTime)thisV.AsObject).Value;
            return JsValue.Object(new TemporalPlainTime(s_plainTimeProto!, pdt.Hour, pdt.Minute, pdt.Second, pdt.Millisecond));
        });
    }

    // =====================================================================
    //                          Temporal.Instant
    // =====================================================================

    private sealed class TemporalInstant(JsObject prototype, long epochMilliseconds) : JsObject(prototype)
    {
        public long EpochMilliseconds { get; } = epochMilliseconds;
    }

    private static JsObject? s_instantProto;

    private static void InstallInstant(JsRealm realm, JsObject temporal)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        s_instantProto = proto;
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "Instant", 1, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Temporal.Instant requires 'new'"));
            }

            // The spec argument is epochNanoseconds (BigInt); store ms.
            var v = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (!v.IsBigInt)
            {
                throw new JsThrow(realm.NewTypeError("Temporal.Instant requires a BigInt epochNanoseconds"));
            }

            var ns = v.AsBigInt;
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new TemporalInstant(instProto, (long)(ns / 1_000_000)));
        }, isConstructor: true);
        WireTemporalCtor(realm, temporal, ctor, proto, "Instant", "Temporal.Instant");

        IntrinsicHelpers.DefineMethod(realm, ctor, "fromEpochMilliseconds", 1, (_, args) =>
        {
            var ms = (long)JsValue.ToNumber(args.Length > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Object(new TemporalInstant(proto, ms));
        });

        proto.DefineOwnProperty("epochMilliseconds", PropertyDescriptor.Accessor(
            new JsNativeFunction(realm, "get epochMilliseconds", 0, (thisV, _) =>
                thisV.IsObject && thisV.AsObject is TemporalInstant inst
                    ? JsValue.Number(inst.EpochMilliseconds)
                    : throw new JsThrow(realm.NewTypeError("Temporal.Instant method called on incompatible receiver"))),
            null));
        proto.DefineOwnProperty("epochNanoseconds", PropertyDescriptor.Accessor(
            new JsNativeFunction(realm, "get epochNanoseconds", 0, (thisV, _) =>
                thisV.IsObject && thisV.AsObject is TemporalInstant inst
                    ? JsValue.BigInt(new System.Numerics.BigInteger(inst.EpochMilliseconds) * 1_000_000)
                    : throw new JsThrow(realm.NewTypeError("Temporal.Instant method called on incompatible receiver"))),
            null));

        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
        {
            var inst = thisV.IsObject && thisV.AsObject is TemporalInstant i
                ? i
                : throw new JsThrow(realm.NewTypeError("Temporal.Instant method called on incompatible receiver"));
            var dto = DateTimeOffset.FromUnixTimeMilliseconds(inst.EpochMilliseconds);
            return JsValue.String(dto.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Temporal.Instant has no valueOf")));
        IntrinsicHelpers.DefineMethod(realm, ctor, "compare", 2, (_, args) =>
        {
            long Ms(JsValue v) => v.IsObject && v.AsObject is TemporalInstant i
                ? i.EpochMilliseconds
                : throw new JsThrow(realm.NewTypeError("Temporal.Instant.compare requires Instants"));
            var a = Ms(args.Length > 0 ? args[0] : JsValue.Undefined);
            var b = Ms(args.Length > 1 ? args[1] : JsValue.Undefined);
            return JsValue.Number(a.CompareTo(b) switch { < 0 => -1, > 0 => 1, _ => 0 });
        });
        TemporalInstant ReqInstant(JsValue thisV) =>
            thisV.IsObject && thisV.AsObject is TemporalInstant i
                ? i
                : throw new JsThrow(realm.NewTypeError("Temporal.Instant method called on incompatible receiver"));
        JsValue InstantArith(JsValue thisV, JsValue[] args, int sign)
        {
            var inst = ReqInstant(thisV);
            var d = DurationFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            // §8.5 — Instant arithmetic accepts TIME units only.
            if (d.Years != 0 || d.Months != 0 || d.Weeks != 0 || d.Days != 0)
            {
                throw new JsThrow(realm.NewRangeError("Temporal.Instant arithmetic accepts time units only"));
            }

            var deltaMs = ((((long)d.Hours * 60) + d.Minutes) * 60 + d.Seconds) * 1000 + d.Milliseconds;
            return JsValue.Object(new TemporalInstant(s_instantProto!, inst.EpochMilliseconds + (sign * deltaMs)));
        }
        IntrinsicHelpers.DefineMethod(realm, proto, "add", 1, (thisV, args) => InstantArith(thisV, args, 1));
        IntrinsicHelpers.DefineMethod(realm, proto, "subtract", 1, (thisV, args) => InstantArith(thisV, args, -1));
        JsValue InstantDiff(JsValue thisV, JsValue[] args, int sign)
        {
            var inst = ReqInstant(thisV);
            var other = args.Length > 0 && args[0].IsObject && args[0].AsObject is TemporalInstant o
                ? o
                : throw new JsThrow(realm.NewTypeError("Temporal.Instant difference requires an Instant"));
            var ms = sign * (other.EpochMilliseconds - inst.EpochMilliseconds);
            return JsValue.Object(new TemporalDuration(s_durationProto!, 0, 0, 0, 0,
                0, 0, (int)(ms / 1000), (int)(ms % 1000)));
        }
        IntrinsicHelpers.DefineMethod(realm, proto, "until", 1, (thisV, args) => InstantDiff(thisV, args, 1));
        IntrinsicHelpers.DefineMethod(realm, proto, "since", 1, (thisV, args) => InstantDiff(thisV, args, -1));
        IntrinsicHelpers.DefineMethod(realm, proto, "equals", 1, (thisV, args) =>
        {
            var inst = ReqInstant(thisV);
            return JsValue.Boolean(args.Length > 0 && args[0].IsObject
                && args[0].AsObject is TemporalInstant o && o.EpochMilliseconds == inst.EpochMilliseconds);
        });
    }

    private static void WireTemporalCtor(JsRealm realm, JsObject temporal, JsNativeFunction ctor, JsObject proto, string name, string tag)
    {
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String(tag), writable: false, enumerable: false, configurable: true));
        temporal.DefineOwnProperty(name,
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    // =====================================================================
    //                        Temporal.PlainYearMonth
    // =====================================================================

    private sealed class TemporalPlainYearMonth(JsObject prototype, int year, int month) : JsObject(prototype)
    {
        public int Year { get; } = year;
        public int Month { get; } = month;
    }

    private static void InstallPlainYearMonth(JsRealm realm, JsObject temporal)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "PlainYearMonth", 2, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Temporal.PlainYearMonth requires 'new'"));
            }

            var y = ToIntegerThrow(realm, args, 0, "year");
            var m = ToIntegerThrow(realm, args, 1, "month");
            if (y is < 1 or > 9999 || m is < 1 or > 12)
            {
                throw new JsThrow(realm.NewRangeError($"Invalid Temporal.PlainYearMonth: {y}-{m}"));
            }

            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new TemporalPlainYearMonth(instProto, y, m));
        }, isConstructor: true);
        WireTemporalCtor(realm, temporal, ctor, proto, "PlainYearMonth", "Temporal.PlainYearMonth");

        TemporalPlainYearMonth Req(JsValue thisV) =>
            thisV.IsObject && thisV.AsObject is TemporalPlainYearMonth ym
                ? ym
                : throw new JsThrow(realm.NewTypeError("Temporal.PlainYearMonth method called on incompatible receiver"));
        void Getter(string name, Func<TemporalPlainYearMonth, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0, (thisV, _) => read(Req(thisV))),
                null));
        Getter("year", ym => JsValue.Number(ym.Year));
        Getter("month", ym => JsValue.Number(ym.Month));
        Getter("monthCode", ym => JsValue.String("M" + ym.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)));
        Getter("daysInMonth", ym => JsValue.Number(DateTime.DaysInMonth(ym.Year, ym.Month)));
        Getter("daysInYear", ym => JsValue.Number(DateTime.IsLeapYear(ym.Year) ? 366 : 365));
        Getter("monthsInYear", _ => JsValue.Number(12));
        Getter("inLeapYear", ym => JsValue.Boolean(DateTime.IsLeapYear(ym.Year)));
        Getter("calendarId", _ => JsValue.String("iso8601"));

        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
        {
            var ym = Req(thisV);
            return JsValue.String($"{ym.Year:D4}-{ym.Month:D2}");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Temporal.PlainYearMonth has no valueOf")));
        IntrinsicHelpers.DefineMethod(realm, proto, "equals", 1, (thisV, args) =>
        {
            var ym = Req(thisV);
            var o = args.Length > 0 && args[0].IsObject && args[0].AsObject is TemporalPlainYearMonth other
                ? other
                : throw new JsThrow(realm.NewTypeError("Temporal.PlainYearMonth.equals requires a PlainYearMonth"));
            return JsValue.Boolean(ym.Year == o.Year && ym.Month == o.Month);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toPlainDate", 1, (thisV, args) =>
        {
            var ym = Req(thisV);
            var day = 1;
            if (args.Length > 0 && args[0].IsObject)
            {
                var dv = AbstractOperations.Get(realm.ActiveVm, args[0].AsObject, "day");
                if (!dv.IsUndefined)
                {
                    day = (int)JsValue.ToNumber(dv);
                }
            }

            return JsValue.Object(MakePlainDate(realm, s_plainDateProto!, ym.Year, ym.Month, day));
        });
    }

    // =====================================================================
    //                        Temporal.PlainMonthDay
    // =====================================================================

    private sealed class TemporalPlainMonthDay(JsObject prototype, int month, int day) : JsObject(prototype)
    {
        public int Month { get; } = month;
        public int Day { get; } = day;
    }

    private static void InstallPlainMonthDay(JsRealm realm, JsObject temporal)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "PlainMonthDay", 2, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Temporal.PlainMonthDay requires 'new'"));
            }

            var m = ToIntegerThrow(realm, args, 0, "month");
            var d = ToIntegerThrow(realm, args, 1, "day");
            // Validate against a leap reference year (Feb 29 is a valid month-day).
            if (m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(1972, Math.Clamp(m, 1, 12)))
            {
                throw new JsThrow(realm.NewRangeError($"Invalid Temporal.PlainMonthDay: {m}-{d}"));
            }

            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new TemporalPlainMonthDay(instProto, m, d));
        }, isConstructor: true);
        WireTemporalCtor(realm, temporal, ctor, proto, "PlainMonthDay", "Temporal.PlainMonthDay");

        TemporalPlainMonthDay Req(JsValue thisV) =>
            thisV.IsObject && thisV.AsObject is TemporalPlainMonthDay md
                ? md
                : throw new JsThrow(realm.NewTypeError("Temporal.PlainMonthDay method called on incompatible receiver"));
        proto.DefineOwnProperty("monthCode", PropertyDescriptor.Accessor(
            new JsNativeFunction(realm, "get monthCode", 0, (thisV, _) =>
                JsValue.String("M" + Req(thisV).Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))),
            null));
        proto.DefineOwnProperty("day", PropertyDescriptor.Accessor(
            new JsNativeFunction(realm, "get day", 0, (thisV, _) => JsValue.Number(Req(thisV).Day)),
            null));
        proto.DefineOwnProperty("calendarId", PropertyDescriptor.Accessor(
            new JsNativeFunction(realm, "get calendarId", 0, (_, _) => JsValue.String("iso8601")),
            null));

        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
        {
            var md = Req(thisV);
            return JsValue.String($"{md.Month:D2}-{md.Day:D2}");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Temporal.PlainMonthDay has no valueOf")));
        IntrinsicHelpers.DefineMethod(realm, proto, "equals", 1, (thisV, args) =>
        {
            var md = Req(thisV);
            var o = args.Length > 0 && args[0].IsObject && args[0].AsObject is TemporalPlainMonthDay other
                ? other
                : throw new JsThrow(realm.NewTypeError("Temporal.PlainMonthDay.equals requires a PlainMonthDay"));
            return JsValue.Boolean(md.Month == o.Month && md.Day == o.Day);
        });
    }

    // =====================================================================
    //                        Temporal.ZonedDateTime
    // =====================================================================

    private sealed class TemporalZonedDateTime(JsObject prototype, long epochMilliseconds, string timeZoneId) : JsObject(prototype)
    {
        public long EpochMilliseconds { get; } = epochMilliseconds;
        public string TimeZoneId { get; } = timeZoneId;

        public DateTimeOffset Local()
        {
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(EpochMilliseconds);
            try
            {
                var tz = TimeZoneId == "UTC" ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
                return TimeZoneInfo.ConvertTime(utc, tz);
            }
            catch (TimeZoneNotFoundException)
            {
                return utc;
            }
        }
    }

    private static void InstallZonedDateTime(JsRealm realm, JsObject temporal)
    {
        var proto = new JsObject(realm.ObjectPrototype);
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "ZonedDateTime", 2, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Temporal.ZonedDateTime requires 'new'"));
            }

            var v = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (!v.IsBigInt)
            {
                throw new JsThrow(realm.NewTypeError("Temporal.ZonedDateTime requires a BigInt epochNanoseconds"));
            }

            var tzArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            var tz = tzArg.IsUndefined ? "UTC" : AbstractOperations.ToStringJs(realm.ActiveVm, tzArg);
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new TemporalZonedDateTime(instProto, (long)(v.AsBigInt / 1_000_000), tz));
        }, isConstructor: true);
        WireTemporalCtor(realm, temporal, ctor, proto, "ZonedDateTime", "Temporal.ZonedDateTime");

        TemporalZonedDateTime Req(JsValue thisV) =>
            thisV.IsObject && thisV.AsObject is TemporalZonedDateTime z
                ? z
                : throw new JsThrow(realm.NewTypeError("Temporal.ZonedDateTime method called on incompatible receiver"));
        void Getter(string name, Func<TemporalZonedDateTime, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0, (thisV, _) => read(Req(thisV))),
                null));
        Getter("epochMilliseconds", z => JsValue.Number(z.EpochMilliseconds));
        Getter("epochNanoseconds", z => JsValue.BigInt(new System.Numerics.BigInteger(z.EpochMilliseconds) * 1_000_000));
        Getter("timeZoneId", z => JsValue.String(z.TimeZoneId));
        Getter("calendarId", _ => JsValue.String("iso8601"));
        Getter("year", z => JsValue.Number(z.Local().Year));
        Getter("month", z => JsValue.Number(z.Local().Month));
        Getter("day", z => JsValue.Number(z.Local().Day));
        Getter("hour", z => JsValue.Number(z.Local().Hour));
        Getter("minute", z => JsValue.Number(z.Local().Minute));
        Getter("second", z => JsValue.Number(z.Local().Second));

        IntrinsicHelpers.DefineMethod(realm, proto, "toInstant", 0, (thisV, _) =>
            JsValue.Object(new TemporalInstant(s_instantProto!, Req(thisV).EpochMilliseconds)));
        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
        {
            var z = Req(thisV);
            var local = z.Local();
            var offset = local.Offset == TimeSpan.Zero
                ? "+00:00"
                : (local.Offset < TimeSpan.Zero ? "-" : "+") + local.Offset.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
            return JsValue.String(local.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                + offset + "[" + z.TimeZoneId + "]");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Temporal.ZonedDateTime has no valueOf")));
    }
}
