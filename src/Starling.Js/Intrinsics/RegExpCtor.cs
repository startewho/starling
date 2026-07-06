using System.Buffers;
using System.Text;
using Starling.RegExp;
using Starling.Js.Runtime;
using Starling.Js.Runtime.Regex;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §22.2 The RegExp constructor and §22.2.6 RegExp.prototype. Wires the parser
/// → Pike-VM pipeline behind the JS-visible <c>new RegExp(…)</c>,
/// <c>RegExp.prototype.exec</c>, <c>.test</c>, and Symbol.* protocol hooks.
/// </summary>
public static class RegExpCtor
{
    public static void Install(JsRealm realm)
    {
        System.ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.RegExpPrototype;

        var ctor = new JsNativeFunction(realm, "RegExp", length: 2,
            (newTarget, args) => Construct(realm, newTarget, args),
            isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));

        var speciesGetter = new JsNativeFunction(realm, "get [Symbol.species]", 0,
            (thisV, _) => thisV, isConstructor: false);
        ctor.DefineOwnProperty(SymbolCtor.Species,
            PropertyDescriptor.Accessor(speciesGetter, null, enumerable: false, configurable: true));

        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));

        realm.RegExpBuiltinExec = IntrinsicHelpers.DefineMethod(realm, proto, "exec", 1, (thisV, args) => Exec(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, proto, "test", 1, (thisV, args) => Test(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, proto, "compile", 2, (thisV, args) => Compile(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0,
            (thisV, _) => RegExpToString(realm, thisV));

        IntrinsicHelpers.DefineMethod(realm, ctor, "escape", 1, (_, args) => Escape(realm, args));

        // Annex B / legacy-RegExp-features: static accessors on %RegExp%
        // reflecting the most recent successful BUILTIN match in this realm.
        // Getters throw TypeError when the receiver is not %RegExp% itself;
        // `input`/`$_` also has a setter.
        InstallLegacyStatics(realm, ctor);

        // Flag getters
        realm.RegExpGlobalGetter = DefineFlagGetter(realm, proto, "global", RegexFlags.Global);
        DefineFlagGetter(realm, proto, "ignoreCase", RegexFlags.IgnoreCase);
        DefineFlagGetter(realm, proto, "multiline", RegexFlags.Multiline);
        DefineFlagGetter(realm, proto, "dotAll", RegexFlags.DotAll);
        realm.RegExpUnicodeGetter = DefineFlagGetter(realm, proto, "unicode", RegexFlags.Unicode);
        DefineFlagGetter(realm, proto, "unicodeSets", RegexFlags.UnicodeSets);
        DefineFlagGetter(realm, proto, "sticky", RegexFlags.Sticky);
        DefineFlagGetter(realm, proto, "hasIndices", RegexFlags.HasIndices);

        DefineGetter(realm, proto, "source", (thisV) =>
        {
            if (thisV.IsObject && thisV.AsObject is JsRegExp re)
            {
                return JsValue.String(re.Source);
            }

            if (thisV.IsObject && ReferenceEquals(thisV.AsObject, realm.RegExpPrototype))
            {
                return JsValue.String("(?:)");
            }

            throw new JsThrow(realm.NewTypeError("get RegExp.prototype.source called on non-RegExp"));
        });
        DefineGetter(realm, proto, "flags", (thisV) =>
        {
            // §22.2.6.4 — GENERIC: reads each flag property off the receiver
            // via [[Get]] (observable; works on any object), not the internal
            // slots, so a plain object with getters builds a flag string too.
            if (!thisV.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("get RegExp.prototype.flags called on non-object"));
            }

            var obj = thisV.AsObject;
            var vm = realm.ActiveVm;
            var sb = new StringBuilder(8);
            void Add(string prop, char ch)
            {
                if (JsValue.ToBoolean(AbstractOperations.Get(vm, obj, prop)))
                {
                    sb.Append(ch);
                }
            }
            Add("hasIndices", 'd');
            Add("global", 'g');
            Add("ignoreCase", 'i');
            Add("multiline", 'm');
            Add("dotAll", 's');
            Add("unicode", 'u');
            Add("unicodeSets", 'v');
            Add("sticky", 'y');
            return JsValue.String(sb.ToString());
        });

        // Symbol.match / Symbol.replace / Symbol.search / Symbol.split / Symbol.matchAll
        DefineSymbolMethod(realm, proto, SymbolCtor.Match, "[Symbol.match]", 1,
            (thisV, args) => SymbolMatch(realm, thisV, args));
        DefineSymbolMethod(realm, proto, SymbolCtor.MatchAll, "[Symbol.matchAll]", 1,
            (thisV, args) => SymbolMatchAll(realm, thisV, args));
        DefineSymbolMethod(realm, proto, SymbolCtor.Replace, "[Symbol.replace]", 2,
            (thisV, args) => SymbolReplace(realm, thisV, args));
        DefineSymbolMethod(realm, proto, SymbolCtor.Search, "[Symbol.search]", 1,
            (thisV, args) => SymbolSearch(realm, thisV, args));
        DefineSymbolMethod(realm, proto, SymbolCtor.Split, "[Symbol.split]", 2,
            (thisV, args) => SymbolSplit(realm, thisV, args));

        realm.RegExpBuiltinSymbolReplace = proto.Get(SymbolCtor.Replace).AsObject;

        realm.RegExpConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("RegExp",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    // ------------------------------------------------------------------
    //                       Constructor
    // ------------------------------------------------------------------
    /// <summary>§22.2.5.1 RegExp.escape: escapes a string so it matches
    /// itself as pattern text. The argument must already be a String — no
    /// coercion.</summary>
    private static JsValue Escape(JsRealm realm, JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsString)
        {
            throw new JsThrow(realm.NewTypeError("RegExp.escape requires a string argument"));
        }

        var s = v.AsString;
        var sb = new StringBuilder(s.Length + 8);
        var i = 0;
        while (i < s.Length)
        {
            int cp = s[i];
            var units = 1;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                units = 2;
            }

            if (sb.Length == 0 && cp is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'))
            {
                // A leading alphanumeric is hex-escaped so the output stays
                // safe after \0, \1, or \c contexts.
                sb.Append('\\').Append('x');
                AppendHex(sb, cp, 2);
            }
            else
            {
                EncodeForRegExpEscape(sb, cp);
            }

            i += units;
        }

        return JsValue.String(sb.ToString());
    }

    private static void EncodeForRegExpEscape(StringBuilder sb, int cp)
    {
        switch (cp)
        {
            case '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '/':
                sb.Append('\\').Append((char)cp);
                return;
            case 0x09: sb.Append('\\').Append('t'); return;
            case 0x0A: sb.Append('\\').Append('n'); return;
            case 0x0B: sb.Append('\\').Append('v'); return;
            case 0x0C: sb.Append('\\').Append('f'); return;
            case 0x0D: sb.Append('\\').Append('r'); return;
        }

        var needsEscape =
            cp is ',' or '-' or '=' or '<' or '>' or '#' or '&' or '!' or '%' or ':' or ';' or '@' or '~' or '\'' or '`' or '"'
            || RegexCharClass.IsWhitespace(cp)
            || RegexCharClass.IsLineTerminator(cp)
            || cp is >= 0xD800 and <= 0xDFFF;
        if (!needsEscape)
        {
            if (cp <= 0xFFFF)
            {
                sb.Append((char)cp);
            }
            else
            {
                sb.Append(char.ConvertFromUtf32(cp));
            }

            return;
        }

        if (cp <= 0xFF)
        {
            sb.Append('\\').Append('x');
            AppendHex(sb, cp, 2);
            return;
        }

        // UnicodeEscape each UTF-16 code unit.
        if (cp <= 0xFFFF)
        {
            sb.Append('\\').Append('u');
            AppendHex(sb, cp, 4);
        }
        else
        {
            var offset = cp - 0x10000;
            sb.Append('\\').Append('u');
            AppendHex(sb, 0xD800 + (offset >> 10), 4);
            sb.Append('\\').Append('u');
            AppendHex(sb, 0xDC00 + (offset & 0x3FF), 4);
        }
    }

    private static void AppendHex(StringBuilder sb, int value, int digits)
    {
        for (var shift = (digits - 1) * 4; shift >= 0; shift -= 4)
        {
            var d = (value >> shift) & 0xF;
            sb.Append((char)(d < 10 ? '0' + d : 'a' + d - 10));
        }
    }

    private static JsValue Construct(JsRealm realm, JsValue newTarget, JsValue[] args)
    {
        // §22.2.4.1 RegExp(pattern, flags) — including the "regexp-like"
        // protocol: any object whose @@match is truthy contributes its
        // observable `source`/`flags` properties, and a plain (non-new) call
        // returns the argument unchanged when its `constructor` is %RegExp%.
        var vm = realm.ActiveVm;
        var patternArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        var flagsArg = args.Length > 1 ? args[1] : JsValue.Undefined;

        var isConstruct = newTarget.IsObject && AbstractOperations.IsConstructor(newTarget);
        var patternIsRegExp = IsRegExpLike(realm, patternArg);
        if (!isConstruct && patternIsRegExp && flagsArg.IsUndefined)
        {
            var patternCtor = AbstractOperations.Get(vm, patternArg.AsObject, "constructor");
            if (patternCtor.IsObject && realm.RegExpConstructor is { } rc
                && ReferenceEquals(patternCtor.AsObject, rc))
            {
                return patternArg;
            }
        }

        JsValue p;
        JsValue f;
        if (patternArg.IsObject && patternArg.AsObject is JsRegExp existing)
        {
            p = JsValue.String(existing.Source);
            f = flagsArg.IsUndefined ? JsValue.String(RegexFlagParser.ToFlagString(existing.Flags)) : flagsArg;
        }
        else if (patternIsRegExp)
        {
            p = AbstractOperations.Get(vm, patternArg.AsObject, "source");
            f = flagsArg.IsUndefined ? AbstractOperations.Get(vm, patternArg.AsObject, "flags") : flagsArg;
        }
        else
        {
            p = patternArg;
            f = flagsArg;
        }

        // RegExpInitialize: ToString(pattern) before ToString(flags); both
        // observable coercions run before any SyntaxError.
        var source = p.IsUndefined ? string.Empty : AbstractOperations.ToStringJs(vm, p);
        var flagsStr = f.IsUndefined ? string.Empty : AbstractOperations.ToStringJs(vm, f);
        if (!RegexFlagParser.TryParse(flagsStr, out var flags, out var err))
        {
            throw new JsThrow(realm.NewSyntaxError(err!));
        }

        IRegexMatcher compiled;
        try
        {
            compiled = RegexBackendSelector.Compile(source, flags);
        }
        catch (RegexSyntaxException ex)
        {
            throw new JsThrow(realm.NewSyntaxError($"Invalid regular expression: /{source}/: {ex.Message}"));
        }
        var re = new JsRegExp(realm, compiled);
        var instProto = IntrinsicHelpers.NewTargetPrototype(vm, newTarget, realm.RegExpPrototype);
        if (!ReferenceEquals(instProto, realm.RegExpPrototype))
        {
            re.SetPrototypeOf(instProto);
        }

        return JsValue.Object(re);
    }

    /// <summary>§7.2.8 IsRegExp: @@match (observable Get) decides when present;
    /// otherwise the internal matcher slot.</summary>
    private static bool IsRegExpLike(JsRealm realm, JsValue v)
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

    // Annex B §B.2.3.1 RegExp.prototype.compile(pattern, flags) — re-initialize
    // the receiver in place (legacy web API). The receiver must be a real
    // RegExp object; a RegExp pattern argument may not be paired with explicit
    // flags.
    private static JsValue Compile(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsRegExp re)
        {
            throw new JsThrow(realm.NewTypeError("RegExp.prototype.compile called on incompatible receiver"));
        }

        var patternArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        var flagsArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        string source;
        RegexFlags flags;
        if (patternArg.IsObject && patternArg.AsObject is JsRegExp existing)
        {
            if (!flagsArg.IsUndefined)
            {
                throw new JsThrow(realm.NewTypeError("Cannot supply flags when constructing one RegExp from another"));
            }

            source = existing.Source;
            flags = existing.Flags;
        }
        else
        {
            source = patternArg.IsUndefined ? string.Empty : JsValue.ToStringValue(patternArg);
            var flagsStr = flagsArg.IsUndefined ? string.Empty : JsValue.ToStringValue(flagsArg);
            if (!RegexFlagParser.TryParse(flagsStr, out flags, out var err))
            {
                throw new JsThrow(realm.NewSyntaxError(err!));
            }
        }

        IRegexMatcher compiled;
        try
        {
            compiled = RegexBackendSelector.Compile(source, flags);
        }
        catch (RegexSyntaxException ex)
        {
            throw new JsThrow(realm.NewSyntaxError($"Invalid regular expression: /{source}/: {ex.Message}"));
        }

        re.Recompile(compiled);
        AbstractOperations.Set(realm.ActiveVm, re, "lastIndex", JsValue.Number(0));
        return thisV;
    }

    // ------------------------------------------------------------------
    //                       prototype.exec / test
    // ------------------------------------------------------------------
    private static JsValue RegExpToString(JsRealm realm, JsValue thisV)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("RegExp.prototype.toString called on non-object"));
        }

        var obj = thisV.AsObject;
        var pattern = JsValue.ToStringValue(AbstractOperations.Get(realm.ActiveVm, obj, "source"));
        var flags = JsValue.ToStringValue(AbstractOperations.Get(realm.ActiveVm, obj, "flags"));
        return JsValue.String("/" + pattern + "/" + flags);
    }

    internal static JsValue Exec(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var re = RequireRegExp(realm, thisV);
        var vm = realm.ActiveVm;
        var input = args.Length > 0 ? AbstractOperations.ToStringJs(vm, args[0]) : "undefined";
        var advancing = (re.Flags & (RegexFlags.Global | RegexFlags.Sticky)) != 0;

        // §22.2.7.2 step 4: lastIndex = ToLength(?Get(R, "lastIndex")) — the
        // read (and any valueOf coercion) is observable even when the regexp
        // is neither global nor sticky.
        var lastIndex = ToLengthObservable(realm, AbstractOperations.Get(vm, re, "lastIndex"));
        if (!advancing)
        {
            lastIndex = 0;
        }

        if (lastIndex > input.Length)
        {
            if (advancing)
            {
                SetLastIndexOrThrow(realm, re, 0);
            }

            return JsValue.Null;
        }

        var start = (int)lastIndex;
        int bufLen = 2 * (re.Compiled.CaptureCount + 1);
        var spanBuffer = ArrayPool<int>.Shared.Rent(bufLen);
        try
        {
            if (!re.Compiled.ExecSpans(input, start, spanBuffer, out _, out var matchEnd))
            {
                if (advancing)
                {
                    SetLastIndexOrThrow(realm, re, 0);
                }

                return JsValue.Null;
            }
            if (advancing)
            {
                SetLastIndexOrThrow(realm, re, matchEnd);
            }

            RecordLegacyMatch(realm, re, input, spanBuffer);
            return JsValue.Object(BuildMatchArrayFromSpans(realm, re, input, spanBuffer));
        }
        finally
        {
            ArrayPool<int>.Shared.Return(spanBuffer);
        }
    }

    /// <summary>§7.1.20 ToLength with observable coercion (valueOf/toString on
    /// an object lastIndex runs user code).</summary>
    private static double ToLengthObservable(JsRealm realm, JsValue v)
    {
        if (!v.IsNumber)
        {
            v = AbstractOperations.ToPrimitive(realm.ActiveVm, v, "number");
        }

        var n = JsValue.ToNumber(v);
        if (double.IsNaN(n) || n <= 0)
        {
            return 0;
        }

        n = System.Math.Truncate(n);
        return System.Math.Min(n, 9007199254740991d);
    }

    /// <summary>Set(R, "lastIndex", v, true): a rejected write (e.g. the
    /// property was made non-writable) is a TypeError.</summary>
    private static void SetLastIndexOrThrow(JsRealm realm, JsObject re, double value)
    {
        if (!AbstractOperations.Set(realm.ActiveVm, re, "lastIndex", JsValue.Number(value)))
        {
            throw new JsThrow(realm.NewTypeError("Cannot assign to read only property 'lastIndex'"));
        }
    }

    /// <summary>§7.1.4 ToNumber with observable coercion; symbols reject.</summary>
    private static double ToNumberObservable(JsRealm realm, JsValue v)
    {
        if (!v.IsNumber)
        {
            v = AbstractOperations.ToPrimitive(realm.ActiveVm, v, "number");
        }

        if (v.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a number"));
        }

        return JsValue.ToNumber(v);
    }

    /// <summary>§7.1.5 ToIntegerOrInfinity clamped into int range.</summary>
    private static double ToIntegerObservable(JsRealm realm, JsValue v)
    {
        var n = ToNumberObservable(realm, v);
        if (double.IsNaN(n))
        {
            return 0;
        }

        return System.Math.Truncate(n);
    }

    /// <summary>§7.1.7 ToUint32 with observable coercion.</summary>
    private static uint ToUint32Observable(JsRealm realm, JsValue v)
    {
        var n = ToNumberObservable(realm, v);
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0)
        {
            return 0;
        }

        var i = System.Math.Truncate(n);
        var mod = i - System.Math.Floor(i / 4294967296.0) * 4294967296.0;
        return (uint)mod;
    }

    /// <summary>§7.3.24 SpeciesConstructor(rx, %RegExp%) — returns the
    /// constructor value to Construct with.</summary>
    private static JsValue SpeciesConstructorOf(JsRealm realm, JsObject obj)
    {
        var vm = realm.ActiveVm;
        var c = AbstractOperations.Get(vm, obj, "constructor");
        if (c.IsUndefined)
        {
            return JsValue.Object(realm.RegExpConstructor!);
        }

        if (!c.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("constructor is not an object"));
        }

        var sp = AbstractOperations.Get(vm, c.AsObject, JsPropertyKey.Symbol(SymbolCtor.Species));
        if (sp.IsUndefined || sp.IsNull)
        {
            return JsValue.Object(realm.RegExpConstructor!);
        }

        if (!AbstractOperations.IsConstructor(sp))
        {
            throw new JsThrow(realm.NewTypeError("@@species is not a constructor"));
        }

        return sp;
    }

    /// <summary>§7.2.10 SameValue for the lastIndex save/restore protocol
    /// (distinguishes -0 from +0; NaN equals NaN).</summary>
    private static bool SameValueJs(JsValue a, JsValue b)
    {
        if (a.IsNumber && b.IsNumber)
        {
            var da = a.AsNumber;
            var db = b.AsNumber;
            if (double.IsNaN(da) && double.IsNaN(db))
            {
                return true;
            }

            return da == db && double.IsNegative(da) == double.IsNegative(db);
        }

        return a.Equals(b);
    }

    internal static JsValue Test(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var result = Exec(realm, thisV, args);
        return JsValue.Boolean(!result.IsNull);
    }

    // Public-internal helper so JsRegExpStringIterator can build per-match
    // arrays without rerouting through prototype.exec (avoids re-entering the
    // dispatcher just to construct the same object). The iterator already holds
    // a freshly-filled span buffer for the current match.
    internal static JsArray BuildMatchArrayForIterator(JsRealm realm, JsRegExp re, string input, int[] spanBuffer)
        => BuildMatchArrayFromSpans(realm, re, input, spanBuffer);

    // Build the §22.2.7.2 result array from a span buffer already filled by
    // IRegexMatcher.ExecSpans. spanBuffer holds (start,end) pairs for groups
    // 0..CaptureCount, with (-1,-1) for a non-participating group. Substrings
    // are cut from `input` only at the point a group's text is materialized,
    // and the d-flag indices block reads the same spans (no extra GroupSpan
    // calls, no re-exec).
    private static JsArray BuildMatchArrayFromSpans(JsRealm realm, JsRegExp re, string input, int[] spanBuffer)
    {
        int captureCount = re.Compiled.CaptureCount;
        // Pre-size: group 0 + each capture group.
        var arr = new JsArray(realm, captureCount + 1);
        // index 0 = full match, then group captures.
        int m0s = spanBuffer[0];
        int m0e = spanBuffer[1];
        arr.Push(JsValue.String(m0s < 0 ? string.Empty : input.Substring(m0s, m0e - m0s)));
        for (var i = 1; i <= captureCount; i++)
        {
            int gs = spanBuffer[i * 2];
            int ge = spanBuffer[i * 2 + 1];
            arr.Push(gs < 0 ? JsValue.Undefined : JsValue.String(input.Substring(gs, ge - gs)));
        }
        // index / input / groups
        arr.DefineOwnProperty("index",
            PropertyDescriptor.Data(JsValue.Number(m0s), writable: true, enumerable: true, configurable: true));
        arr.DefineOwnProperty("input",
            PropertyDescriptor.Data(JsValue.String(input), writable: true, enumerable: true, configurable: true));
        // §22.2.7.2: groups is OrdinaryObjectCreate(null) when the pattern
        // has named groups, else undefined — but always an own property.
        JsValue groups = JsValue.Undefined;
        if (re.Compiled.NamedCaptures.Count > 0)
        {
            var g = new JsObject(null);
            foreach (var (name, idx) in re.Compiled.NamedCaptures)
            {
                int gs = spanBuffer[idx * 2];
                int ge = spanBuffer[idx * 2 + 1];
                g.DefineOwnProperty(name,
                    PropertyDescriptor.Data(gs < 0 ? JsValue.Undefined : JsValue.String(input.Substring(gs, ge - gs)),
                        writable: true, enumerable: true, configurable: true));
            }
            groups = JsValue.Object(g);
        }
        arr.DefineOwnProperty("groups",
            PropertyDescriptor.Data(groups, writable: true, enumerable: true, configurable: true));

        if ((re.Flags & RegexFlags.HasIndices) != 0)
        {
            var indicesArr = new JsArray(realm, captureCount + 1);
            JsObject? indexGroups = re.Compiled.NamedCaptures.Count > 0 ? new JsObject(null) : null;
            // Inverse map so each pair array is built once and shared with the
            // named entry, as MakeMatchIndicesIndexPairArray does.
            for (var i = 0; i <= captureCount; i++)
            {
                int gs = spanBuffer[i * 2];
                int ge = spanBuffer[i * 2 + 1];
                JsValue pairVal = JsValue.Undefined;
                if (gs >= 0)
                {
                    var pair = new JsArray(realm, 2);
                    pair.Push(JsValue.Number(gs));
                    pair.Push(JsValue.Number(ge));
                    pairVal = JsValue.Object(pair);
                }

                indicesArr.Push(pairVal);
                if (indexGroups is not null && i > 0)
                {
                    foreach (var (name, idx) in re.Compiled.NamedCaptures)
                    {
                        if (idx == i)
                        {
                            indexGroups.DefineOwnProperty(name,
                                PropertyDescriptor.Data(pairVal, writable: true, enumerable: true, configurable: true));
                        }
                    }
                }
            }

            indicesArr.DefineOwnProperty("groups",
                PropertyDescriptor.Data(indexGroups is null ? JsValue.Undefined : JsValue.Object(indexGroups),
                    writable: true, enumerable: true, configurable: true));
            arr.DefineOwnProperty("indices",
                PropertyDescriptor.Data(JsValue.Object(indicesArr), writable: true, enumerable: true, configurable: true));
        }
        return arr;
    }

    // ------------------------------------------------------------------
    //                       Symbol.* protocols
    // ------------------------------------------------------------------
    // §22.2.6.8 RegExp.prototype [ @@match ] ( string ). Each match MUST be
    // obtained through RegExpExec(R, S) (§22.2.7.1), which honors the receiver's
    // (possibly overridden) `exec`; the global/unicode flags are read generically
    // via Get. The previous implementation re-matched through the internal
    // engine and ignored a user `exec` — core-js's DELEGATES_TO_EXEC
    // feature-detect saw that as a broken native and installed a recursing
    // @@match polyfill (jQuery↔core-js infinite recursion → RangeError).
    private static JsValue SymbolMatch(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        // Step 2: `this` must be an Object (need not be a genuine RegExp — a
        // user object whose `exec`/`global`/`unicode` we read is allowed).
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("RegExp.prototype[Symbol.match] called on non-object"));
        }

        var rx = thisV.AsObject;
        var vm = realm.ActiveVm;
        var s = AbstractOperations.ToStringJs(vm, args.Length > 0 ? args[0] : JsValue.Undefined);

        // Fast path — when rx is a genuine RegExp whose exec and flag getters
        // are still the realm built-ins, none of the generic loop's property
        // reads can run user code, so loop straight against the compiled
        // matcher. The instant anything is overridden we fall through to the
        // fully observable generic algorithm.
        if (rx is JsRegExp fastRe && IsBuiltinExecAndFlags(realm, fastRe))
        {
            if ((fastRe.Flags & RegexFlags.Global) == 0)
            {
                return RegExpExec(realm, rx, s);
            }

            return GlobalMatchFastPath(realm, fastRe, s);
        }

        // §22.2.6.8: flags is read as a STRING off the receiver (this Get runs
        // the flags accessor, which itself reads the individual flag props).
        var flags = AbstractOperations.ToStringJs(vm, AbstractOperations.Get(vm, rx, "flags"));
        if (!flags.Contains('g'))
        {
            return RegExpExec(realm, rx, s);
        }

        bool fullUnicode = flags.Contains('u') || flags.Contains('v');
        SetLastIndexOrThrow(realm, rx, 0);
        var results = new JsArray(realm);
        while (true)
        {
            var result = RegExpExec(realm, rx, s);
            if (result.IsNull)
            {
                break;
            }

            var matchStr = AbstractOperations.ToStringJs(vm,
                AbstractOperations.Get(vm, result.AsObject, "0"));
            results.Push(JsValue.String(matchStr));
            // Step 8.b.iv: empty match → advance lastIndex so the loop terminates.
            if (matchStr.Length == 0)
            {
                var li = ToLengthObservable(realm, AbstractOperations.Get(vm, rx, "lastIndex"));
                var thisIndex = (int)System.Math.Min(li, s.Length + 1d);
                SetLastIndexOrThrow(realm, rx, AdvanceStringIndex(s, thisIndex, fullUnicode));
            }
        }
        return results.Length == 0 ? JsValue.Null : JsValue.Object(results);
    }

    // The @@match global fast path. Mirrors Exec's lastIndex bookkeeping exactly
    // (set 0 first, advance to match end per match, empty-match AdvanceStringIndex,
    // reset to 0 on no-match, return null when zero matches). Pushes only the
    // whole-match string per iteration; the full result array is never built.
    private static JsValue GlobalMatchFastPath(JsRealm realm, JsRegExp re, string s)
    {
        bool fullUnicode = (re.Flags & RegexFlags.Unicode) != 0;
        re.LastIndex = 0;
        var results = new JsArray(realm);
        int bufLen = 2 * (re.Compiled.CaptureCount + 1);
        var spanBuffer = ArrayPool<int>.Shared.Rent(bufLen);
        try
        {
            while (true)
            {
                var start = (int)System.Math.Max(0, re.LastIndex);
                if (start > s.Length)
                {
                    re.LastIndex = 0;
                    break;
                }
                if (!re.Compiled.ExecSpans(s, start, spanBuffer, out var matchStart, out var matchEnd))
                {
                    re.LastIndex = 0;
                    break;
                }
                re.LastIndex = matchEnd;
                int len = matchEnd - matchStart;
                results.Push(JsValue.String(len == 0 ? string.Empty : s.Substring(matchStart, len)));
                // Step 7.g.iv: empty match → advance lastIndex so the loop terminates.
                if (len == 0)
                {
                    var li = (int)re.LastIndex;
                    re.LastIndex = AdvanceStringIndex(s, li, fullUnicode);
                }
            }
            return results.Length == 0 ? JsValue.Null : JsValue.Object(results);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(spanBuffer);
        }
    }

    // True only when re.exec resolves to the realm's built-in exec and the
    // global/unicode flag getters resolve to the realm's built-in getters —
    // i.e. nothing on re or its prototype chain shadows them. Any override
    // disqualifies the fast path so the generic RegExpExec delegation runs.
    private static bool IsBuiltinExecAndFlags(JsRealm realm, JsRegExp re)
    {
        // Hot-path shortcut: a regexp that still carries the canonical
        // single-lastIndex shape owns no exec/global/unicode of its own, so
        // resolution falls straight to its prototype. If that prototype is the
        // realm's pristine RegExp.prototype with the builtin exec + flag getters
        // still in place, the guard holds without walking the chain or building a
        // descriptor per name. Anything off this path (own shadowing prop,
        // re-pointed __proto__, subclass) takes the full spec walk below.
        if (re.HasPristineShape && ReferenceEquals(re.GetPrototypeOf(), realm.RegExpPrototype)
            && ProtoBuiltinsIntact(realm))
        {
            return true;
        }

        return ResolvesToBuiltin(re, "exec", realm.RegExpBuiltinExec, accessor: false)
            && ResolvesToBuiltin(re, "global", realm.RegExpGlobalGetter, accessor: true)
            && ResolvesToBuiltin(re, "unicode", realm.RegExpUnicodeGetter, accessor: true);
    }

    // exec/global/unicode on RegExp.prototype still resolve to the builtins. The
    // prototype is dictionary-mode, so each lookup is a single hash probe with no
    // descriptor build beyond the returned struct; no prototype-chain walk.
    private static bool ProtoBuiltinsIntact(JsRealm realm)
    {
        // Steady state: the structural prototype-mutation epoch is unchanged
        // since we last confirmed the builtins intact. global/unicode are
        // accessors and exec is a method on RegExp.prototype; the only ways to
        // disturb them — defineProperty, delete, or migrating the prototype —
        // all bump ProtoEpoch. The single exception is a plain value
        // reassignment of the exec data property (`RegExp.prototype.exec = f`),
        // which rewrites a dictionary slot in place without a structural change,
        // so we re-probe exec (one hash lookup) even on an epoch hit. (We can't
        // make that write bump the epoch without adding cost to the very hot
        // generic dictionary-mode Set path, which globals in eval scope hit.)
        int epoch = JsObject.ProtoEpoch;
        if (epoch == realm.RegExpGuardCachedEpoch)
        {
            return realm.RegExpGuardCachedIntact && ExecStillBuiltin(realm);
        }

        bool fresh = ComputeProtoBuiltinsIntact(realm, realm.RegExpPrototype);
        realm.RegExpGuardCachedEpoch = epoch;
        realm.RegExpGuardCachedIntact = fresh;
        return fresh;
    }

    private static bool ExecStillBuiltin(JsRealm realm)
    {
        var ed = realm.RegExpPrototype.GetOwnPropertyDescriptor("exec");
        return ed is { IsAccessor: false } d && d.Value.IsObject
            && ReferenceEquals(d.Value.AsObject, realm.RegExpBuiltinExec);
    }

    private static bool ComputeProtoBuiltinsIntact(JsRealm realm, JsObject p)
    {
        if (!ExecStillBuiltin(realm))
        {
            return false;
        }

        var g = p.GetOwnPropertyDescriptor("global");
        if (g is not { IsAccessor: true } gd || !ReferenceEquals(gd.Getter, realm.RegExpGlobalGetter))
        {
            return false;
        }

        var u = p.GetOwnPropertyDescriptor("unicode");
        if (u is not { IsAccessor: true } ud || !ReferenceEquals(ud.Getter, realm.RegExpUnicodeGetter))
        {
            return false;
        }

        return true;
    }

    // Walk the own→prototype chain for `name`; the first object that owns it
    // wins. For an accessor property compare its getter to `expected`; for a
    // data property compare its value's object. Returns false if not found or
    // if it is the wrong kind, so a deleted/overridden slot fails the guard.
    private static bool ResolvesToBuiltin(JsObject start, string name, JsObject? expected, bool accessor)
    {
        if (expected is null)
        {
            return false;
        }

        for (var o = (JsObject?)start; o is not null; o = o.GetPrototypeOf())
        {
            var d = o.GetOwnPropertyDescriptor(name);
            if (d is null)
            {
                continue;
            }

            var desc = d.Value;
            if (accessor)
            {
                return desc.IsAccessor && ReferenceEquals(desc.Getter, expected);
            }

            return !desc.IsAccessor && desc.Value.IsObject && ReferenceEquals(desc.Value.AsObject, expected);
        }
        return false;
    }

    // §22.2.7.3 AdvanceStringIndex — step over a full code point when the
    // `unicode` flag is set and `index` sits on a leading surrogate.
    internal static int AdvanceStringIndex(string s, int index, bool unicode)
    {
        if (!unicode || index + 1 >= s.Length)
        {
            return index + 1;
        }

        var first = s[index];
        if (first < 0xD800 || first > 0xDBFF)
        {
            return index + 1;
        }

        var second = s[index + 1];
        if (second < 0xDC00 || second > 0xDFFF)
        {
            return index + 1;
        }

        return index + 2;
    }

    // §22.2.6.9 RegExp.prototype [ @@matchAll ] ( string ). Returns a
    // RegExpStringIterator (the same iterator String.prototype.matchAll
    // builds), so delegating String#matchAll through this method preserves the
    // iterator shape (Array.isArray(...) === false) the spec mandates.
    private static JsValue SymbolMatchAll(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        // §22.2.6.9 — fully generic: any Object receiver; the matcher is
        // species-constructed from (R, flags-string) with every read
        // observable; the global TypeError belongs to String.prototype
        // .matchAll, NOT here.
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("RegExp.prototype[Symbol.matchAll] called on non-object"));
        }

        var r = thisV.AsObject;
        var vm = realm.ActiveVm;
        var s = AbstractOperations.ToStringJs(vm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var species = SpeciesConstructorOf(realm, r);
        var flags = AbstractOperations.ToStringJs(vm, AbstractOperations.Get(vm, r, "flags"));
        var matcherV = AbstractOperations.Construct(vm, species, new[] { thisV, JsValue.String(flags) });

        if (!matcherV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Constructed matcher is not an object"));
        }

        var matcher = matcherV.AsObject;
        var lastIndex = ToLengthObservable(realm, AbstractOperations.Get(vm, r, "lastIndex"));
        AbstractOperations.Set(vm, matcher, "lastIndex", JsValue.Number(lastIndex));
        var global = flags.Contains('g');
        var unicode = flags.Contains('u') || flags.Contains('v');
        return JsValue.Object(new JsRegExpStringIterator(realm, matcher, s, global, unicode));
    }

    // §22.2.7.1 RegExpExec ( R, S ): read R.exec; if callable, call it and
    // require an Object-or-null result; otherwise fall back to the built-in
    // RegExp.prototype.exec. Honoring the user-visible `exec` property is what
    // lets a subclass / monkeypatched exec drive @@replace/@@match/@@split —
    // core-js feature-detects this delegation (DELEGATES_TO_EXEC) and installs
    // a recursing polyfill when it is missing.
    internal static JsValue RegExpExec(JsRealm realm, JsObject r, string s)
    {
        var vm = realm.ActiveVm;
        var exec = AbstractOperations.Get(vm, r, "exec");
        if (AbstractOperations.IsCallable(exec))
        {
            var result = AbstractOperations.Call(vm, exec, JsValue.Object(r),
                new[] { JsValue.String(s) });
            if (!result.IsObject && !result.IsNull)
            {
                throw new JsThrow(realm.NewTypeError(
                    "RegExp exec method returned something other than an Object or null"));
            }

            return result;
        }
        // Fallback: must be a real RegExp to run the built-in.
        if (r is not JsRegExp)
        {
            throw new JsThrow(realm.NewTypeError("RegExp#exec called on incompatible receiver"));
        }

        return Exec(realm, JsValue.Object(r), new[] { JsValue.String(s) });
    }

    private static JsValue SymbolReplace(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        // §22.2.6.11 RegExp.prototype[@@replace]. `this` must be an Object but
        // need not be a genuine RegExp (it can be a user object whose `exec`
        // and `global` we read), so we do not RequireRegExp here.
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("RegExp.prototype[Symbol.replace] called on non-object"));
        }

        var s = AbstractOperations.ToStringJs(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var replacement = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (thisV.AsObject is JsRegExp re)
        {
            return ReplaceString(realm, re, s, replacement);
        }

        return ReplaceGeneric(realm, thisV.AsObject, s, replacement);
    }

    /// <summary>Direct entry for a genuine RegExp receiver — used by @@replace
    /// and by <c>String.prototype.replace</c>'s fast path so a
    /// <c>str.replace(/re/, "...")</c> call skips the @@replace lookup, the
    /// per-call argument array, and the native Call dispatch. Takes the
    /// span-based fast path when nothing is overridden, else the generic loop.</summary>
    internal static JsValue ReplaceString(JsRealm realm, JsRegExp re, string s, JsValue replacement)
    {
        bool functional = AbstractOperations.IsCallable(replacement);
        string replStr = functional ? null! : AbstractOperations.ToStringJs(realm.ActiveVm, replacement);
        if ((re.Flags & RegexFlags.Sticky) == 0 && re.Compiled.NamedCaptures.Count == 0
            && IsBuiltinExecAndFlags(realm, re))
        {
            return ReplaceFast(realm, re, s, replacement, functional, replStr);
        }
        return ReplaceGeneric(realm, re, s, replacement);
    }

    // §22.2.6.11 generic path — honors a user-overridden exec or a non-RegExp
    // receiver: gathers matches via RegExpExec, then applies GetSubstitution.
    private static JsValue ReplaceGeneric(JsRealm realm, JsObject rx, string s, JsValue replacement)
    {
        var vm = realm.ActiveVm;
        bool functional = AbstractOperations.IsCallable(replacement);
        string replStr = functional ? null! : AbstractOperations.ToStringJs(vm, replacement);
        // §22.2.6.11: g/u come from the flags STRING read off the receiver.
        var flags = AbstractOperations.ToStringJs(vm, AbstractOperations.Get(vm, rx, "flags"));
        bool global = flags.Contains('g');
        bool flagsFullUnicode = flags.Contains('u') || flags.Contains('v');
        if (global)
        {
            SetLastIndexOrThrow(realm, rx, 0);
        }

        // Step 14: gather every match through RegExpExec (which calls rx.exec).
        var results = new List<JsValue>();
        while (true)
        {
            var result = RegExpExec(realm, rx, s);
            if (result.IsNull)
            {
                break;
            }

            results.Add(result);
            if (!global)
            {
                break;
            }
            // Empty-match advance (§22.2.6.11 step 14.d.iii).
            var resObj = result.AsObject;
            var matchStr = AbstractOperations.ToStringJs(vm, AbstractOperations.Get(vm, resObj, "0"));
            if (matchStr.Length == 0)
            {
                var li = ToLengthObservable(realm, AbstractOperations.Get(vm, rx, "lastIndex"));
                var thisIndex = (int)System.Math.Min(li, s.Length + 1d);
                SetLastIndexOrThrow(realm, rx, AdvanceStringIndex(s, thisIndex, flagsFullUnicode));
            }
        }

        var sb = new StringBuilder();
        int nextSourcePosition = 0;
        foreach (var result in results)
        {
            var resObj = result.AsObject;
            // Step 14: every read off the (possibly user-built) result object
            // and every coercion is observable, in spec order.
            int nCaptures = System.Math.Max(0,
                (int)ToLengthObservable(realm, AbstractOperations.Get(vm, resObj, "length")) - 1);
            var matched = AbstractOperations.ToStringJs(vm, AbstractOperations.Get(vm, resObj, "0"));
            int matchLength = matched.Length;
            var rawIndex = ToIntegerObservable(realm, AbstractOperations.Get(vm, resObj, "index"));
            int position = (int)System.Math.Max(0, System.Math.Min(rawIndex, s.Length));
            var captures = new List<JsValue>(nCaptures);
            for (var n = 1; n <= nCaptures; n++)
            {
                var cap = AbstractOperations.Get(vm, resObj, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                captures.Add(cap.IsUndefined ? JsValue.Undefined : JsValue.String(AbstractOperations.ToStringJs(vm, cap)));
            }
            var namedCaptures = AbstractOperations.Get(vm, resObj, "groups");

            string replacementText;
            if (functional)
            {
                var fnArgs = new List<JsValue> { JsValue.String(matched) };
                fnArgs.AddRange(captures);
                fnArgs.Add(JsValue.Number(position));
                fnArgs.Add(JsValue.String(s));
                if (!namedCaptures.IsUndefined)
                {
                    fnArgs.Add(namedCaptures);
                }

                var r = AbstractOperations.Call(vm, replacement, JsValue.Undefined, fnArgs.ToArray());
                replacementText = AbstractOperations.ToStringJs(vm, r);
            }
            else
            {
                // Step 14.k: ToObject(namedCaptures) happens up front (null
                // groups is a TypeError even when "$<" never appears).
                if (!namedCaptures.IsUndefined)
                {
                    namedCaptures = JsValue.Object(AbstractOperations.ToObject(realm, namedCaptures));
                }

                replacementText = GetSubstitution(realm, replStr, matched, s, position, captures, namedCaptures);
            }

            if (position >= nextSourcePosition)
            {
                sb.Append(s, nextSourcePosition, position - nextSourcePosition);
                sb.Append(replacementText);
                nextSourcePosition = position + matchLength;
            }
        }
        if (nextSourcePosition < s.Length)
        {
            sb.Append(s, nextSourcePosition, s.Length - nextSourcePosition);
        }

        return JsValue.String(sb.ToString());
    }

    // Fast @@replace for a built-in, non-sticky RegExp with no named captures.
    // One forward scan via ExecSpans; the result is assembled in a single
    // StringBuilder with no per-match allocation (no exec-result object, no
    // captures list, no property-bag reads). Observable behavior is identical to
    // the generic loop above for this class of receiver.
    private static JsValue ReplaceFast(JsRealm realm, JsRegExp re, string s, JsValue replacement, bool functional, string? replStr)
    {
        var vm = realm.ActiveVm;
        bool global = (re.Flags & RegexFlags.Global) != 0;
        bool fullUnicode = (re.Flags & RegexFlags.Unicode) != 0;
        int captureCount = re.Compiled.CaptureCount;
        // §22.2.6.11 step 12: a global replace sets lastIndex to 0 (and the
        // built-in scan leaves it 0 after the terminal no-match). Non-global,
        // non-sticky exec never touches lastIndex. The pristine-shape case (the
        // norm: this method is only reached when the builtin guard held) writes
        // slot 0 directly, skipping the generic setter's name lookup.
        if (global)
        {
            if (re.HasPristineShape)
            {
                re.ResetLastIndexFast();
            }
            else
            {
                re.LastIndex = 0;
            }
        }

        // Literal replacement (no '$' substitution tokens) on a backend that can
        // do a single-pass whole-string replace (the .NET engine): delegate the
        // whole operation, skipping the per-match scan and its allocations. The
        // Pike VM returns null here so its (already light) scan runs below.
        if (!functional && replStr is not null && replStr.IndexOf('$') < 0)
        {
            var whole = re.Compiled.TryReplaceLiteral(s, replStr, global);
            if (whole is not null)
            {
                return JsValue.String(whole);
            }
        }

        var spans = ArrayPool<int>.Shared.Rent(2 * (captureCount + 1));
        try
        {
            StringBuilder? sb = null;
            int nextSourcePosition = 0;
            int pos = 0;
            while (re.Compiled.ExecSpans(s, pos, spans, out int matchStart, out int matchEnd))
            {
                int matchLen = matchEnd - matchStart;
                string replacementText = functional
                    ? CallReplaceFn(vm, replacement, s, matchStart, matchEnd, spans, captureCount)
                    : GetSubstitutionFast(replStr!, s, matchStart, matchEnd, spans, captureCount);

                // Forward scan ⇒ matchStart >= nextSourcePosition always.
                sb ??= new StringBuilder(s.Length + 16);
                sb.Append(s, nextSourcePosition, matchStart - nextSourcePosition);
                sb.Append(replacementText);
                nextSourcePosition = matchStart + matchLen;

                if (!global)
                {
                    break;
                }

                pos = matchLen == 0 ? AdvanceStringIndex(s, matchEnd, fullUnicode) : matchEnd;
                if (pos > s.Length)
                {
                    break;
                }
            }
            if (sb is null)
            {
                return JsValue.String(s); // no match → original string, no allocation
            }

            if (nextSourcePosition < s.Length)
            {
                sb.Append(s, nextSourcePosition, s.Length - nextSourcePosition);
            }

            return JsValue.String(sb.ToString());
        }
        finally
        {
            ArrayPool<int>.Shared.Return(spans);
        }
    }

    // Build the (matched, cap1..capN, position, string) argument list for a
    // functional replacement directly from spans (no named-captures arg — the
    // fast path only runs when there are none).
    private static string CallReplaceFn(JsVm? vm, JsValue replacement, string s, int matchStart, int matchEnd, int[] spans, int captureCount)
    {
        var fnArgs = new JsValue[captureCount + 3];
        fnArgs[0] = JsValue.String(s.Substring(matchStart, matchEnd - matchStart));
        for (var c = 1; c <= captureCount; c++)
        {
            int cs = spans[2 * c], ce = spans[2 * c + 1];
            fnArgs[c] = cs < 0 ? JsValue.Undefined : JsValue.String(s.Substring(cs, ce - cs));
        }
        fnArgs[captureCount + 1] = JsValue.Number(matchStart);
        fnArgs[captureCount + 2] = JsValue.String(s);
        return JsValue.ToStringValue(AbstractOperations.Call(vm, replacement, JsValue.Undefined, fnArgs));
    }

    // §22.2.6.11.1 GetSubstitution working directly off match spans + the source
    // string (no captures list, no exec-result object). $<name> is treated as
    // literal — the fast path only runs when the regex has no named captures.
    private static string GetSubstitutionFast(string replacement, string str, int matchStart, int matchEnd, int[] spans, int captureCount)
    {
        // No '$' ⇒ the replacement is literal (the common case, e.g. "0"); skip
        // the StringBuilder entirely.
        if (replacement.IndexOf('$') < 0)
        {
            return replacement;
        }

        int position = matchStart;
        int tailPos = matchEnd;
        var sb = new StringBuilder(replacement.Length + 8);
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] != '$' || i + 1 >= replacement.Length) { sb.Append(replacement[i]); continue; }
            var next = replacement[i + 1];
            switch (next)
            {
                case '$': sb.Append('$'); i++; break;
                case '&': sb.Append(str, matchStart, matchEnd - matchStart); i++; break;
                case '`': sb.Append(str, 0, position); i++; break;
                case '\'': sb.Append(str, tailPos, str.Length - tailPos); i++; break;
                // No named captures here, so $<name> is literal $<name> (GetSubstitution
                // §22.2.6.11.1: refReplacement is "$<" when namedCaptures is undefined).
                // Emit "$<" and let the loop copy the rest of the token verbatim — emitting
                // only "$" would drop the "<" and corrupt the replacement (e.g. "$<x>"→"$x>").
                case '<': sb.Append('$').Append('<'); i++; break;
                default:
                    if (next >= '0' && next <= '9')
                    {
                        int n = next - '0';
                        int consumed = 1;
                        if (i + 2 < replacement.Length && replacement[i + 2] >= '0' && replacement[i + 2] <= '9')
                        {
                            var n2 = n * 10 + (replacement[i + 2] - '0');
                            if (n2 >= 1 && n2 <= captureCount) { n = n2; consumed = 2; }
                        }
                        if (n >= 1 && n <= captureCount)
                        {
                            int cs = spans[2 * n], ce = spans[2 * n + 1];
                            if (cs >= 0)
                            {
                                sb.Append(str, cs, ce - cs);
                            }

                            i += consumed;
                        }
                        else { sb.Append('$').Append(next); i++; }
                    }
                    else { sb.Append('$').Append(next); i++; }
                    break;
            }
        }
        return sb.ToString();
    }

    private static double ToLengthLocal(JsValue v)
    {
        var n = JsValue.ToNumber(v);
        if (double.IsNaN(n) || n <= 0)
        {
            return 0;
        }

        return System.Math.Min(System.Math.Floor(n), 9007199254740991d);
    }

    // §22.2.6.11.1 GetSubstitution — operates on the captures list + namedCaptures
    // object pulled from the exec result, per spec (not on the raw RegexMatch).
    private static string GetSubstitution(JsRealm realm, string replacement, string matched,
        string str, int position, List<JsValue> captures, JsValue namedCaptures)
    {
        var vm = realm.ActiveVm;
        int tailPos = position + matched.Length;
        int m = captures.Count;
        var sb = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] != '$' || i + 1 >= replacement.Length)
            {
                sb.Append(replacement[i]);
                continue;
            }
            var next = replacement[i + 1];
            switch (next)
            {
                case '$': sb.Append('$'); i++; break;
                case '&': sb.Append(matched); i++; break;
                case '`': sb.Append(str, 0, position); i++; break;
                case '\'': sb.Append(str, tailPos, str.Length - tailPos); i++; break;
                case '<':
                    {
                        // Named captures only honored when groups is not undefined
                        // (§22.2.6.11.1 step 11). Otherwise "$<" is literal —
                        // emit both chars so the '<' isn't swallowed.
                        if (namedCaptures.IsUndefined) { sb.Append('$').Append('<'); i++; break; }
                        var close = replacement.IndexOf('>', i + 2);
                        if (close < 0) { sb.Append('$').Append('<'); i++; break; }
                        var name = replacement.Substring(i + 2, close - (i + 2));
                        var groupsObj = AbstractOperations.ToObject(realm, namedCaptures);
                        var cap = AbstractOperations.Get(vm, groupsObj, name);
                        if (!cap.IsUndefined)
                        {
                            sb.Append(AbstractOperations.ToStringJs(vm, cap));
                        }

                        i = close;
                        break;
                    }
                default:
                    if (next >= '0' && next <= '9')
                    {
                        int n = next - '0';
                        int consumed = 1;
                        if (i + 2 < replacement.Length && replacement[i + 2] >= '0' && replacement[i + 2] <= '9')
                        {
                            var n2 = n * 10 + (replacement[i + 2] - '0');
                            if (n2 >= 1 && n2 <= m) { n = n2; consumed = 2; }
                        }
                        if (n >= 1 && n <= m)
                        {
                            var cap = captures[n - 1];
                            if (!cap.IsUndefined)
                            {
                                sb.Append(JsValue.ToStringValue(cap));
                            }

                            i += consumed;
                        }
                        else
                        {
                            sb.Append('$').Append(next);
                            i++;
                        }
                    }
                    else
                    {
                        sb.Append('$').Append(next);
                        i++;
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static JsValue SymbolSearch(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        // §22.2.6.12 — generic over any Object receiver: lastIndex is
        // saved/zeroed/restored through OBSERVABLE property ops and the match
        // runs through RegExpExec (honoring a user exec).
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("RegExp.prototype[Symbol.search] called on non-object"));
        }

        var vm0 = realm.ActiveVm;
        var s = AbstractOperations.ToStringJs(vm0, args.Length > 0 ? args[0] : JsValue.Undefined);
        if (thisV.AsObject is JsRegExp fastRe && IsBuiltinExecAndFlags(realm, fastRe))
        {
            var savedLastIndex = fastRe.LastIndex;
            fastRe.LastIndex = 0;
            var m = fastRe.Compiled.Exec(s, 0);
            fastRe.LastIndex = savedLastIndex;
            return JsValue.Number(m is null ? -1 : m.Start);
        }

        var rx = thisV.AsObject;
        var vm = realm.ActiveVm;
        // §22.2.6.12: save/zero/restore lastIndex with SameValue comparisons
        // (-0 must be written back over +0) and throwing Sets.
        var previous = AbstractOperations.Get(vm, rx, "lastIndex");
        if (!SameValueJs(previous, JsValue.Number(0)))
        {
            SetLastIndexOrThrow(realm, rx, 0);
        }

        var result = RegExpExec(realm, rx, s);
        var current = AbstractOperations.Get(vm, rx, "lastIndex");
        if (!SameValueJs(current, previous))
        {
            if (!AbstractOperations.Set(vm, rx, "lastIndex", previous))
            {
                throw new JsThrow(realm.NewTypeError("Cannot assign to read only property 'lastIndex'"));
            }
        }

        return result.IsNull
            ? JsValue.Number(-1)
            : AbstractOperations.Get(vm, result.AsObject, "index");
    }

    private static JsValue SymbolSplit(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("RegExp.prototype[Symbol.split] called on non-object"));
        }

        // The fast path additionally requires a Number/undefined limit — an
        // object limit's valueOf is observable and ordered AFTER the species
        // lookup, which only the generic path performs.
        var limitArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (thisV.AsObject is not JsRegExp || !IsBuiltinExecAndFlags(realm, (JsRegExp)thisV.AsObject)
            || !(limitArg.IsUndefined || limitArg.IsNumber))
        {
            return GenericSymbolSplit(realm, thisV.AsObject, args);
        }

        var re = (JsRegExp)thisV.AsObject;
        var s = AbstractOperations.ToStringJs(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var limit = limitArg.IsUndefined ? uint.MaxValue : ToUint32Observable(realm, limitArg);
        var arr = new JsArray(realm);
        if (limit == 0)
        {
            return JsValue.Object(arr);
        }

        int captureCount = re.Compiled.CaptureCount;
        bool unicode = (re.Flags & RegexFlags.Unicode) != 0;
        int bufLen = 2 * (captureCount + 1);
        var spanBuffer = ArrayPool<int>.Shared.Rent(bufLen);
        try
        {
            if (s.Length == 0)
            {
                // Per spec, if pattern matches empty string return [], else [""].
                if (!re.Compiled.ExecSpans(s, 0, spanBuffer, out _, out _))
                {
                    arr.Push(JsValue.String(string.Empty));
                }

                return JsValue.Object(arr);
            }
            int pos = 0;
            int prev = 0;
            while (pos < s.Length)
            {
                if (!re.Compiled.ExecSpans(s, pos, spanBuffer, out var matchStart, out var matchEnd))
                {
                    break;
                }

                if (matchEnd == prev)
                {
                    pos = AdvanceStringIndex(s, pos, unicode);
                    continue;
                }
                arr.Push(JsValue.String(s[prev..matchStart]));
                if (arr.Length >= limit)
                {
                    return JsValue.Object(arr);
                }
                // Push captures — substring only for participating groups.
                for (var i = 1; i <= captureCount; i++)
                {
                    int gs = spanBuffer[i * 2];
                    int ge = spanBuffer[i * 2 + 1];
                    arr.Push(gs < 0 ? JsValue.Undefined : JsValue.String(s.Substring(gs, ge - gs)));
                    if (arr.Length >= limit)
                    {
                        return JsValue.Object(arr);
                    }
                }
                prev = matchEnd;
                pos = matchEnd;
            }
            arr.Push(JsValue.String(s[prev..]));
            return JsValue.Object(arr);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(spanBuffer);
        }
    }

    // ------------------------------------------------------------------
    //                          Helpers
    // ------------------------------------------------------------------

    /// <summary>Compile a JS pattern string with default flags. Used by
    /// String.prototype methods to coerce non-RegExp first arguments.</summary>
    public static JsRegExp Create(JsRealm realm, string source, string flags = "")
    {
        if (!RegexFlagParser.TryParse(flags, out var f, out var err))
        {
            throw new JsThrow(realm.NewSyntaxError(err!));
        }

        IRegexMatcher compiled;
        try
        {
            compiled = RegexBackendSelector.Compile(source, f);
        }
        catch (RegexSyntaxException ex)
        {
            throw new JsThrow(realm.NewSyntaxError($"Invalid regular expression: /{source}/: {ex.Message}"));
        }
        return new JsRegExp(realm, compiled);
    }

    public static bool IsRegExp(JsValue v) => v.IsObject && v.AsObject is JsRegExp;

    /// <summary>§7.2.6 IsRegExp — the OBSERVABLE form: a @@match property that
    /// coerces truthy makes any object "a RegExp"; only when @@match is
    /// undefined does the internal slot decide.</summary>
    public static bool IsRegExpSpec(JsRealm realm, JsValue v)
    {
        if (!v.IsObject)
        {
            return false;
        }

        var matchV = AbstractOperations.Get(realm.ActiveVm, v.AsObject, JsPropertyKey.Symbol(SymbolCtor.Match));
        if (!matchV.IsUndefined)
        {
            return JsValue.ToBoolean(matchV);
        }

        return v.AsObject is JsRegExp;
    }

    /// <summary>§22.2.6.14 slow path — species-constructed sticky splitter
    /// driven entirely through RegExpExec and observable lastIndex ops, for
    /// subclassed/patched receivers. The pristine-regexp fast path stays on
    /// the compiled matcher.</summary>
    private static JsValue GenericSymbolSplit(JsRealm realm, JsObject rx, JsValue[] args)
    {
        var vm = realm.ActiveVm;
        // §22.2.6.14 order: S, species constructor, flags string, splitter
        // construction, then the limit's ToUint32 — each step observable.
        var s = AbstractOperations.ToStringJs(vm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var species = SpeciesConstructorOf(realm, rx);
        var flags = AbstractOperations.ToStringJs(vm, AbstractOperations.Get(vm, rx, "flags"));
        var newFlags = flags.Contains('y') ? flags : flags + "y";
        var unicode = flags.Contains('u') || flags.Contains('v');

        var splitterV = AbstractOperations.Construct(vm, species,
            new[] { JsValue.Object(rx), JsValue.String(newFlags) });
        if (!splitterV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Constructed splitter is not an object"));
        }

        var splitter = splitterV.AsObject;
        var arr = new JsArray(realm);
        var limitArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        var limit = limitArg.IsUndefined ? uint.MaxValue : ToUint32Observable(realm, limitArg);
        if (limit == 0)
        {
            return JsValue.Object(arr);
        }

        if (s.Length == 0)
        {
            var z0 = RegExpExec(realm, splitter, s);
            if (z0.IsNull)
            {
                arr.Push(JsValue.String(s));
            }

            return JsValue.Object(arr);
        }

        int p = 0, q = 0;
        while (q < s.Length)
        {
            AbstractOperations.Set(vm, splitter, "lastIndex", JsValue.Number(q));
            var z = RegExpExec(realm, splitter, s);
            if (z.IsNull)
            {
                q = AdvanceStringIndex(s, q, unicode);
                continue;
            }

            var e = (int)System.Math.Min(
                ToLengthObservable(realm, AbstractOperations.Get(vm, splitter, "lastIndex")),
                s.Length);
            if (e == p)
            {
                q = AdvanceStringIndex(s, q, unicode);
                continue;
            }

            arr.Push(JsValue.String(s[p..q]));
            if (arr.Length >= limit)
            {
                return JsValue.Object(arr);
            }

            p = e;
            var zLen = (long)ToLengthObservable(realm, AbstractOperations.Get(vm, z.AsObject, "length"));
            for (var i = 1; i < zLen; i++)
            {
                arr.Push(AbstractOperations.Get(vm, z.AsObject, i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (arr.Length >= limit)
                {
                    return JsValue.Object(arr);
                }
            }

            q = p;
        }

        arr.Push(JsValue.String(s[p..]));
        return JsValue.Object(arr);
    }

    private static JsRegExp RequireRegExp(JsRealm realm, JsValue v)
    {
        if (v.IsObject && v.AsObject is JsRegExp r)
        {
            return r;
        }

        throw new JsThrow(realm.NewTypeError("Method called on non-RegExp object"));
    }

    private static JsNativeFunction DefineFlagGetter(JsRealm realm, JsObject target, string name, RegexFlags flag)
    {
        return DefineGetter(realm, target, name, (thisV) =>
        {
            if (thisV.IsObject && thisV.AsObject is JsRegExp r)
            {
                return JsValue.Boolean((r.Flags & flag) != 0);
            }

            // §22.2.6: on %RegExp.prototype% itself the getter answers
            // undefined instead of throwing.
            if (thisV.IsObject && ReferenceEquals(thisV.AsObject, realm.RegExpPrototype))
            {
                return JsValue.Undefined;
            }

            throw new JsThrow(realm.NewTypeError($"Getter '{name}' called on non-RegExp"));
        });
    }

    private static JsNativeFunction DefineGetter(JsRealm realm, JsObject target, string name, System.Func<JsValue, JsValue> body)
    {
        var fn = new JsNativeFunction(realm, "get " + name, 0, (thisV, _) => body(thisV), isConstructor: false);
        target.DefineOwnProperty(name, PropertyDescriptor.Accessor(fn, null, enumerable: false, configurable: true));
        return fn;
    }

    private static void DefineSymbolMethod(JsRealm realm, JsObject target, JsSymbol key, string name, int length, System.Func<JsValue, JsValue[], JsValue> body)
    {
        var fn = new JsNativeFunction(realm, name, length, body, isConstructor: false);
        target.DefineOwnProperty(key, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
    }

    // ------------------------------------------------------------------
    //        Annex B legacy static accessors (RegExp.$1, lastMatch, …)
    // ------------------------------------------------------------------

    private static void RecordLegacyMatch(JsRealm realm, JsRegExp re, string input, int[] spanBuffer)
    {
        var captures = new string?[re.Compiled.CaptureCount];
        for (var i = 1; i <= re.Compiled.CaptureCount; i++)
        {
            var cs = spanBuffer[2 * i];
            var ce = spanBuffer[(2 * i) + 1];
            captures[i - 1] = cs >= 0 && ce >= cs ? input[cs..ce] : null;
        }

        realm.LegacyRegExpMatch = new JsRealm.LegacyMatchState(
            input, spanBuffer[0], spanBuffer[1], captures);
    }

    private static void InstallLegacyStatics(JsRealm realm, JsNativeFunction ctor)
    {
        JsRealm.LegacyMatchState Require(JsValue thisV)
        {
            // Legacy semantics: the receiver must be %RegExp% of THIS realm.
            if (!thisV.IsObject || !ReferenceEquals(thisV.AsObject, ctor))
            {
                throw new JsThrow(realm.NewTypeError("RegExp legacy static accessed on incompatible receiver"));
            }

            return realm.LegacyRegExpMatch ?? JsRealm.LegacyMatchState.Empty;
        }

        void Getter(string name, Func<JsRealm.LegacyMatchState, string> read, string? alias = null)
        {
            var get = new JsNativeFunction(realm, "get " + name, 0, (thisV, _) => JsValue.String(read(Require(thisV))));
            var desc = PropertyDescriptor.Accessor(get, null);
            ctor.DefineOwnProperty(name, desc);
            if (alias is not null)
            {
                ctor.DefineOwnProperty(alias, desc);
            }
        }

        // input / $_ — read-write.
        var inputGet = new JsNativeFunction(realm, "get input", 0, (thisV, _) => JsValue.String(Require(thisV).Input));
        var inputSet = new JsNativeFunction(realm, "set input", 1, (thisV, args) =>
        {
            var st = Require(thisV);
            var v = JsValue.ToStringValue(args.Length > 0 ? args[0] : JsValue.Undefined);
            realm.LegacyRegExpMatch = st with { Input = v };
            return JsValue.Undefined;
        });
        var inputDesc = PropertyDescriptor.Accessor(inputGet, inputSet);
        ctor.DefineOwnProperty("input", inputDesc);
        ctor.DefineOwnProperty("$_", inputDesc);

        Getter("lastMatch", st => SafeSlice(st.Input, st.MatchStart, st.MatchEnd), "$&");
        Getter("lastParen", st =>
        {
            for (var i = st.Captures.Length - 1; i >= 0; i--)
            {
                if (st.Captures[i] is { } c)
                {
                    return c;
                }
            }

            return "";
        }, "$+");
        Getter("leftContext", st => SafeSlice(st.Input, 0, st.MatchStart), "$`");
        Getter("rightContext", st => SafeSlice(st.Input, st.MatchEnd, st.Input.Length), "$'");
        for (var n = 1; n <= 9; n++)
        {
            var idx = n - 1;
            Getter("$" + n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                st => idx < st.Captures.Length ? st.Captures[idx] ?? "" : "");
        }

        static string SafeSlice(string input, int start, int end)
        {
            if (start < 0 || end < start || end > input.Length)
            {
                return "";
            }

            return input[start..end];
        }
    }
}
