using System.Globalization;
using System.Numerics;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>Exact decimal value + rounding engine backing Intl.NumberFormat
/// (§15.5.3 FormatNumericToString and friends). Values are digit strings with
/// a base-10 exponent so rounding never goes through binary floating point.</summary>
public static partial class IntlObj
{
    private sealed class DecimalNum
    {
        public bool IsNaN;
        public int Inf;               // 0 finite, +1/-1 infinity
        public bool Negative;         // sign, including negative zero
        public string Digits = "0";   // significant digits, normalized (no leading/trailing zeros; "0" for zero)
        public int Exponent;          // value = d1.d2...dn x 10^Exponent

        public bool IsZero => Inf == 0 && !IsNaN && Digits == "0";
        public bool IsFinite => !IsNaN && Inf == 0;

        public DecimalNum Shift(int k)
        {
            if (!IsFinite || IsZero || k == 0)
            {
                return this;
            }

            return new DecimalNum { Negative = Negative, Digits = Digits, Exponent = Exponent + k };
        }

        public static DecimalNum NaNValue() => new() { IsNaN = true };

        public static DecimalNum Infinity(bool negative) => new() { Inf = negative ? -1 : 1, Negative = negative };

        public static DecimalNum Zero(bool negative) => new() { Negative = negative };

        public static DecimalNum Normalize(string digits, int exponentOfFirst, bool negative)
        {
            var start = 0;
            while (start < digits.Length && digits[start] == '0')
            {
                start++;
            }

            if (start == digits.Length)
            {
                return Zero(negative);
            }

            var end = digits.Length;
            while (end > start + 1 && digits[end - 1] == '0')
            {
                end--;
            }

            return new DecimalNum
            {
                Negative = negative,
                Digits = digits[start..end],
                Exponent = exponentOfFirst - start,
            };
        }

        public static DecimalNum FromDouble(double d)
        {
            if (double.IsNaN(d))
            {
                return NaNValue();
            }

            if (double.IsInfinity(d))
            {
                return Infinity(d < 0);
            }

            var negative = double.IsNegative(d);
            var a = Math.Abs(d);
            if (a == 0)
            {
                return Zero(negative);
            }

            return ParseUnsignedDecimal(a.ToString("R", CultureInfo.InvariantCulture), negative)
                ?? Zero(negative);
        }

        public static DecimalNum FromBigInteger(BigInteger b)
        {
            var negative = b.Sign < 0;
            var s = BigInteger.Abs(b).ToString(CultureInfo.InvariantCulture);
            return Normalize(s, s.Length - 1, negative);
        }

        /// <summary>StringNumericLiteral (§7.1.4.1) interpreted exactly —
        /// arbitrary precision, no double round-trip.</summary>
        public static DecimalNum FromNumericString(string raw)
        {
            var s = raw.AsSpan();
            var start = 0;
            var end = s.Length;
            while (start < end && IsJsWhiteSpace(s[start]))
            {
                start++;
            }

            while (end > start && IsJsWhiteSpace(s[end - 1]))
            {
                end--;
            }

            s = s[start..end];
            if (s.Length == 0)
            {
                return Zero(false);
            }

            var negative = false;
            if (s[0] is '+' or '-')
            {
                negative = s[0] == '-';
                s = s[1..];
            }

            if (s.SequenceEqual("Infinity"))
            {
                return Infinity(negative);
            }

            if (s.Length > 2 && s[0] == '0' && (s[1] is 'x' or 'X' or 'o' or 'O' or 'b' or 'B'))
            {
                // Non-decimal literals take no sign per StringNumericLiteral.
                if (negative || raw.AsSpan(start, end - start)[0] is '+' or '-')
                {
                    return NaNValue();
                }

                var radix = s[1] is 'x' or 'X' ? 16 : s[1] is 'o' or 'O' ? 8 : 2;
                var acc = BigInteger.Zero;
                for (var i = 2; i < s.Length; i++)
                {
                    var dv = HexDigitValue(s[i]);
                    if (dv < 0 || dv >= radix)
                    {
                        return NaNValue();
                    }

                    acc = acc * radix + dv;
                }

                return FromBigInteger(acc);
            }

            return ParseUnsignedDecimal(new string(s), negative) ?? NaNValue();
        }

        private static int HexDigitValue(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };

        private static bool IsJsWhiteSpace(char c)
            => c is '\uFEFF' || char.IsWhiteSpace(c);

