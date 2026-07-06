using System.Globalization;
using System.Text;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §22.1 The String constructor and §22.1.3 String.prototype. Strings are
/// represented as .NET UTF-16 strings, matching ECMAScript's code-unit model.
/// Methods that accept regular expressions delegate through the standard
/// <c>Symbol.match</c>, <c>Symbol.replace</c>, <c>Symbol.search</c>, and
/// <c>Symbol.split</c> hooks when present.
/// </summary>
public static class StringCtor
{
    private const int MaxRepeatLength = 1_000_000;

    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var stringProto = realm.StringPrototype;

        var ctor = new JsNativeFunction(realm, "String", length: 1, (newTarget, args) =>
        {
            // §22.1.1.1 String(value): no argument returns the empty string;
            // otherwise route through §7.1.17 ToString. Use the AO variant so
            // an object's toString/valueOf (e.g. Error.prototype.toString) is
            // invoked instead of the flat "[object Object]" fallback. Note:
            // String(symbol) is the one allowed Symbol→string path, so handle
            // it before ToString (which rejects Symbols per step 2).
            // §22.1.1.1 step 4: when called as a function, return the primitive.
            var constructed = IntrinsicHelpers.IsConstructInvocation(newTarget);
            string text;
            if (args.Length == 0)
            {
                text = string.Empty;
            }
            else if (!constructed && args[0].IsSymbol)
            {
                text = args[0].AsSymbol.DescriptiveString;
            }
            else
            {
                text = AbstractOperations.ToStringJs(realm.ActiveVm, args[0]);
            }

            if (constructed)
            {
                // §22.1.1.1 step 5: StringCreate with OrdinaryCreateFromConstructor —
                // prototype comes from new.target so `class S extends String {}` works.
                var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, stringProto, static r => r.StringPrototype);
                return JsValue.Object(CreateStringObject(realm, text, instProto));
            }
            return JsValue.String(text);
        }, isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(stringProto), writable: false, enumerable: false, configurable: false));

        IntrinsicHelpers.DefineMethod(realm, ctor, "fromCharCode", 1, (_, args) => FromCharCode(realm, args));
        IntrinsicHelpers.DefineMethod(realm, ctor, "fromCodePoint", 1, (_, args) => FromCodePoint(realm, args));
        IntrinsicHelpers.DefineMethod(realm, ctor, "raw", 1, (_, args) => Raw(realm, args));

        // Bulk-install constructor + every string-keyed prototype method by
        // adopting one precomputed shape. The symbol-keyed @@iterator is
        // installed separately below (symbols can never enter a shape).
        IntrinsicHelpers.BulkInstallBuiltins(realm, stringProto, new[]
        {
            new IntrinsicHelpers.BulkMember("constructor", 0, null, JsValue.Object(ctor)),
            new IntrinsicHelpers.BulkMember("at", 1, (thisV, args) => At(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("charAt", 1, (thisV, args) => CharAt(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("charCodeAt", 1, (thisV, args) => CharCodeAt(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("codePointAt", 1, (thisV, args) => CodePointAt(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("concat", 1, (thisV, args) => Concat(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("endsWith", 1, (thisV, args) => EndsWith(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("includes", 1, (thisV, args) => Includes(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("indexOf", 1, (thisV, args) => IndexOf(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("lastIndexOf", 1, (thisV, args) => LastIndexOf(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("localeCompare", 1, (thisV, args) => LocaleCompare(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("normalize", 0, (thisV, args) => Normalize(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("padEnd", 1, (thisV, args) => Pad(realm, thisV, args, atStart: false)),
            new IntrinsicHelpers.BulkMember("padStart", 1, (thisV, args) => Pad(realm, thisV, args, atStart: true)),
            new IntrinsicHelpers.BulkMember("repeat", 1, (thisV, args) => Repeat(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("match", 1, (thisV, args) => Match(realm, thisV, args, all: false)),
            new IntrinsicHelpers.BulkMember("matchAll", 1, (thisV, args) => Match(realm, thisV, args, all: true)),
            new IntrinsicHelpers.BulkMember("search", 1, (thisV, args) => Search(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("replace", 2, (thisV, args) => Replace(realm, thisV, args, replaceAll: false)),
            new IntrinsicHelpers.BulkMember("replaceAll", 2, (thisV, args) => Replace(realm, thisV, args, replaceAll: true)),
            new IntrinsicHelpers.BulkMember("slice", 2, (thisV, args) => Slice(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("split", 2, (thisV, args) => Split(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("startsWith", 1, (thisV, args) => StartsWith(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("substring", 2, (thisV, args) => Substring(realm, thisV, args)),
            // Annex B §B.2.2.1 String.prototype.substr(start[, length]) — legacy but widely used.
            new IntrinsicHelpers.BulkMember("substr", 2, (thisV, args) => Substr(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("toLowerCase", 0, (thisV, args) => JsValue.String(StringCasing.ToLowerJs(CoerceThisString(realm, thisV)))),
            new IntrinsicHelpers.BulkMember("toUpperCase", 0, (thisV, args) => JsValue.String(StringCasing.ToUpperJs(CoerceThisString(realm, thisV)))),
            new IntrinsicHelpers.BulkMember("toLocaleLowerCase", 0, (thisV, args) => JsValue.String(StringCasing.ToLowerJs(CoerceThisString(realm, thisV)))),
            new IntrinsicHelpers.BulkMember("toLocaleUpperCase", 0, (thisV, args) => JsValue.String(StringCasing.ToUpperJs(CoerceThisString(realm, thisV)))),
            new IntrinsicHelpers.BulkMember("trim", 0, (thisV, args) => JsValue.String(TrimJs(CoerceThisString(realm, thisV), trimStart: true, trimEnd: true))),
            new IntrinsicHelpers.BulkMember("trimStart", 0, (thisV, args) => JsValue.String(TrimJs(CoerceThisString(realm, thisV), trimStart: true, trimEnd: false))),
            new IntrinsicHelpers.BulkMember("trimEnd", 0, (thisV, args) => JsValue.String(TrimJs(CoerceThisString(realm, thisV), trimStart: false, trimEnd: true))),
            new IntrinsicHelpers.BulkMember("toString", 0, (thisV, args) => JsValue.String(ThisStringValue(realm, thisV))),
            new IntrinsicHelpers.BulkMember("valueOf", 0, (thisV, args) => JsValue.String(ThisStringValue(realm, thisV))),
            new IntrinsicHelpers.BulkMember("isWellFormed", 0, (thisV, args) => JsValue.Boolean(IsWellFormedString(CoerceThisString(realm, thisV)))),
            new IntrinsicHelpers.BulkMember("toWellFormed", 0, (thisV, args) => JsValue.String(ToWellFormedString(CoerceThisString(realm, thisV)))),
            // Annex B §B.2.2.2–.14 HTML wrapper methods.
            new IntrinsicHelpers.BulkMember("anchor", 1, (thisV, args) => CreateHtml(realm, thisV, "a", "name", args)),
            new IntrinsicHelpers.BulkMember("big", 0, (thisV, args) => CreateHtml(realm, thisV, "big", "", args)),
            new IntrinsicHelpers.BulkMember("blink", 0, (thisV, args) => CreateHtml(realm, thisV, "blink", "", args)),
            new IntrinsicHelpers.BulkMember("bold", 0, (thisV, args) => CreateHtml(realm, thisV, "b", "", args)),
            new IntrinsicHelpers.BulkMember("fixed", 0, (thisV, args) => CreateHtml(realm, thisV, "tt", "", args)),
            new IntrinsicHelpers.BulkMember("fontcolor", 1, (thisV, args) => CreateHtml(realm, thisV, "font", "color", args)),
            new IntrinsicHelpers.BulkMember("fontsize", 1, (thisV, args) => CreateHtml(realm, thisV, "font", "size", args)),
            new IntrinsicHelpers.BulkMember("italics", 0, (thisV, args) => CreateHtml(realm, thisV, "i", "", args)),
            new IntrinsicHelpers.BulkMember("link", 1, (thisV, args) => CreateHtml(realm, thisV, "a", "href", args)),
            new IntrinsicHelpers.BulkMember("small", 0, (thisV, args) => CreateHtml(realm, thisV, "small", "", args)),
            new IntrinsicHelpers.BulkMember("strike", 0, (thisV, args) => CreateHtml(realm, thisV, "strike", "", args)),
            new IntrinsicHelpers.BulkMember("sub", 0, (thisV, args) => CreateHtml(realm, thisV, "sub", "", args)),
            new IntrinsicHelpers.BulkMember("sup", 0, (thisV, args) => CreateHtml(realm, thisV, "sup", "", args)),
        });
        stringProto.DefineOwnProperty("trimLeft",
            PropertyDescriptor.BuiltinMethod(stringProto.Get("trimStart")));
        stringProto.DefineOwnProperty("trimRight",
            PropertyDescriptor.BuiltinMethod(stringProto.Get("trimEnd")));

        // §22.1.3.34 String.prototype[@@iterator] — walks the string by
        // Unicode code points (not UTF-16 code units), e.g.
        // [..."😀ab"].length === 3.
        var stringIterator = new JsNativeFunction(realm, "[Symbol.iterator]", 0, (thisV, _) =>
        {
            var s = CoerceThisString(realm, thisV);
            return IteratorIntrinsics.CreateStringIterator(realm, s);
        }, isConstructor: false);
        stringProto.DefineOwnProperty(SymbolCtor.Iterator,
            PropertyDescriptor.BuiltinMethod(JsValue.Object(stringIterator)));

        realm.StringConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("String",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    private static JsStringObject CreateStringObject(JsRealm realm, string text, JsObject? proto = null)
        => new(proto ?? realm.StringPrototype, text);

    /// <summary>§22.1.3.29/§22.1.3.35 thisStringValue: only primitive strings
    /// and String exotic wrappers qualify; anything else is a TypeError (never
    /// ToPrimitive — that would re-enter toString and recurse).</summary>
    private static string ThisStringValue(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsString)
        {
            return thisV.AsString;
        }

        if (thisV.IsObject && thisV.AsObject is JsStringObject jso)
        {
            return jso.Text;
        }

        throw new JsThrow(realm.NewTypeError("String.prototype method requires a String receiver"));
    }

    /// <summary>Generic-method receiver coercion: RequireObjectCoercible then
    /// §7.1.17 ToString, so object receivers dispatch their own toString.</summary>
    private static string CoerceThisString(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsString)
        {
            return thisV.AsString;
        }

        if (thisV.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("String.prototype method called on null or undefined"));
        }

        return AbstractOperations.ToStringJs(realm.ActiveVm, thisV);
    }

    /// <summary>Receiver to hand a matched @@match/@@search/@@split hook: the
    /// builtin native hooks stringify with the non-observable fast ToString, so
    /// they get the coerced primitive; a user-defined hook gets the raw
    /// receiver per spec.</summary>
    private static JsValue DelegateReceiver(JsRealm realm, JsValue thisV, JsValue method)
    {
        if (thisV.IsObject && method.IsObject && method.AsObject is JsNativeFunction)
        {
            return JsValue.String(CoerceThisString(realm, thisV));
        }

        return thisV;
    }

    private static void RequireCoercibleThis(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("String.prototype method called on null or undefined"));
        }
    }

    private static string ToStringArg(JsRealm realm, JsValue v)
        => AbstractOperations.ToStringJs(realm.ActiveVm, v);

    /// <summary>§7.1.4 ToNumber with observable ToPrimitive for objects and JS
    /// TypeErrors for Symbol/BigInt inputs.</summary>
    private static double ToNumberJs(JsRealm realm, JsValue v)
    {
        var prim = v.Kind == JsValueKind.Object ? AbstractOperations.ToPrimitive(realm.ActiveVm, v, "number") : v;
        if (prim.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a number"));
        }

        if (prim.IsBigInt)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a BigInt value to a number"));
        }

        return JsValue.ToNumber(prim);
    }

    /// <summary>§7.2.6 IsRegExp — the observable check: Get(@@match) first,
    /// falling back to the [[RegExpMatcher]] slot.</summary>
    private static bool IsRegExpSpec(JsRealm realm, JsValue v)
    {
        if (!v.IsObject)
        {
            return false;
        }

        var matcher = AbstractOperations.Get(realm.ActiveVm, v.AsObject, JsPropertyKey.Symbol(SymbolCtor.Match));
        if (!matcher.IsUndefined)
        {
            return JsValue.ToBoolean(matcher);
        }

        return v.AsObject is JsRegExp;
    }

    private static JsValue FromCharCode(JsRealm realm, JsValue[] args)
    {
        var chars = new char[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            chars[i] = (char)ToUint16(realm, args[i]);
        }

        return JsValue.String(new string(chars));
    }

    private static JsValue FromCodePoint(JsRealm realm, JsValue[] args)
    {
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            var n = ToNumberJs(realm, arg);
            if (!IsIntegral(n) || n < 0 || n > 0x10FFFF)
            {
                throw new JsThrow(realm.NewRangeError("Invalid code point"));
            }

            // UTF16EncodeCodePoint: lone surrogates are legal here and encode
            // as their own code unit, so avoid ConvertFromUtf32 (it rejects them).
            var cp = (int)n;
            if (cp <= 0xFFFF)
            {
                sb.Append((char)cp);
            }
            else
            {
                cp -= 0x10000;
                sb.Append((char)(0xD800 + (cp >> 10)));
                sb.Append((char)(0xDC00 + (cp & 0x3FF)));
            }
        }
        return JsValue.String(sb.ToString());
    }

    private static JsValue Raw(JsRealm realm, JsValue[] args)
    {
        var cooked = AbstractOperations.ToObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var raw = AbstractOperations.ToObject(realm, AbstractOperations.Get(realm.ActiveVm, cooked, "raw"));
        var literalSegments = ToLengthJs(realm, AbstractOperations.Get(realm.ActiveVm, raw, "length"));
        if (literalSegments == 0)
        {
            return JsValue.String(string.Empty);
        }

        var sb = new StringBuilder();
        for (var i = 0L; i < literalSegments; i++)
        {
            sb.Append(ToStringArg(realm, AbstractOperations.Get(realm.ActiveVm, raw, i.ToString(CultureInfo.InvariantCulture))));
            if (i + 1 < literalSegments && i + 1 < args.Length)
            {
                sb.Append(ToStringArg(realm, args[i + 1]));
            }
        }
        return JsValue.String(sb.ToString());
    }

    private static JsValue At(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var i = ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var k = i >= 0 ? i : s.Length + i;
        return k < 0 || k >= s.Length ? JsValue.Undefined : JsValue.String(s[(int)k].ToString());
    }

    private static JsValue CharAt(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var pos = ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        return pos < 0 || pos >= s.Length ? JsValue.String(string.Empty) : JsValue.String(s[(int)pos].ToString());
    }

    private static JsValue CharCodeAt(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var pos = ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        return pos < 0 || pos >= s.Length ? JsValue.NaN : JsValue.Number(s[(int)pos]);
    }

    private static JsValue CodePointAt(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var pos = ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        if (pos < 0 || pos >= s.Length)
        {
            return JsValue.Undefined;
        }

        var first = s[(int)pos];
        if (char.IsHighSurrogate(first) && pos + 1 < s.Length && char.IsLowSurrogate(s[(int)pos + 1]))
        {
            return JsValue.Number(char.ConvertToUtf32(first, s[(int)pos + 1]));
        }

        return JsValue.Number(first);
    }

    private static JsValue Concat(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var sb = new StringBuilder(CoerceThisString(realm, thisV));
        foreach (var arg in args)
        {
            sb.Append(ToStringArg(realm, arg));
        }

        return JsValue.String(sb.ToString());
    }

    private static JsValue EndsWith(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var searchArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (IsRegExpSpec(realm, searchArg))
        {
            throw new JsThrow(realm.NewTypeError("First argument to String.prototype.endsWith must not be a regular expression"));
        }

        var search = ToStringArg(realm, searchArg);
        var end = args.Length > 1 && !args[1].IsUndefined ? Clamp(ToIntegerOrInfinity(realm, args[1]), 0, s.Length) : s.Length;
        var start = end - search.Length;
        return JsValue.Boolean(start >= 0 && s.AsSpan((int)start, search.Length).SequenceEqual(search));
    }

    private static JsValue Includes(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var searchArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (IsRegExpSpec(realm, searchArg))
        {
            throw new JsThrow(realm.NewTypeError("First argument to String.prototype.includes must not be a regular expression"));
        }

        var search = ToStringArg(realm, searchArg);
        var pos = Clamp(ToIntegerOrInfinity(realm, args.Length > 1 ? args[1] : JsValue.Undefined), 0, s.Length);
        return JsValue.Boolean(s.IndexOf(search, (int)pos, StringComparison.Ordinal) >= 0);
    }

    private static JsValue IndexOf(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var search = ToStringArg(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var pos = Clamp(ToIntegerOrInfinity(realm, args.Length > 1 ? args[1] : JsValue.Undefined), 0, s.Length);
        return JsValue.Number(s.IndexOf(search, (int)pos, StringComparison.Ordinal));
    }

    private static JsValue LastIndexOf(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var search = ToStringArg(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var raw = args.Length > 1 ? ToNumberJs(realm, args[1]) : double.NaN;
        var pos = double.IsNaN(raw) ? s.Length : Clamp(IntegerOrInfinityOf(raw), 0, s.Length);
        if (search.Length == 0)
        {
            return JsValue.Number(pos);
        }

        var start = Math.Min((int)pos + search.Length - 1, s.Length - 1);
        return JsValue.Number(start < 0 ? -1 : s.LastIndexOf(search, start, StringComparison.Ordinal));
    }

    private static JsValue LocaleCompare(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var that = ToStringArg(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        // Canonically equivalent strings must compare equal (§22.1.3.12 note 2);
        // under invariant globalization the comparison itself is ordinal, so
        // compare canonical decompositions.
        var result = string.CompareOrdinal(
            StringNormalization.Normalize(s, compatibility: false, compose: false),
            StringNormalization.Normalize(that, compatibility: false, compose: false));
        return JsValue.Number(Math.Sign(result));
    }

    private static JsValue Normalize(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var formName = args.Length == 0 || args[0].IsUndefined ? "NFC" : ToStringArg(realm, args[0]);
        var (compatibility, compose) = formName switch
        {
            "NFC" => (false, true),
            "NFD" => (false, false),
            "NFKC" => (true, true),
            "NFKD" => (true, false),
            _ => throw new JsThrow(realm.NewRangeError("Invalid normalization form")),
        };
        return JsValue.String(StringNormalization.Normalize(s, compatibility, compose));
    }

    private static JsValue Pad(JsRealm realm, JsValue thisV, JsValue[] args, bool atStart)
    {
        var s = CoerceThisString(realm, thisV);
        var maxLength = ToLengthJs(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var fill = args.Length > 1 && !args[1].IsUndefined ? ToStringArg(realm, args[1]) : " ";
        if (maxLength <= s.Length || fill.Length == 0)
        {
            return JsValue.String(s);
        }

        var needed = (int)Math.Min(maxLength - s.Length, MaxRepeatLength);
        var filler = RepeatToLength(fill, needed);
        return JsValue.String(atStart ? filler + s : s + filler);
    }

    private static JsValue Repeat(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var count = ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        if (count < 0 || count == long.MaxValue)
        {
            throw new JsThrow(realm.NewRangeError("Invalid repeat count"));
        }

        if (s.Length != 0 && count > MaxRepeatLength / s.Length)
        {
            throw new JsThrow(realm.NewRangeError("Repeat count too large"));
        }

        if (s.Length == 0 || count == 0)
        {
            return JsValue.String(string.Empty);
        }

        var sb = new StringBuilder(s.Length * (int)count);
        for (var i = 0; i < count; i++)
        {
            sb.Append(s);
        }

        return JsValue.String(sb.ToString());
    }

    private static JsValue Replace(JsRealm realm, JsValue thisV, JsValue[] args, bool replaceAll)
    {
        RequireCoercibleThis(realm, thisV);
        var searchValue = args.Length > 0 ? args[0] : JsValue.Undefined;
        var replacement = args.Length > 1 ? args[1] : JsValue.Undefined;
        // §22.1.3.19/§22.1.3.20 step 2: when searchValue is not nullish, look
        // up a callable [Symbol.replace] and delegate with the RAW receiver.
        // This is NOT RegExp-specific — any object exposing a callable
        // [Symbol.replace] participates (core-js feature-detects this).
        if (!searchValue.IsNullish)
        {
            // §22.1.3.20 step 2a: replaceAll requires a global regexp, checked
            // via the observable `flags` property, before @@replace lookup.
            if (replaceAll && IsRegExpSpec(realm, searchValue))
            {
                var flags = AbstractOperations.Get(realm.ActiveVm, searchValue.AsObject, "flags");
                if (flags.IsNullish)
                {
                    throw new JsThrow(realm.NewTypeError("String.prototype.replaceAll called with a RegExp whose flags are not object coercible"));
                }

                if (!ToStringArg(realm, flags).Contains('g'))
                {
                    throw new JsThrow(realm.NewTypeError("String.prototype.replaceAll called with a non-global RegExp"));
                }
            }

            var replaceFn = AbstractOperations.GetMethod(realm.ActiveVm, searchValue, SymbolCtor.Replace);
            if (!replaceFn.IsUndefined)
            {
                if (searchValue.AsObject is JsRegExp re && replaceFn.IsObject
                    && ReferenceEquals(replaceFn.AsObject, realm.RegExpBuiltinSymbolReplace))
                {
                    return RegExpCtor.ReplaceString(realm, re, CoerceThisString(realm, thisV), replacement);
                }

                return AbstractOperations.Call(realm.ActiveVm, replaceFn, searchValue, new[] { DelegateReceiver(realm, thisV, replaceFn), replacement });
            }
        }

        var s = CoerceThisString(realm, thisV);
        var search = ToStringArg(realm, searchValue);
        var functional = AbstractOperations.IsCallable(replacement);
        var replStr = functional ? null : ToStringArg(realm, replacement);
        if (!replaceAll)
        {
            var pos = s.IndexOf(search, StringComparison.Ordinal);
            if (pos < 0)
            {
                return JsValue.String(s);
            }

            return JsValue.String(s[..pos] + ReplacementText(realm, replacement, replStr, search, pos, s) + s[(pos + search.Length)..]);
        }

        if (search.Length == 0)
        {
            var sbEmpty = new StringBuilder();
            for (var i = 0; i <= s.Length; i++)
            {
                sbEmpty.Append(ReplacementText(realm, replacement, replStr, search, i, s));
                if (i < s.Length)
                {
                    sbEmpty.Append(s[i]);
                }
            }
            return JsValue.String(sbEmpty.ToString());
        }

        var sb = new StringBuilder();
        var cursor = 0;
        while (cursor <= s.Length)
        {
            var pos = s.IndexOf(search, cursor, StringComparison.Ordinal);
            if (pos < 0)
            {
                break;
            }

            sb.Append(s, cursor, pos - cursor);
            sb.Append(ReplacementText(realm, replacement, replStr, search, pos, s));
            cursor = pos + search.Length;
        }
        sb.Append(s, cursor, s.Length - cursor);
        return JsValue.String(sb.ToString());
    }

    private static string ReplacementText(JsRealm realm, JsValue replacement, string? replStr, string matched, int position, string whole)
    {
        if (replStr is null)
        {
            var result = AbstractOperations.Call(realm.ActiveVm, replacement, JsValue.Undefined,
                new[] { JsValue.String(matched), JsValue.Number(position), JsValue.String(whole) });
            return ToStringArg(realm, result);
        }
        return GetSubstitution(replStr, matched, position, whole);
    }

    private static string GetSubstitution(string replacement, string matched, int position, string whole)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] != '$' || i + 1 >= replacement.Length)
            {
                sb.Append(replacement[i]);
                continue;
            }
            var next = replacement[++i];
            switch (next)
            {
                case '$': sb.Append('$'); break;
                case '&': sb.Append(matched); break;
                case '`': sb.Append(whole[..position]); break;
                case '\'': sb.Append(whole[(position + matched.Length)..]); break;
                default:
                    sb.Append('$').Append(next);
                    break;
            }
        }
        return sb.ToString();
    }

    private static JsValue Slice(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var len = s.Length;
        var start = ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var from = start < 0 ? Math.Max(len + start, 0) : Math.Min(start, len);
        var end = args.Length > 1 && !args[1].IsUndefined ? ToIntegerOrInfinity(realm, args[1]) : len;
        var to = end < 0 ? Math.Max(len + end, 0) : Math.Min(end, len);
        return JsValue.String(from >= to ? string.Empty : s.Substring((int)from, (int)(to - from)));
    }

    private static JsValue Match(JsRealm realm, JsValue thisV, JsValue[] args, bool all)
    {
        // §22.1.3.13 String.prototype.match / §22.1.3.14 matchAll: if the
        // argument is neither undefined nor null, delegate to its
        // [Symbol.match] / [Symbol.matchAll] method when present — the argument
        // need not be a genuine RegExp. (Only this delegation makes core-js's
        // DELEGATES_TO_SYMBOL feature-detect pass; without it core-js wraps the
        // native and recurses.)
        RequireCoercibleThis(realm, thisV);
        var regexp = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!regexp.IsNullish)
        {
            // §22.1.3.14 step 2a: matchAll on a RegExp demands the global flag,
            // observed via the `flags` property, before the @@matchAll lookup.
            if (all && IsRegExpSpec(realm, regexp))
            {
                var flags = AbstractOperations.Get(realm.ActiveVm, regexp.AsObject, "flags");
                if (flags.IsNullish)
                {
                    throw new JsThrow(realm.NewTypeError("String.prototype.matchAll called with a RegExp whose flags are not object coercible"));
                }

                if (!ToStringArg(realm, flags).Contains('g'))
                {
                    throw new JsThrow(realm.NewTypeError("matchAll requires a global regular expression"));
                }
            }

            var symbol = all ? SymbolCtor.MatchAll : SymbolCtor.Match;
            var matcher = AbstractOperations.GetMethod(realm.ActiveVm, regexp, symbol);
            if (!matcher.IsUndefined)
            {
                return AbstractOperations.Call(realm.ActiveVm, matcher, regexp, new[] { DelegateReceiver(realm, thisV, matcher) });
            }
        }

        var s = CoerceThisString(realm, thisV);
        var re = RegExpCtor.Create(realm, regexp.IsUndefined ? "" : ToStringArg(realm, regexp), all ? "g" : "");
        var fn = AbstractOperations.Get(realm.ActiveVm, re, JsPropertyKey.Symbol(all ? SymbolCtor.MatchAll : SymbolCtor.Match));
        return AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Object(re), new[] { JsValue.String(s) });
    }

    private static JsValue Search(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        RequireCoercibleThis(realm, thisV);
        var regexp = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!regexp.IsNullish)
        {
            var searcher = AbstractOperations.GetMethod(realm.ActiveVm, regexp, SymbolCtor.Search);
            if (!searcher.IsUndefined)
            {
                return AbstractOperations.Call(realm.ActiveVm, searcher, regexp, new[] { DelegateReceiver(realm, thisV, searcher) });
            }
        }

        var s = CoerceThisString(realm, thisV);
        var re = RegExpCtor.Create(realm, regexp.IsUndefined ? "" : ToStringArg(realm, regexp), "");
        var fn = AbstractOperations.Get(realm.ActiveVm, re, JsPropertyKey.Symbol(SymbolCtor.Search));
        return AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Object(re), new[] { JsValue.String(s) });
    }

    private static JsValue Split(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        RequireCoercibleThis(realm, thisV);
        var separator = args.Length > 0 ? args[0] : JsValue.Undefined;
        var limitArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (!separator.IsNullish)
        {
            var splitter = AbstractOperations.GetMethod(realm.ActiveVm, separator, SymbolCtor.Split);
            if (!splitter.IsUndefined)
            {
                return AbstractOperations.Call(realm.ActiveVm, splitter, separator, new[] { DelegateReceiver(realm, thisV, splitter), limitArg });
            }
        }

        var s = CoerceThisString(realm, thisV);
        var limit = limitArg.IsUndefined ? uint.MaxValue : ToUint32(realm, limitArg);
        if (separator.IsUndefined)
        {
            var whole = new List<JsValue>();
            if (limit != 0)
            {
                whole.Add(JsValue.String(s));
            }

            return MakeArrayLike(realm, whole);
        }

        var sep = ToStringArg(realm, separator);
        var result = new List<JsValue>();
        if (limit == 0)
        {
            return MakeArrayLike(realm, result);
        }

        if (sep.Length == 0)
        {
            for (var i = 0; i < s.Length && result.Count < limit; i++)
            {
                result.Add(JsValue.String(s[i].ToString()));
            }

            return MakeArrayLike(realm, result);
        }
        var cursor = 0;
        while (result.Count < limit)
        {
            var pos = s.IndexOf(sep, cursor, StringComparison.Ordinal);
            if (pos < 0)
            {
                break;
            }

            result.Add(JsValue.String(s[cursor..pos]));
            cursor = pos + sep.Length;
        }
        if (result.Count < limit)
        {
            result.Add(JsValue.String(s[cursor..]));
        }

        return MakeArrayLike(realm, result);
    }

    private static JsValue StartsWith(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var searchArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (IsRegExpSpec(realm, searchArg))
        {
            throw new JsThrow(realm.NewTypeError("First argument to String.prototype.startsWith must not be a regular expression"));
        }

        var search = ToStringArg(realm, searchArg);
        var pos = Clamp(ToIntegerOrInfinity(realm, args.Length > 1 ? args[1] : JsValue.Undefined), 0, s.Length);
        return JsValue.Boolean((int)pos + search.Length <= s.Length
            && s.AsSpan((int)pos, search.Length).SequenceEqual(search));
    }

    private static JsValue Substring(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var start = Clamp(ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined), 0, s.Length);
        var end = args.Length > 1 && !args[1].IsUndefined
            ? Clamp(ToIntegerOrInfinity(realm, args[1]), 0, s.Length)
            : s.Length;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return JsValue.String(s.Substring((int)start, (int)(end - start)));
    }

    // Annex B §B.2.2.1 String.prototype.substr(start[, length]).
    private static JsValue Substr(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var size = (long)s.Length;
        var intStart = ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        long lstart;
        if (intStart == long.MinValue)
        {
            lstart = 0;
        }
        else if (intStart < 0)
        {
            lstart = Math.Max(size + intStart, 0);
        }
        else
        {
            lstart = Math.Min(intStart, size);
        }

        long resultLen;
        if (args.Length <= 1 || args[1].IsUndefined)
        {
            resultLen = size - lstart;
        }
        else
        {
            var lenNum = ToIntegerOrInfinity(realm, args[1]);
            if (lenNum <= 0)
            {
                return JsValue.String("");
            }

            resultLen = Math.Min(lenNum == long.MaxValue ? size : lenNum, size - lstart);
        }
        if (resultLen <= 0)
        {
            return JsValue.String("");
        }

        return JsValue.String(s.Substring((int)lstart, (int)resultLen));
    }

    // Annex B §B.2.2 CreateHTML: wrap the coerced receiver in a tag, escaping
    // double quotes in the attribute value.
    private static JsValue CreateHtml(JsRealm realm, JsValue thisV, string tag, string attribute, JsValue[] args)
    {
        var s = CoerceThisString(realm, thisV);
        var sb = new StringBuilder("<").Append(tag);
        if (attribute.Length != 0)
        {
            var v = ToStringArg(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            sb.Append(' ').Append(attribute).Append("=\"").Append(v.Replace("\"", "&quot;", StringComparison.Ordinal)).Append('"');
        }
        sb.Append('>').Append(s).Append("</").Append(tag).Append('>');
        return JsValue.String(sb.ToString());
    }

    private static bool IsWellFormedString(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }
        return true;
    }

    private static string ToWellFormedString(string s)
    {
        if (IsWellFormedString(s))
        {
            return s;
        }

        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1]))
                {
                    i++;
                }
                else
                {
                    chars[i] = '\uFFFD';
                }
            }
            else if (char.IsLowSurrogate(c))
            {
                chars[i] = '\uFFFD';
            }
        }
        return new string(chars);
    }

    // §22.1.3.21 String.prototype.split returns a genuine Array (ArrayCreate),
    // not a bare array-like — callers do `.split(x).join(y)`, `.map(...)`, etc.,
    // which require Array.prototype. (Was returning an ordinary object, so those
    // methods were undefined.)
    private static JsValue MakeArrayLike(JsRealm realm, List<JsValue> items)
        => JsValue.Object(new JsArray(realm, items));

    private static string RepeatToLength(string fill, int length)
    {
        var sb = new StringBuilder(length);
        while (sb.Length < length)
        {
            sb.Append(fill);
        }

        return sb.ToString(0, length);
    }

    private static string TrimJs(string s, bool trimStart, bool trimEnd)
    {
        var start = 0;
        var end = s.Length - 1;
        if (trimStart)
        {
            while (start <= end && IsJsWhiteSpace(s[start]))
            {
                start++;
            }
        }

        if (trimEnd)
        {
            while (end >= start && IsJsWhiteSpace(s[end]))
            {
                end--;
            }
        }

        return start > end ? string.Empty : s[start..(end + 1)];
    }

    // §12.2 WhiteSpace ∪ §12.3 LineTerminator — NOT .NET's set: U+0085 and
    // U+200B are excluded, U+FEFF is included.
    private static bool IsJsWhiteSpace(char c) => c switch
    {
        '\t' or '\n' or '\v' or '\f' or '\r' or ' ' => true,
        '\u00A0' or '\u1680' or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF' => true,
        >= '\u2000' and <= '\u200A' => true,
        _ => false,
    };

    private static long Clamp(long value, long min, long max) => Math.Min(Math.Max(value, min), max);

    private static bool IsIntegral(double d) => !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Truncate(d);

    /// <summary>§7.1.5 ToIntegerOrInfinity with ±infinity mapped to
    /// long.MaxValue/long.MinValue sentinels.</summary>
    private static long ToIntegerOrInfinity(JsRealm realm, JsValue value)
        => IntegerOrInfinityOf(ToNumberJs(realm, value));

    private static long IntegerOrInfinityOf(double n)
    {
        if (double.IsNaN(n) || n == 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(n))
        {
            return long.MaxValue;
        }

        if (double.IsNegativeInfinity(n))
        {
            return long.MinValue;
        }

        return (long)Math.Truncate(n);
    }

    /// <summary>§7.1.20 LengthOfArrayLike's ToLength clamp.</summary>
    private static long ToLengthJs(JsRealm realm, JsValue value)
    {
        var len = ToIntegerOrInfinity(realm, value);
        if (len <= 0)
        {
            return 0;
        }

        return Math.Min(len, int.MaxValue);
    }

    /// <summary>§7.1.6 ToInt32/§7.1.7 ToUint32 modulo arithmetic.</summary>
    private static uint ToUint32(JsRealm realm, JsValue value)
    {
        var n = ToNumberJs(realm, value);
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0)
        {
            return 0;
        }

        var i = Math.Truncate(n);
        var mod = i - Math.Floor(i / 4294967296.0) * 4294967296.0;
        return (uint)mod;
    }

    private static ushort ToUint16(JsRealm realm, JsValue value)
    {
        var n = ToNumberJs(realm, value);
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0)
        {
            return 0;
        }

        var i = Math.Truncate(n);
        var mod = i - Math.Floor(i / 65536.0) * 65536.0;
        return (ushort)mod;
    }
}
