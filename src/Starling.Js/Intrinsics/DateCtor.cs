using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §21.4 The Date constructor and §21.4.4 Date.prototype. The time value is a
/// millisecond <c>double</c> (NaN = invalid) and every calendar operation is
/// computed directly on it (§21.4.1 MakeDay/MakeTime/TimeClip), so the full
/// ±8.64e15 ms range — years −271821..275760 — works without the narrower
/// range limits of the host date types.
/// </summary>
/// <remarks>
/// Locale is intentionally invariant — every <c>toLocale*</c> method delegates
/// to its non-locale sibling, and <c>getTimezoneOffset</c> returns 0 since
/// Starling treats the local timezone as UTC for reproducibility. The browser
/// would otherwise pull the host's TZ + culture, which makes snapshot rendering
/// non-deterministic across machines.
/// </remarks>
public static class DateCtor
{
    private static readonly string[] WeekdayShort = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    private static readonly string[] MonthShort = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    private const double MsPerDay = 86_400_000d;
    private const double MsPerHour = 3_600_000d;
    private const double MsPerMinute = 60_000d;
    private const double MsPerSecond = 1_000d;
    private const double MaxTimeValue = 8.64e15;

    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.DatePrototype;

        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(realm, "Date", length: 7, (newTarget, args) =>
        {
            // §21.4.2.1 — Called without new, return the current date as a
            // string regardless of args (spec oddity that real bundles use to
            // sniff host locale formatting; we return a stable UTC string).
            var calledAsConstructor = IntrinsicHelpers.IsConstructInvocation(newTarget);
            if (!calledAsConstructor)
            {
                return JsValue.String(FormatToString(NowMs()));
            }

            double ms;
            if (args.Length == 0)
            {
                ms = NowMs();
            }
            else if (args.Length == 1)
            {
                var v = args[0];
                if (v.IsObject && v.AsObject is JsDate other)
                {
                    ms = other.TimeValueMs;
                }
                else
                {
                    var prim = v.IsObject
                        ? AbstractOperations.ToPrimitive(realm.ActiveVm, v, "default")
                        : v;
                    ms = prim.IsString ? ParseDate(prim.AsString) : ToNumberPrim(realm, prim);
                }
                ms = TimeClip(ms);
            }
            else
            {
                ms = TimeClip(MakeFromFields(realm, args));
            }
            // §21.4.2.1 step 7: OrdinaryCreateFromConstructor — prototype from
            // new.target so `class D extends Date {}` produces a D-prototyped
            // date, falling back to new.target's function realm per
            // GetPrototypeFromConstructor when its "prototype" is not an object.
            var date = new JsDate(realm, ms);
            var instProto = PrototypeFromNewTarget(realm, newTarget, proto);
            if (!ReferenceEquals(instProto, proto))
            {
                date.SetPrototypeOf(instProto);
            }

            return JsValue.Object(date);
        }, isConstructor: true);

        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));

        // Static methods.
        IntrinsicHelpers.DefineMethod(realm, ctor, "now", 0, (_, _) => JsValue.Number(NowMs()));
        IntrinsicHelpers.DefineMethod(realm, ctor, "parse", 1, (_, args) =>
            JsValue.Number(args.Length == 0 ? double.NaN : ParseDate(AbstractOperations.ToStringJs(realm.ActiveVm, args[0]))));
        IntrinsicHelpers.DefineMethod(realm, ctor, "UTC", 7, (_, args) => JsValue.Number(TimeClip(MakeFromFields(realm, args))));

        // Prototype getters.
        IntrinsicHelpers.DefineMethod(realm, proto, "getTime", 0, (thisV, _) => Time(realm, thisV));
        IntrinsicHelpers.DefineMethod(realm, proto, "valueOf", 0, (thisV, _) => Time(realm, thisV));
        IntrinsicHelpers.DefineMethod(realm, proto, "getFullYear", 0, (thisV, _) => GetField(realm, thisV, DateField.Year));
        IntrinsicHelpers.DefineMethod(realm, proto, "getMonth", 0, (thisV, _) => GetField(realm, thisV, DateField.Month));
        IntrinsicHelpers.DefineMethod(realm, proto, "getDate", 0, (thisV, _) => GetField(realm, thisV, DateField.Day));
        IntrinsicHelpers.DefineMethod(realm, proto, "getDay", 0, (thisV, _) => GetField(realm, thisV, DateField.Weekday));
        IntrinsicHelpers.DefineMethod(realm, proto, "getHours", 0, (thisV, _) => GetField(realm, thisV, DateField.Hours));
        IntrinsicHelpers.DefineMethod(realm, proto, "getMinutes", 0, (thisV, _) => GetField(realm, thisV, DateField.Minutes));
        IntrinsicHelpers.DefineMethod(realm, proto, "getSeconds", 0, (thisV, _) => GetField(realm, thisV, DateField.Seconds));
        IntrinsicHelpers.DefineMethod(realm, proto, "getMilliseconds", 0, (thisV, _) => GetField(realm, thisV, DateField.Milliseconds));
        IntrinsicHelpers.DefineMethod(realm, proto, "getTimezoneOffset", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            return d.IsValid ? JsValue.Number(0) : JsValue.NaN;
        });

        // UTC variants — equivalent under our invariant-UTC locale, but kept
        // distinct so dual lookups still resolve.
        IntrinsicHelpers.DefineMethod(realm, proto, "getUTCFullYear", 0, (thisV, _) => GetField(realm, thisV, DateField.Year));
        IntrinsicHelpers.DefineMethod(realm, proto, "getUTCMonth", 0, (thisV, _) => GetField(realm, thisV, DateField.Month));
        IntrinsicHelpers.DefineMethod(realm, proto, "getUTCDate", 0, (thisV, _) => GetField(realm, thisV, DateField.Day));
        IntrinsicHelpers.DefineMethod(realm, proto, "getUTCDay", 0, (thisV, _) => GetField(realm, thisV, DateField.Weekday));
        IntrinsicHelpers.DefineMethod(realm, proto, "getUTCHours", 0, (thisV, _) => GetField(realm, thisV, DateField.Hours));
        IntrinsicHelpers.DefineMethod(realm, proto, "getUTCMinutes", 0, (thisV, _) => GetField(realm, thisV, DateField.Minutes));
        IntrinsicHelpers.DefineMethod(realm, proto, "getUTCSeconds", 0, (thisV, _) => GetField(realm, thisV, DateField.Seconds));
        IntrinsicHelpers.DefineMethod(realm, proto, "getUTCMilliseconds", 0, (thisV, _) => GetField(realm, thisV, DateField.Milliseconds));

        // Setters.
        IntrinsicHelpers.DefineMethod(realm, proto, "setTime", 1, (thisV, args) =>
        {
            var d = RequireDate(realm, thisV);
            var ms = TimeClip(args.Length == 0 ? double.NaN : ToNumberArg(realm, args[0]));
            d.SetTimeMs(ms);
            return JsValue.Number(ms);
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "setFullYear", 3, (thisV, args) => SetParts(realm, thisV, args, DatePart.Year));
        IntrinsicHelpers.DefineMethod(realm, proto, "setMonth", 2, (thisV, args) => SetParts(realm, thisV, args, DatePart.Month));
        IntrinsicHelpers.DefineMethod(realm, proto, "setDate", 1, (thisV, args) => SetParts(realm, thisV, args, DatePart.Day));
        IntrinsicHelpers.DefineMethod(realm, proto, "setHours", 4, (thisV, args) => SetParts(realm, thisV, args, DatePart.Hours));
        IntrinsicHelpers.DefineMethod(realm, proto, "setMinutes", 3, (thisV, args) => SetParts(realm, thisV, args, DatePart.Minutes));
        IntrinsicHelpers.DefineMethod(realm, proto, "setSeconds", 2, (thisV, args) => SetParts(realm, thisV, args, DatePart.Seconds));
        IntrinsicHelpers.DefineMethod(realm, proto, "setMilliseconds", 1, (thisV, args) => SetParts(realm, thisV, args, DatePart.Milliseconds));
        IntrinsicHelpers.DefineMethod(realm, proto, "setUTCFullYear", 3, (thisV, args) => SetParts(realm, thisV, args, DatePart.Year));
        IntrinsicHelpers.DefineMethod(realm, proto, "setUTCMonth", 2, (thisV, args) => SetParts(realm, thisV, args, DatePart.Month));
        IntrinsicHelpers.DefineMethod(realm, proto, "setUTCDate", 1, (thisV, args) => SetParts(realm, thisV, args, DatePart.Day));
        IntrinsicHelpers.DefineMethod(realm, proto, "setUTCHours", 4, (thisV, args) => SetParts(realm, thisV, args, DatePart.Hours));
        IntrinsicHelpers.DefineMethod(realm, proto, "setUTCMinutes", 3, (thisV, args) => SetParts(realm, thisV, args, DatePart.Minutes));
        IntrinsicHelpers.DefineMethod(realm, proto, "setUTCSeconds", 2, (thisV, args) => SetParts(realm, thisV, args, DatePart.Seconds));
        IntrinsicHelpers.DefineMethod(realm, proto, "setUTCMilliseconds", 1, (thisV, args) => SetParts(realm, thisV, args, DatePart.Milliseconds));

        // Annex B §B.2.4.1 getYear() — getFullYear() - 1900 (NaN passthrough).
        IntrinsicHelpers.DefineMethod(realm, proto, "getYear", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            if (!d.IsValid)
            {
                return JsValue.NaN;
            }

            return JsValue.Number(GetField(realm, thisV, DateField.Year).AsNumber - 1900);
        });
        // Annex B §B.2.4.2 setYear(year) — 0 ≤ y ≤ 99 means 1900 + y; a NaN
        // year invalidates the date.
        IntrinsicHelpers.DefineMethod(realm, proto, "setYear", 1, (thisV, args) =>
        {
            var y = JsValue.ToNumber(args.Length > 0 ? args[0] : JsValue.Undefined);
            if (double.IsNaN(y))
            {
                return SetParts(realm, thisV, new[] { JsValue.NaN }, DatePart.Year);
            }

            var yi = Math.Truncate(y);
            var full = yi is >= 0 and <= 99 ? yi + 1900 : y;
            return SetParts(realm, thisV, new[] { JsValue.Number(full) }, DatePart.Year);
        });

        // String conversions.
        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            return JsValue.String(d.IsValid ? FormatToString(d.TimeValueMs) : "Invalid Date");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toDateString", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            return JsValue.String(d.IsValid ? FormatDateString(d.TimeValueMs) : "Invalid Date");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toTimeString", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            return JsValue.String(d.IsValid ? FormatTimeString(d.TimeValueMs) : "Invalid Date");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toISOString", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            if (!d.IsValid)
            {
                throw new JsThrow(realm.NewRangeError("Invalid time value"));
            }

            return JsValue.String(FormatIsoString(d.TimeValueMs));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toJSON", 1, (thisV, _) =>
        {
            // §21.4.4.37 — generic: ToObject(this), ToPrimitive(number) to
            // detect a non-finite time value, then Invoke(O, "toISOString").
            var vm = realm.ActiveVm;
            var obj = AbstractOperations.ToObject(realm, thisV);
            var tv = AbstractOperations.ToPrimitive(vm, JsValue.Object(obj), "number");
            if (tv.IsNumber && !double.IsFinite(tv.AsNumber))
            {
                return JsValue.Null;
            }

            var method = AbstractOperations.Get(vm, obj, "toISOString");
            if (!AbstractOperations.IsCallable(method))
            {
                throw new JsThrow(realm.NewTypeError("toISOString is not a function"));
            }

            return AbstractOperations.Call(vm, method, JsValue.Object(obj), Array.Empty<JsValue>());
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toUTCString", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            return JsValue.String(d.IsValid ? FormatUtcString(d.TimeValueMs) : "Invalid Date");
        });
        // Annex B §B.2.4.3 — toGMTString is the SAME function object as
        // toUTCString (not a wrapper), mirroring the trimLeft/trimStart alias.
        proto.DefineOwnProperty("toGMTString",
            PropertyDescriptor.BuiltinMethod(proto.Get("toUTCString")));
        // Locale variants — invariant, so delegate to the non-locale form.
        IntrinsicHelpers.DefineMethod(realm, proto, "toLocaleString", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            return JsValue.String(d.IsValid ? FormatToString(d.TimeValueMs) : "Invalid Date");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toLocaleDateString", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            return JsValue.String(d.IsValid ? FormatDateString(d.TimeValueMs) : "Invalid Date");
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "toLocaleTimeString", 0, (thisV, _) =>
        {
            var d = RequireDate(realm, thisV);
            return JsValue.String(d.IsValid ? FormatTimeString(d.TimeValueMs) : "Invalid Date");
        });

        // §21.4.4.45 Date.prototype[@@toPrimitive] — generic over any object
        // receiver: validates the hint, then runs OrdinaryToPrimitive.
        var toPrimitive = new JsNativeFunction(realm, "[Symbol.toPrimitive]", 1, (thisV, args) =>
        {
            if (!thisV.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("Date.prototype[Symbol.toPrimitive] called on non-object"));
            }

            var hintV = args.Length > 0 ? args[0] : JsValue.Undefined;
            var hint = hintV.IsString ? hintV.AsString : null;
            var stringFirst = hint is "string" or "default";
            if (!stringFirst && hint != "number")
            {
                throw new JsThrow(realm.NewTypeError("Invalid hint: " + (hint ?? JsValue.ToStringValue(hintV))));
            }

            return OrdinaryToPrimitive(realm, thisV.AsObject, stringFirst);
        }, isConstructor: false);
        proto.DefineOwnProperty(SymbolCtor.ToPrimitive,
            PropertyDescriptor.Data(JsValue.Object(toPrimitive), writable: false, enumerable: false, configurable: true));

        realm.DateConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Date",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    // ------------------------------------------------------------------
    //                          Coercion helpers
    // ------------------------------------------------------------------

    /// <summary>§7.1.4 ToNumber with observable ToPrimitive dispatch (user
    /// valueOf/toString/@@toPrimitive run through the active VM).</summary>
    private static double ToNumberArg(JsRealm realm, JsValue v)
    {
        if (v.IsObject)
        {
            v = AbstractOperations.ToPrimitive(realm.ActiveVm, v, "number");
        }

        return ToNumberPrim(realm, v);
    }

    private static double ToNumberPrim(JsRealm realm, JsValue prim)
    {
        if (prim.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a number"));
        }

        if (prim.IsBigInt)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a BigInt value to a number"));
        }

        return NumberCtor.ToNumber(prim);
    }

    /// <summary>§7.1.1.1 OrdinaryToPrimitive — the toString/valueOf probe
    /// without the @@toPrimitive lookup (this IS the @@toPrimitive body).</summary>
    private static JsValue OrdinaryToPrimitive(JsRealm realm, JsObject obj, bool stringFirst)
    {
        var vm = realm.ActiveVm;
        var first = stringFirst ? "toString" : "valueOf";
        var second = stringFirst ? "valueOf" : "toString";
        var m1 = AbstractOperations.Get(vm, obj, first);
        if (AbstractOperations.IsCallable(m1))
        {
            var r = AbstractOperations.Call(vm, m1, JsValue.Object(obj), Array.Empty<JsValue>());
            if (!r.IsObject)
            {
                return r;
            }
        }

        var m2 = AbstractOperations.Get(vm, obj, second);
        if (AbstractOperations.IsCallable(m2))
        {
            var r = AbstractOperations.Call(vm, m2, JsValue.Object(obj), Array.Empty<JsValue>());
            if (!r.IsObject)
            {
                return r;
            }
        }

        throw new JsThrow(realm.NewTypeError("Cannot convert object to primitive value"));
    }

    /// <summary>§10.1.13 GetPrototypeFromConstructor — new.target's "prototype"
    /// when it is an object, else the %Date.prototype% of new.target's own
    /// function realm (cross-realm Reflect.construct), else our default.</summary>
    private static JsObject PrototypeFromNewTarget(JsRealm realm, JsValue newTarget, JsObject defaultProto)
    {
        if (!newTarget.IsObject || !AbstractOperations.IsConstructor(newTarget))
        {
            return defaultProto;
        }

        var p = AbstractOperations.Get(realm.ActiveVm, newTarget.AsObject, "prototype");
        if (p.IsObject)
        {
            return p.AsObject;
        }

        var funcRealm = FunctionRealmOf(newTarget.AsObject);
        return funcRealm?.DatePrototype ?? defaultProto;
    }

    private static JsRealm? FunctionRealmOf(JsObject fn) => fn switch
    {
        JsFunction f => f.Realm,
        JsBoundFunction bf => FunctionRealmOf(bf.Target),
        _ => null,
    };

    // ------------------------------------------------------------------
    //                          Spec time arithmetic (§21.4.1)
    // ------------------------------------------------------------------

    private static double NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>§21.4.1.31 TimeClip — NaN outside ±8.64e15, integer ms inside,
    /// with −0 normalized to +0.</summary>
    internal static double TimeClip(double t)
    {
        if (!double.IsFinite(t) || Math.Abs(t) > MaxTimeValue)
        {
            return double.NaN;
        }

        var clipped = Math.Truncate(t);
        return clipped == 0 ? 0 : clipped;
    }

    /// <summary>§21.4.1.27 MakeTime, IEEE-754 double arithmetic in spec
    /// operation order.</summary>
    private static double MakeTime(double hour, double min, double sec, double ms)
    {
        if (!double.IsFinite(hour) || !double.IsFinite(min) || !double.IsFinite(sec) || !double.IsFinite(ms))
        {
            return double.NaN;
        }

        var h = Math.Truncate(hour);
        var m = Math.Truncate(min);
        var s = Math.Truncate(sec);
        var milli = Math.Truncate(ms);
        return ((h * MsPerHour + m * MsPerMinute) + s * MsPerSecond) + milli;
    }

    /// <summary>§21.4.1.28 MakeDay — day number for (year, month, 1) plus the
    /// date offset, all as doubles so huge inputs overflow to values TimeClip
    /// later rejects instead of wrapping.</summary>
    private static double MakeDay(double year, double month, double date)
    {
        if (!double.IsFinite(year) || !double.IsFinite(month) || !double.IsFinite(date))
        {
            return double.NaN;
        }

        var y = Math.Truncate(year);
        var m = Math.Truncate(month);
        var dt = Math.Truncate(date);
        var ym = y + Math.Floor(m / 12);
        if (!double.IsFinite(ym) || Math.Abs(ym) > 1_000_000d)
        {
            // No finite time value has a year this large (the valid range ends
            // at ±275760); spec step 8 returns NaN when t cannot be found.
            return double.NaN;
        }

        var mn = (int)(m - 12 * Math.Floor(m / 12));
        var day = DaysFromCivil((long)ym, mn + 1, 1);
        return (day + dt) - 1;
    }

    /// <summary>§21.4.1.29 MakeDate.</summary>
    private static double MakeDate(double day, double time)
    {
        if (!double.IsFinite(day) || !double.IsFinite(time))
        {
            return double.NaN;
        }

        return day * MsPerDay + time;
    }

    /// <summary>The shared §21.4.2.1 / §21.4.3.4 field path: ToNumber every
    /// supplied argument left-to-right (all coercions are observable and happen
    /// before any NaN check), apply the 0–99 → 1900-relative year rule, then
    /// combine. Caller wraps in TimeClip.</summary>
    private static double MakeFromFields(JsRealm realm, JsValue[] args)
    {
        var y = args.Length > 0 ? ToNumberArg(realm, args[0]) : double.NaN;
        var m = args.Length > 1 ? ToNumberArg(realm, args[1]) : 0;
        var dt = args.Length > 2 ? ToNumberArg(realm, args[2]) : 1;
        var h = args.Length > 3 ? ToNumberArg(realm, args[3]) : 0;
        var min = args.Length > 4 ? ToNumberArg(realm, args[4]) : 0;
        var s = args.Length > 5 ? ToNumberArg(realm, args[5]) : 0;
        var milli = args.Length > 6 ? ToNumberArg(realm, args[6]) : 0;

        var yr = y;
        if (!double.IsNaN(y))
        {
            var yi = Math.Truncate(y);
            if (yi >= 0 && yi <= 99)
            {
                yr = 1900 + yi;
            }
        }

        return MakeDate(MakeDay(yr, m, dt), MakeTime(h, min, s, milli));
    }

    /// <summary>Days since the epoch for a proleptic Gregorian civil date
    /// (month 1-12). Exact for |year| ≤ 1e6.</summary>
    private static long DaysFromCivil(long y, int m, int d)
    {
        y -= m <= 2 ? 1 : 0;
        var era = (y >= 0 ? y : y - 399) / 400;
        var yoe = y - era * 400;
        var doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
        var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    /// <summary>Inverse of <see cref="DaysFromCivil"/>.</summary>
    private static (long Year, int Month, int Day) CivilFromDays(long z)
    {
        z += 719468;
        var era = (z >= 0 ? z : z - 146096) / 146097;
        var doe = z - era * 146097;
        var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        var y = yoe + era * 400;
        var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        var mp = (5 * doy + 2) / 153;
        var d = (int)(doy - (153 * mp + 2) / 5 + 1);
        var m = (int)(mp < 10 ? mp + 3 : mp - 9);
        return (y + (m <= 2 ? 1 : 0), m, d);
    }

    private static long FloorDiv(long a, long b) => a >= 0 ? a / b : ~(~a / b);

    private static long FloorMod(long a, long b)
    {
        var r = a % b;
        return r < 0 ? r + b : r;
    }

    /// <summary>Split a valid (TimeClip'd) time value into calendar fields.
    /// The whole range fits a long exactly, so this is pure integer math.</summary>
    private readonly record struct DateFields(long Year, int Month, int Day, int Weekday, int Hours, int Minutes, int Seconds, int Milliseconds);

    private static DateFields FieldsFromTime(double t)
    {
        var tl = (long)t;
        var day = FloorDiv(tl, 86_400_000L);
        var msInDay = FloorMod(tl, 86_400_000L);
        var (year, month, dayOfMonth) = CivilFromDays(day);
        return new DateFields(
            year,
            month - 1,
            dayOfMonth,
            (int)FloorMod(day + 4, 7),
            (int)(msInDay / 3_600_000L),
            (int)(msInDay / 60_000L % 60),
            (int)(msInDay / 1_000L % 60),
            (int)(msInDay % 1_000L));
    }

    // ------------------------------------------------------------------
    //                          Getters
    // ------------------------------------------------------------------

    private static JsDate RequireDate(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsObject && thisV.AsObject is JsDate d)
        {
            return d;
        }

        throw new JsThrow(realm.NewTypeError("Date.prototype method called on non-Date receiver"));
    }

    private static JsValue Time(JsRealm realm, JsValue thisV)
    {
        var d = RequireDate(realm, thisV);
        return d.IsValid ? JsValue.Number(d.TimeValueMs) : JsValue.NaN;
    }

    private enum DateField { Year, Month, Day, Weekday, Hours, Minutes, Seconds, Milliseconds }

    private static JsValue GetField(JsRealm realm, JsValue thisV, DateField field)
    {
        var d = RequireDate(realm, thisV);
        if (!d.IsValid)
        {
            return JsValue.NaN;
        }

        var f = FieldsFromTime(d.TimeValueMs);
        return JsValue.Number(field switch
        {
            DateField.Year => f.Year,
            DateField.Month => f.Month,
            DateField.Day => f.Day,
            DateField.Weekday => f.Weekday,
            DateField.Hours => f.Hours,
            DateField.Minutes => f.Minutes,
            DateField.Seconds => f.Seconds,
            _ => f.Milliseconds,
        });
    }

    // ------------------------------------------------------------------
    //                          Setters
    // ------------------------------------------------------------------

    private enum DatePart { Year, Month, Day, Hours, Minutes, Seconds, Milliseconds }

    private static JsValue SetParts(JsRealm realm, JsValue thisV, JsValue[] args, DatePart kind)
    {
        var d = RequireDate(realm, thisV);
        // [[DateValue]] is read before any argument coercion (observable when a
        // poisoned valueOf mutates the receiver).
        var t = d.TimeValueMs;
        // setFullYear treats an invalid date as time +0 (§21.4.4.21); every
        // other setter leaves it invalid — but ALL argument coercions still run.
        var invalid = double.IsNaN(t);
        var f = FieldsFromTime(invalid ? 0 : t);
        double year = f.Year, month = f.Month, day = f.Day;
        double hours = f.Hours, minutes = f.Minutes, seconds = f.Seconds, ms = f.Milliseconds;

        double Take(int i) => i < args.Length ? ToNumberArg(realm, args[i]) : double.NaN;
        double TakeOr(int i, double fallback) => i < args.Length ? ToNumberArg(realm, args[i]) : fallback;

        switch (kind)
        {
            case DatePart.Year:
                year = Take(0);
                month = TakeOr(1, month);
                day = TakeOr(2, day);
                break;
            case DatePart.Month:
                month = Take(0);
                day = TakeOr(1, day);
                break;
            case DatePart.Day:
                day = Take(0);
                break;
            case DatePart.Hours:
                hours = Take(0);
                minutes = TakeOr(1, minutes);
                seconds = TakeOr(2, seconds);
                ms = TakeOr(3, ms);
                break;
            case DatePart.Minutes:
                minutes = Take(0);
                seconds = TakeOr(1, seconds);
                ms = TakeOr(2, ms);
                break;
            case DatePart.Seconds:
                seconds = Take(0);
                ms = TakeOr(1, ms);
                break;
            case DatePart.Milliseconds:
                ms = Take(0);
                break;
        }

        if (invalid && kind != DatePart.Year)
        {
            return JsValue.NaN;
        }

        var u = TimeClip(MakeDate(MakeDay(year, month, day), MakeTime(hours, minutes, seconds, ms)));
        d.SetTimeMs(u);
        return JsValue.Number(u);
    }

    // ------------------------------------------------------------------
    //                       Parsing (§21.4.1.15 + lenient legacy)
    // ------------------------------------------------------------------

    internal static double ParseDate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return double.NaN;
        }

        var s = input.Trim();
        if (TryParseIso(s, out var iso))
        {
            return iso;
        }

        // Non-ISO strings are implementation-defined (§21.4.3.2); accept the
        // formats the web depends on. First the host's lenient parser for
        // human-readable forms ("December 17, 1995 03:24:00"), then a token
        // scanner for the toString round-trip family.
        if (System.DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var any))
        {
            return any.ToUnixTimeMilliseconds();
        }

        if (TryParseLegacy(s, out var legacy))
        {
            return legacy;
        }

        return double.NaN;
    }

    /// <summary>The §21.4.1.15 Date Time String Format: YYYY or ±YYYYYY, then
    /// optional -MM[-DD], then optional THH:mm[:ss[.fff]] with optional Z or
    /// ±HH:mm offset. Returns false when the string is not an instance of the
    /// grammar at all (caller may try legacy forms); returns true with NaN when
    /// it matches the shape but has out-of-range fields.</summary>
    private static bool TryParseIso(string s, out double ms)
    {
        ms = double.NaN;
        var i = 0;
        var n = s.Length;

        static bool Digits(string str, ref int pos, int count, out int value)
        {
            value = 0;
            if (pos + count > str.Length)
            {
                return false;
            }

            for (var k = 0; k < count; k++)
            {
                var c = str[pos + k];
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }

                value = value * 10 + (c - '0');
            }

            pos += count;
            return true;
        }

        long year;
        if (i < n && (s[i] == '+' || s[i] == '-'))
        {
            var sign = s[i] == '-' ? -1 : 1;
            i++;
            if (!Digits(s, ref i, 6, out var y6))
            {
                return false;
            }

            if (sign == -1 && y6 == 0)
            {
                // "-000000" is explicitly invalid.
                return true;
            }

            year = sign * (long)y6;
        }
        else
        {
            if (!Digits(s, ref i, 4, out var y4))
            {
                return false;
            }

            year = y4;
        }

        var month = 1;
        var day = 1;
        var dateOnly = true;
        if (i < n && s[i] == '-')
        {
            i++;
            if (!Digits(s, ref i, 2, out month))
            {
                return false;
            }

            if (i < n && s[i] == '-')
            {
                i++;
                if (!Digits(s, ref i, 2, out day))
                {
                    return false;
                }
            }
        }

        int hour = 0, minute = 0, second = 0, milli = 0;
        var haveOffset = false;
        var offsetMinutes = 0L;
        if (i < n && (s[i] == 'T' || s[i] == 't'))
        {
            dateOnly = false;
            i++;
            if (!Digits(s, ref i, 2, out hour) || i >= n || s[i] != ':')
            {
                return false;
            }

            i++;
            if (!Digits(s, ref i, 2, out minute))
            {
                return false;
            }

            if (i < n && s[i] == ':')
            {
                i++;
                if (!Digits(s, ref i, 2, out second))
                {
                    return false;
                }

                if (i < n && s[i] == '.')
                {
                    i++;
                    var start = i;
                    while (i < n && char.IsAsciiDigit(s[i]))
                    {
                        i++;
                    }

                    if (i == start)
                    {
                        return false;
                    }

                    // Use millisecond precision; extra digits are ignored.
                    var fracLen = Math.Min(3, i - start);
                    milli = int.Parse(s.AsSpan(start, fracLen), CultureInfo.InvariantCulture);
                    for (var k = fracLen; k < 3; k++)
                    {
                        milli *= 10;
                    }
                }
            }

            if (i < n)
            {
                if (s[i] == 'Z' || s[i] == 'z')
                {
                    i++;
                    haveOffset = true;
                }
                else if (s[i] == '+' || s[i] == '-')
                {
                    var osign = s[i] == '-' ? -1 : 1;
                    i++;
                    if (!Digits(s, ref i, 2, out var oh))
                    {
                        return false;
                    }

                    if (i < n && s[i] == ':')
                    {
                        i++;
                    }

                    if (!Digits(s, ref i, 2, out var om))
                    {
                        return false;
                    }

                    if (oh > 23 || om > 59)
                    {
                        return true;
                    }

                    offsetMinutes = osign * (oh * 60L + om);
                    haveOffset = true;
                }
            }
        }

        if (i != n)
        {
            return false;
        }

        // Out-of-range fields: the shape matched, so this is a recognized but
        // invalid date → NaN (already set). A date-only or offset-less string
        // is interpreted as UTC (this engine's local time IS UTC).
        _ = dateOnly;
        _ = haveOffset;
        if (month < 1 || month > 12 || day < 1 || day > DaysInMonth(year, month))
        {
            return true;
        }

        if (hour > 24 || minute > 59 || second > 59 || (hour == 24 && (minute != 0 || second != 0 || milli != 0)))
        {
            return true;
        }

        var t = MakeDate(MakeDay(year, month - 1, day), MakeTime(hour, minute, second, milli))
            - offsetMinutes * MsPerMinute;
        ms = TimeClip(t);
        return true;
    }

    private static int DaysInMonth(long year, int month)
    {
        if (month == 2)
        {
            var leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
            return leap ? 29 : 28;
        }

        return month is 4 or 6 or 9 or 11 ? 30 : 31;
    }

    /// <summary>Token-based parser for the loose date forms: any order of
    /// weekday (ignored), month name, day, 4-digit year, hh:mm[:ss[.fff]],
    /// and a UTC offset ("+0000", "GMT+01:00", "Z", bare "GMT"/"UTC").
    /// Parenthesized zone names are stripped. Without an offset the time is
    /// taken as UTC, matching this engine's ISO fallback behaviour.</summary>
    private static bool TryParseLegacy(string input, out double ms)
    {
        ms = double.NaN;
        var noParen = System.Text.RegularExpressions.Regex.Replace(input, @"\([^)]*\)", " ");
        var tokens = noParen.Split(new[] { ' ', ',', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        int month = -1, day = -1, year = int.MinValue;
        int hour = 0, minute = 0, second = 0, milli = 0;
        var offsetMinutes = 0;
        var sawTime = false;

        foreach (var raw in tokens)
        {
            var tok = raw;
            if (tok.StartsWith("GMT", System.StringComparison.OrdinalIgnoreCase)
                || tok.StartsWith("UTC", System.StringComparison.OrdinalIgnoreCase))
            {
                tok = tok[3..];
                if (tok.Length == 0)
                {
                    continue; // bare GMT/UTC → offset 0
                }
            }
            if (tok is "Z" or "z")
            {
                continue;
            }

            if (tok[0] is '+' or '-')
            {
                var sign = tok[0] == '-' ? -1 : 1;
                var digits = tok[1..].Replace(":", "");
                if (digits.Length != 4 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var hhmm))
                {
                    return false;
                }

                offsetMinutes = sign * (hhmm / 100 * 60 + hhmm % 100);
                continue;
            }

            if (tok.Contains(':'))
            {
                var fracSplit = tok.Split('.');
                var parts = fracSplit[0].Split(':');
                if (parts.Length is < 2 or > 3)
                {
                    return false;
                }

                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour))
                {
                    return false;
                }

                if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute))
                {
                    return false;
                }

                if (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out second))
                {
                    return false;
                }

                if (fracSplit.Length == 2)
                {
                    var frac = fracSplit[1].PadRight(3, '0')[..3];
                    if (!int.TryParse(frac, NumberStyles.None, CultureInfo.InvariantCulture, out milli))
                    {
                        return false;
                    }
                }
                sawTime = true;
                continue;
            }

            if (char.IsAsciiDigit(tok[0]))
            {
                if (!int.TryParse(tok, NumberStyles.None, CultureInfo.InvariantCulture, out var num))
                {
                    return false;
                }

                if (tok.Length >= 3 || num > 31)
                {
                    if (year != int.MinValue)
                    {
                        return false;
                    }

                    year = num;
                }
                else if (day < 0)
                {
                    day = num;
                }
                else if (year == int.MinValue)
                {
                    // "MMM d yy"-style trailing 2-digit year (1900-window not
                    // applied — browsers map 0-99 via the full-year rule only
                    // for Date(y, m) construction, strings keep the literal).
                    year = num;
                }
                else
                {
                    return false;
                }

                continue;
            }

            // Month or weekday name (weekday ignored). Anything else → fail.
            var name = tok.Length > 3 ? tok[..3] : tok;
            var monthIdx = System.Array.FindIndex(MonthShort,
                m => string.Equals(m, name, System.StringComparison.OrdinalIgnoreCase));
            if (monthIdx >= 0)
            {
                if (month >= 0)
                {
                    return false;
                }

                month = monthIdx + 1;
                continue;
            }
            var isWeekday = System.Array.Exists(WeekdayShort,
                w => string.Equals(w, name, System.StringComparison.OrdinalIgnoreCase));
            if (!isWeekday)
            {
                return false;
            }
        }

        if (month < 0 || day < 1 || day > 31 || year == int.MinValue)
        {
            return false;
        }

        if (hour > 24 || minute > 59 || second > 59)
        {
            return false;
        }

        if (!sawTime && (hour != 0 || minute != 0))
        {
            return false;
        }

        var dayNum = DaysFromCivil(year, month, day);
        ms = dayNum * MsPerDay
            + hour * 3_600_000L + minute * 60_000L + second * 1_000L + milli
            - offsetMinutes * 60_000L;
        ms = TimeClip(ms);
        return !double.IsNaN(ms);
    }

    // ------------------------------------------------------------------
    //                       Formatting (invariant)
    // ------------------------------------------------------------------

    /// <summary>§21.4.4.41.2 year text: at least four digits, "-" prefix for
    /// negative years.</summary>
    private static string YearString(long year) =>
        year < 0
            ? "-" + (-year).ToString("D4", CultureInfo.InvariantCulture)
            : year.ToString("D4", CultureInfo.InvariantCulture);

    private static string FormatToString(double ms)
    {
        var f = FieldsFromTime(ms);
        return string.Format(CultureInfo.InvariantCulture,
            "{0} {1} {2:D2} {3} {4:D2}:{5:D2}:{6:D2} GMT+0000 (Coordinated Universal Time)",
            WeekdayShort[f.Weekday], MonthShort[f.Month], f.Day, YearString(f.Year), f.Hours, f.Minutes, f.Seconds);
    }

    private static string FormatDateString(double ms)
    {
        var f = FieldsFromTime(ms);
        return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2:D2} {3}",
            WeekdayShort[f.Weekday], MonthShort[f.Month], f.Day, YearString(f.Year));
    }

    private static string FormatTimeString(double ms)
    {
        var f = FieldsFromTime(ms);
        return string.Format(CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2} GMT+0000 (Coordinated Universal Time)", f.Hours, f.Minutes, f.Seconds);
    }

    private static string FormatUtcString(double ms)
    {
        var f = FieldsFromTime(ms);
        return string.Format(CultureInfo.InvariantCulture,
            "{0}, {1:D2} {2} {3} {4:D2}:{5:D2}:{6:D2} GMT",
            WeekdayShort[f.Weekday], f.Day, MonthShort[f.Month], YearString(f.Year), f.Hours, f.Minutes, f.Seconds);
    }

    /// <summary>§21.4.4.36 — expanded ±YYYYYY year outside 0000-9999.</summary>
    private static string FormatIsoString(double ms)
    {
        var f = FieldsFromTime(ms);
        string year;
        if (f.Year is >= 0 and <= 9999)
        {
            year = f.Year.ToString("D4", CultureInfo.InvariantCulture);
        }
        else if (f.Year < 0)
        {
            year = "-" + (-f.Year).ToString("D6", CultureInfo.InvariantCulture);
        }
        else
        {
            year = "+" + f.Year.ToString("D6", CultureInfo.InvariantCulture);
        }

        return string.Format(CultureInfo.InvariantCulture,
            "{0}-{1:D2}-{2:D2}T{3:D2}:{4:D2}:{5:D2}.{6:D3}Z",
            year, f.Month + 1, f.Day, f.Hours, f.Minutes, f.Seconds, f.Milliseconds);
    }
}