        /// <summary>Parses "digits[.digits][(e|E)[+-]digits]"; null on syntax error.</summary>
        private static DecimalNum? ParseUnsignedDecimal(string text, bool negative)
        {
            var mantissaEnd = text.Length;
            var exp10 = 0;
            var eIdx = text.IndexOfAny(['e', 'E']);
            if (eIdx >= 0)
            {
                mantissaEnd = eIdx;
                var expPart = text.AsSpan(eIdx + 1);
                if (expPart.Length == 0)
                {
                    return null;
                }

                var expNeg = false;
                if (expPart[0] is '+' or '-')
                {
                    expNeg = expPart[0] == '-';
                    expPart = expPart[1..];
                }

                if (expPart.Length == 0)
                {
                    return null;
                }

                long acc = 0;
                foreach (var c in expPart)
                {
                    if (!char.IsAsciiDigit(c))
                    {
                        return null;
                    }

                    acc = Math.Min(acc * 10 + (c - '0'), 1_000_000_000L);
                }

                exp10 = (int)(expNeg ? -acc : acc);
            }

            var mantissa = text.AsSpan(0, mantissaEnd);
            var dot = mantissa.IndexOf('.');
            var intLen = dot < 0 ? mantissa.Length : dot;
            var sb = new System.Text.StringBuilder(mantissa.Length);
            var digitCount = 0;
            for (var i = 0; i < mantissa.Length; i++)
            {
                if (i == dot)
                {
                    continue;
                }

                if (!char.IsAsciiDigit(mantissa[i]))
                {
                    return null;
                }

                sb.Append(mantissa[i]);
                digitCount++;
            }

            if (digitCount == 0)
            {
                return null;
            }

            // Place value of the first collected digit.
            var firstPlace = intLen - 1 + exp10;
            return Normalize(sb.ToString(), firstPlace, negative);
        }
    }

    /// <summary>Rounds |v| to a multiple of increment x 10^p using the given
    /// ECMA-402 rounding mode. Sign is preserved on the result.</summary>
    private static DecimalNum RoundDecimal(DecimalNum v, int p, int increment, string mode)
    {
        if (v.IsZero || !v.IsFinite)
        {
            return v;
        }

        var n = v.Digits.Length;
        var intLen = v.Exponent - p + 1;
        string intStr;
        string fracStr;
        if (intLen <= 0)
        {
            intStr = "0";
            fracStr = new string('0', -intLen) + v.Digits;
        }
        else if (intLen >= n)
        {
            intStr = intLen == n ? v.Digits : v.Digits + new string('0', intLen - n);
            fracStr = string.Empty;
        }
        else
        {
            intStr = v.Digits[..intLen];
            fracStr = v.Digits[intLen..];
        }

        var whole = BigInteger.Parse(intStr, CultureInfo.InvariantCulture);
        var (q, r) = BigInteger.DivRem(whole, increment);
        var fracNonZero = fracStr.AsSpan().IndexOfAnyExcept('0') >= 0;
        if (r.IsZero && !fracNonZero)
        {
            return MakeScaled(whole, p, v.Negative);
        }

        // Discarded = r + f with 0 <= f < 1 (f from fracStr). Compare twice the
        // discarded amount against the increment to classify below/half/above.
        var halfCmp = (r * 2).CompareTo(increment);
        if (halfCmp == 0)
        {
            halfCmp = fracNonZero ? 1 : 0;
        }
        else if (halfCmp < 0 && (r * 2 + 1).CompareTo(increment) == 0)
        {
            halfCmp = CompareFractionToHalf(fracStr);
        }

        var roundUp = mode switch
        {
            "expand" => true,
            "trunc" => false,
            "ceil" => !v.Negative,
            "floor" => v.Negative,
            "halfExpand" => halfCmp >= 0,
            "halfTrunc" => halfCmp > 0,
            "halfCeil" => halfCmp > 0 || (halfCmp == 0 && !v.Negative),
            "halfFloor" => halfCmp > 0 || (halfCmp == 0 && v.Negative),
            _ => halfCmp > 0 || (halfCmp == 0 && !q.IsEven),   // halfEven
        };
        var result = (roundUp ? q + BigInteger.One : q) * increment;
        return MakeScaled(result, p, v.Negative);
    }

    private static int CompareFractionToHalf(string frac)
    {
        if (frac.Length == 0)
        {
            return -1;
        }

        if (frac[0] != '5')
        {
            return frac[0] > '5' ? 1 : -1;
        }

        return frac.AsSpan(1).IndexOfAnyExcept('0') >= 0 ? 1 : 0;
    }

    private static DecimalNum MakeScaled(BigInteger units, int p, bool negative)
    {
        if (units.IsZero)
        {
            return DecimalNum.Zero(negative);
        }

        var s = units.ToString(CultureInfo.InvariantCulture);
        return DecimalNum.Normalize(s, p + s.Length - 1, negative);
    }

    private readonly record struct RawDigits(string Int, string Frac, DecimalNum Rounded, int Magnitude);

    /// <summary>ToRawPrecision (§15.5.8): round to maxSig significant digits,
    /// keep at least minSig (pad with zeros, strip extra trailing zeros).</summary>
    private static RawDigits ToRawPrecision(DecimalNum v, int minSig, int maxSig, string mode)
    {
        if (v.IsZero)
        {
            return new RawDigits("0", minSig > 1 ? new string('0', minSig - 1) : string.Empty, v, 1 - maxSig);
        }

        var r = RoundDecimal(v, v.Exponent - maxSig + 1, 1, mode);
        var e = r.Exponent;
        var shown = Math.Max(r.Digits.Length, minSig);
        var digits = r.Digits.Length < shown ? r.Digits + new string('0', shown - r.Digits.Length) : r.Digits;
        string intD;
        string fracD;
        if (e >= shown - 1)
        {
            intD = digits + new string('0', e - (shown - 1));
            fracD = string.Empty;
        }
        else if (e >= 0)
        {
            intD = digits[..(e + 1)];
            fracD = digits[(e + 1)..];
        }
        else
        {
            intD = "0";
            fracD = new string('0', -e - 1) + digits;
        }

        return new RawDigits(intD, fracD, r, e - maxSig + 1);
    }

    /// <summary>ToRawFixed (§15.5.9): round at 10^-maxFrac (with increment),
    /// pad the fraction to minFrac.</summary>
    private static RawDigits ToRawFixed(DecimalNum v, int minFrac, int maxFrac, int increment, string mode)
    {
        var r = RoundDecimal(v, -maxFrac, increment, mode);
        string intD;
        string fracD;
        if (r.IsZero)
        {
            intD = "0";
            fracD = new string('0', minFrac);
        }
        else
        {
            var e = r.Exponent;
            var digits = r.Digits;
            if (e >= 0)
            {
                if (digits.Length <= e + 1)
                {
                    intD = digits + new string('0', e + 1 - digits.Length);
                    fracD = string.Empty;
                }
                else
                {
                    intD = digits[..(e + 1)];
                    fracD = digits[(e + 1)..];
                }
            }
            else
            {
                intD = "0";
                fracD = new string('0', -e - 1) + digits;
            }

            if (fracD.Length < minFrac)
            {
                fracD += new string('0', minFrac - fracD.Length);
            }
        }

        return new RawDigits(intD, fracD, r, -maxFrac);
    }

    /// <summary>FormatNumericToString (§15.5.3) minus sign handling: picks the
    /// rounding strategy (incl. morePrecision/lessPrecision arbitration) and
    /// applies trailingZeroDisplay.</summary>
    private static RawDigits FormatNumericToString(NumberFormatState st, DecimalNum v)
    {
        RawDigits result;
        switch (st.RoundingType)
        {
            case "significantDigits":
                result = ToRawPrecision(v, st.MinSignificantDigits, st.MaxSignificantDigits, st.RoundingMode);
                break;
            case "fractionDigits":
                result = ToRawFixed(v, st.MinFractionDigits, st.MaxFractionDigits, st.RoundingIncrement, st.RoundingMode);
                break;
            default:
            {
                var s = ToRawPrecision(v, st.MinSignificantDigits, st.MaxSignificantDigits, st.RoundingMode);
                var f = ToRawFixed(v, st.MinFractionDigits, st.MaxFractionDigits, st.RoundingIncrement, st.RoundingMode);
                if (st.RoundingType == "morePrecision")
                {
                    result = s.Magnitude <= f.Magnitude ? s : f;
                }
                else
                {
                    result = s.Magnitude <= f.Magnitude ? f : s;
                }

                break;
            }
        }

        if (st.TrailingZeroDisplay == "stripIfInteger" && result.Frac.AsSpan().IndexOfAnyExcept('0') < 0)
        {
            result = result with { Frac = string.Empty };
        }

        return result;
    }

    private static DecimalNum ToIntlMathematicalValue(JsRealm realm, JsValue value)
    {
        var prim = AbstractOperations.ToPrimitive(realm.ActiveVm, value, "number");
        if (prim.IsBigInt)
        {
            return DecimalNum.FromBigInteger(prim.AsBigInt);
        }

        if (prim.IsString)
        {
            return DecimalNum.FromNumericString(prim.AsString);
        }

        if (prim.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a number"));
        }

        return DecimalNum.FromDouble(NumberCtor.ToNumber(prim));
    }
}
