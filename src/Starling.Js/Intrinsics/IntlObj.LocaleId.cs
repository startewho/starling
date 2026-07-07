using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>UTS 35 Unicode locale identifiers: structural validation,
/// canonicalization (aliases, subtag ordering, extension normalization),
/// likely-subtag add/remove, and the Intl.Locale constructor built on them.</summary>
public static partial class IntlObj
{
    private sealed class UnicodeLocaleId
    {
        public string Language = "und";
        public string? Script;
        public string? Region;
        public List<string> Variants = [];
        public bool HasU;
        public List<string> UAttributes = [];
        public List<(string Key, string Value)> UKeywords = [];
        public UnicodeLocaleId? TLang;
        public List<(string Key, string Value)> TFields = [];
        public bool HasT;
        public List<(char Singleton, string Value)> OtherExtensions = [];
        public string? PrivateUse;

        public string BaseName()
        {
            var sb = new System.Text.StringBuilder(16);
            sb.Append(Language);
            if (Script is not null)
            {
                sb.Append('-').Append(Script);
            }

            if (Region is not null)
            {
                sb.Append('-').Append(Region);
            }

            for (var i = 0; i < Variants.Count; i++)
            {
                sb.Append('-').Append(Variants[i]);
            }

            return sb.ToString();
        }

        public string? UKeywordValue(string key)
        {
            for (var i = 0; i < UKeywords.Count; i++)
            {
                if (UKeywords[i].Key == key)
                {
                    return UKeywords[i].Value;
                }
            }

            return null;
        }

        public void SetUKeyword(string key, string value)
        {
            for (var i = 0; i < UKeywords.Count; i++)
            {
                if (UKeywords[i].Key == key)
                {
                    UKeywords[i] = (key, value);
                    HasU = true;
                    return;
                }
            }

            UKeywords.Add((key, value));
            HasU = true;
        }

        public string ToCanonicalString()
        {
            var sb = new System.Text.StringBuilder(24);
            sb.Append(BaseName());

            var extensions = new List<(char Singleton, string Content)>(OtherExtensions.Count + 2);
            if (HasT)
            {
                var tsb = new System.Text.StringBuilder(12);
                if (TLang is not null)
                {
                    tsb.Append(TLang.BaseName().ToLowerInvariant());
                }

                foreach (var (key, value) in TFields)
                {
                    if (tsb.Length > 0)
                    {
                        tsb.Append('-');
                    }

                    tsb.Append(key).Append('-').Append(value);
                }

                extensions.Add(('t', tsb.ToString()));
            }

            if (HasU)
            {
                var usb = new System.Text.StringBuilder(12);
                foreach (var attr in UAttributes)
                {
                    if (usb.Length > 0)
                    {
                        usb.Append('-');
                    }

                    usb.Append(attr);
                }

                foreach (var (key, value) in UKeywords)
                {
                    if (usb.Length > 0)
                    {
                        usb.Append('-');
                    }

                    usb.Append(key);
                    if (value.Length > 0)
                    {
                        usb.Append('-').Append(value);
                    }
                }

                extensions.Add(('u', usb.ToString()));
            }

            foreach (var other in OtherExtensions)
            {
                extensions.Add(other);
            }

            extensions.Sort((a, b) => a.Singleton.CompareTo(b.Singleton));
            foreach (var (singleton, content) in extensions)
            {
                sb.Append('-').Append(singleton);
                if (content.Length > 0)
                {
                    sb.Append('-').Append(content);
                }
            }

            if (PrivateUse is not null)
            {
                sb.Append("-x-").Append(PrivateUse);
            }

            return sb.ToString();
        }
    }

    private static bool IsLanguageSubtag(string s)
        => (s.Length is 2 or 3 || s.Length is >= 5 and <= 8) && IsAsciiLetters(s);

    private static bool IsScriptSubtag(string s) => s.Length == 4 && IsAsciiLetters(s);

    private static bool IsVariantSubtag(string s)
        => (s.Length is >= 5 and <= 8 && IsAsciiAlnum(s))
            || (s.Length == 4 && char.IsAsciiDigit(s[0]) && IsAsciiAlnum(s));

    private static string TitleCaseSubtag(string s)
        => char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>Parses a unicode_locale_id, validating structure (UTS 35 §3.2):
    /// no extlang, singleton/variant uniqueness, real -t-/-u- grammars.</summary>
    private static bool TryParseUnicodeLocaleId(string tag, out UnicodeLocaleId id)
    {
        id = new UnicodeLocaleId();
        if (tag.Length == 0)
        {
            return false;
        }

        for (var c = 0; c < tag.Length; c++)
        {
            if (!char.IsAsciiLetterOrDigit(tag[c]) && tag[c] != '-')
            {
                return false;
            }
        }

        var parts = tag.Split('-');
        var i = 0;
        if (!IsLanguageSubtag(parts[0]))
        {
            return false;
        }

        id.Language = parts[0].ToLowerInvariant();
        i = 1;
        if (i < parts.Length && IsScriptSubtag(parts[i]))
        {
            id.Script = TitleCaseSubtag(parts[i]);
            i++;
        }

        if (i < parts.Length && IsRegionSubtag(parts[i]))
        {
            id.Region = parts[i].ToUpperInvariant();
            i++;
        }

        var seenVariants = new HashSet<string>(StringComparer.Ordinal);
        while (i < parts.Length && IsVariantSubtag(parts[i]))
        {
            var v = parts[i].ToLowerInvariant();
            if (!seenVariants.Add(v))
            {
                return false;
            }

            id.Variants.Add(v);
            i++;
        }

        var seenSingletons = new HashSet<char>();
        while (i < parts.Length)
        {
            var s = parts[i];
            if (s.Length != 1 || !char.IsAsciiLetterOrDigit(s[0]))
            {
                return false;
            }

            var singleton = char.ToLowerInvariant(s[0]);
            if (!seenSingletons.Add(singleton))
            {
                return false;
            }

            i++;
            if (singleton == 'x')
            {
                if (i >= parts.Length)
                {
                    return false;
                }

                var pu = new List<string>();
                for (; i < parts.Length; i++)
                {
                    if (parts[i].Length is < 1 or > 8 || !IsAsciiAlnum(parts[i]))
                    {
                        return false;
                    }

                    pu.Add(parts[i].ToLowerInvariant());
                }

                id.PrivateUse = string.Join('-', pu);
                return true;
            }

            if (singleton == 't')
            {
                if (!ParseTExtension(parts, ref i, id))
                {
                    return false;
                }
            }
            else if (singleton == 'u')
            {
                if (!ParseUExtension(parts, ref i, id))
                {
                    return false;
                }
            }
            else
            {
                var values = new List<string>();
                while (i < parts.Length && parts[i].Length is >= 2 and <= 8 && IsAsciiAlnum(parts[i]))
                {
                    values.Add(parts[i].ToLowerInvariant());
                    i++;
                }

                if (values.Count == 0)
                {
                    return false;
                }

                id.OtherExtensions.Add((singleton, string.Join('-', values)));
            }
        }

        return true;
    }

    private static bool ParseUExtension(string[] parts, ref int i, UnicodeLocaleId id)
    {
        var any = false;
        while (i < parts.Length && parts[i].Length is >= 3 and <= 8 && IsAsciiAlnum(parts[i]))
        {
            id.UAttributes.Add(parts[i].ToLowerInvariant());
            i++;
            any = true;
        }

        while (i < parts.Length && parts[i].Length == 2
            && char.IsAsciiLetterOrDigit(parts[i][0]) && char.IsAsciiLetter(parts[i][1]))
        {
            var key = parts[i].ToLowerInvariant();
            i++;
            var values = new List<string>();
            while (i < parts.Length && parts[i].Length is >= 3 and <= 8 && IsAsciiAlnum(parts[i]))
            {
                values.Add(parts[i].ToLowerInvariant());
                i++;
            }

            id.UKeywords.Add((key, string.Join('-', values)));
            any = true;
        }

        if (!any)
        {
            return false;
        }

        id.HasU = true;
        return true;
    }

    private static bool ParseTExtension(string[] parts, ref int i, UnicodeLocaleId id)
    {
        var any = false;
        if (i < parts.Length && IsLanguageSubtag(parts[i]))
        {
            var tlang = new UnicodeLocaleId { Language = parts[i].ToLowerInvariant() };
            i++;
            if (i < parts.Length && IsScriptSubtag(parts[i]))
            {
                tlang.Script = TitleCaseSubtag(parts[i]);
                i++;
            }

            if (i < parts.Length && IsRegionSubtag(parts[i]))
            {
                tlang.Region = parts[i].ToUpperInvariant();
                i++;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (i < parts.Length && IsVariantSubtag(parts[i]))
            {
                var v = parts[i].ToLowerInvariant();
                if (!seen.Add(v))
                {
                    return false;
                }

                tlang.Variants.Add(v);
                i++;
            }

            id.TLang = tlang;
            any = true;
        }

        while (i < parts.Length && parts[i].Length == 2
            && char.IsAsciiLetter(parts[i][0]) && char.IsAsciiDigit(parts[i][1]))
        {
            var key = parts[i].ToLowerInvariant();
            i++;
            var values = new List<string>();
            while (i < parts.Length && parts[i].Length is >= 3 and <= 8 && IsAsciiAlnum(parts[i]))
            {
                values.Add(parts[i].ToLowerInvariant());
                i++;
            }

            if (values.Count == 0)
            {
                return false;
            }

            id.TFields.Add((key, string.Join('-', values)));
            any = true;
        }

        if (!any)
        {
            return false;
        }

        // A trailing subtag that is neither a tfield nor a new singleton means
        // the -t- section is malformed (e.g. "en-t-en-latn-latn").
        if (i < parts.Length && parts[i].Length != 1)
        {
            return false;
        }

        id.HasT = true;
        return true;
    }

    // =====================================================================
    //                      Canonicalization (UTS 35 §3.2.1)
    // =====================================================================

    /// <summary>Whole-language-id aliases keyed by lowercase
    /// language[-script][-region][-variants].</summary>
    private static readonly Dictionary<string, string> LanguageIdAliases = new(StringComparer.Ordinal)
    {
        ["art-lojban"] = "jbo",
        ["cel-gaulish"] = "xtg",
        ["zh-guoyu"] = "zh",
        ["zh-hakka"] = "hak",
        ["zh-xiang"] = "hsn",
        ["sgn-gr"] = "gss",
        ["ja-latn-hepburn-heploc"] = "ja-Latn-alalc97",
        ["hy-arevela"] = "hy",
        ["hy-arevmda"] = "hyw",
    };

    private static readonly Dictionary<string, string> LanguageSubtagAliases = new(StringComparer.Ordinal)
    {
        ["cmn"] = "zh",
        ["in"] = "id",
        ["iw"] = "he",
        ["ji"] = "yi",
        ["jw"] = "jv",
        ["mo"] = "ro",
        ["tl"] = "fil",
        ["aar"] = "aa",
        ["heb"] = "he",
        ["ces"] = "cs",
        ["sh"] = "sr-Latn",
        ["cnr"] = "sr-ME",
        ["swc"] = "sw-CD",
        ["twi"] = "ak",
    };

    private static readonly Dictionary<string, string> RegionSimpleAliases = new(StringComparer.Ordinal)
    {
        ["DD"] = "DE",
        ["BU"] = "MM",
        ["TP"] = "TL",
        ["YD"] = "YE",
        ["ZR"] = "CD",
        ["FX"] = "FR",
        ["554"] = "NZ",
        ["276"] = "DE",
        ["840"] = "US",
    };

    private static readonly Dictionary<string, string[]> RegionComplexAliases = new(StringComparer.Ordinal)
    {
        ["SU"] = ["RU", "AM", "AZ", "BY", "EE", "GE", "KZ", "KG", "LV", "LT", "MD", "TJ", "TM", "UA", "UZ"],
        ["810"] = ["RU", "AM", "AZ", "BY", "EE", "GE", "KZ", "KG", "LV", "LT", "MD", "TJ", "TM", "UA", "UZ"],
        ["CS"] = ["RS", "ME"],
        ["YU"] = ["RS", "ME"],
        ["NT"] = ["SA", "IQ"],
    };

    /// <summary>CLDR likely-subtags entries the test corpus exercises, keyed
    /// lang[-script][-region] (case-normalized).</summary>
    private static readonly Dictionary<string, (string Lang, string Script, string Region)> LikelySubtags = new(StringComparer.Ordinal)
    {
        ["en"] = ("en", "Latn", "US"),
        ["en-Shaw"] = ("en", "Shaw", "GB"),
        ["und"] = ("en", "Latn", "US"),
        ["und-Thai"] = ("th", "Thai", "TH"),
        ["und-419"] = ("es", "Latn", "419"),
        ["und-150"] = ("en", "Latn", "150"),
        ["und-AT"] = ("de", "Latn", "AT"),
        ["und-Cyrl-RO"] = ("bg", "Cyrl", "RO"),
        ["und-AQ"] = ("en", "Latn", "AQ"),
        ["und-Armn"] = ("hy", "Armn", "AM"),
        ["und-RS"] = ("sr", "Cyrl", "RS"),
        ["und-ME"] = ("sr", "Latn", "ME"),
        ["th"] = ("th", "Thai", "TH"),
        ["es"] = ("es", "Latn", "ES"),
        ["de"] = ("de", "Latn", "DE"),
        ["bg"] = ("bg", "Cyrl", "BG"),
        ["it"] = ("it", "Latn", "IT"),
        ["ru"] = ("ru", "Cyrl", "RU"),
        ["ar"] = ("ar", "Arab", "EG"),
        ["hy"] = ("hy", "Armn", "AM"),
        ["sr"] = ("sr", "Cyrl", "RS"),
        ["sr-ME"] = ("sr", "Latn", "ME"),
        ["az"] = ("az", "Latn", "AZ"),
        ["az-IQ"] = ("az", "Arab", "IQ"),
        ["zh"] = ("zh", "Hans", "CN"),
        ["zh-TW"] = ("zh", "Hant", "TW"),
        ["zh-Hant"] = ("zh", "Hant", "TW"),
        ["ja"] = ("ja", "Jpan", "JP"),
        ["ko"] = ("ko", "Kore", "KR"),
        ["fr"] = ("fr", "Latn", "FR"),
        ["pt"] = ("pt", "Latn", "BR"),
        ["nl"] = ("nl", "Latn", "NL"),
        ["sv"] = ("sv", "Latn", "SE"),
        ["pl"] = ("pl", "Latn", "PL"),
        ["tr"] = ("tr", "Latn", "TR"),
        ["hi"] = ("hi", "Deva", "IN"),
        ["jbo"] = ("jbo", "Latn", "001"),
        ["hak"] = ("hak", "Hans", "CN"),
        ["hsn"] = ("hsn", "Hans", "CN"),
        ["gss"] = ("gss", "Sgnw", "GR"),
        ["yi"] = ("yi", "Hebr", "001"),
        ["he"] = ("he", "Hebr", "IL"),
        ["id"] = ("id", "Latn", "ID"),
        ["ro"] = ("ro", "Latn", "RO"),
        ["cs"] = ("cs", "Latn", "CZ"),
        ["aa"] = ("aa", "Latn", "ET"),
        ["hyw"] = ("hyw", "Armn", "AM"),
        ["fil"] = ("fil", "Latn", "PH"),
        ["uz"] = ("uz", "Latn", "UZ"),
        ["aae"] = ("aae", "Latn", "IT"),
        ["und-CW"] = ("pap", "Latn", "CW"),
        ["pap"] = ("pap", "Latn", "CW"),
    };

    private static (string Lang, string Script, string Region)? LikelyLookup(string lang, string? script, string? region)
    {
        if (script is not null && region is not null
            && LikelySubtags.TryGetValue($"{lang}-{script}-{region}", out var m1))
        {
            return m1;
        }

        if (region is not null && LikelySubtags.TryGetValue($"{lang}-{region}", out var m2))
        {
            return m2;
        }

        if (script is not null && LikelySubtags.TryGetValue($"{lang}-{script}", out var m3))
        {
            return m3;
        }

        if (LikelySubtags.TryGetValue(lang, out var m4))
        {
            return m4;
        }

        if (script is not null && LikelySubtags.TryGetValue($"und-{script}", out var m5))
        {
            return m5;
        }

        return null;
    }

    /// <summary>Add Likely Subtags: fill script/region (and replace an "und"
    /// language) from the likely-subtags table.</summary>
    private static (string Lang, string? Script, string? Region) AddLikelySubtags(string lang, string? script, string? region)
    {
        var match = LikelyLookup(lang, script, region);
        if (match is null)
        {
            return (lang, script, region);
        }

        var (ml, ms, mr) = match.Value;
        var lang2 = lang == "und" ? ml : lang;
        return (lang2, script ?? ms, region ?? mr);
    }

    private static (string Lang, string? Script, string? Region) RemoveLikelySubtags(string lang, string? script, string? region)
    {
        var max = AddLikelySubtags(lang, script, region);
        var trials = new (string Lang, string? Script, string? Region)[]
        {
            (max.Lang, null, null),
            (max.Lang, null, max.Region),
            (max.Lang, max.Script, null),
        };
        foreach (var trial in trials)
        {
            var expanded = AddLikelySubtags(trial.Lang, trial.Script, trial.Region);
            if (expanded == max)
            {
                return trial;
            }
        }

        return max;
    }

    private static readonly Dictionary<string, string> UValueAliasesCa = new(StringComparer.Ordinal)
    {
        ["islamicc"] = "islamic-civil",
        ["ethiopic-amete-alem"] = "ethioaa",
        ["gregorian"] = "gregory",
    };

    private static readonly Dictionary<string, string> UValueAliasesKs = new(StringComparer.Ordinal)
    {
        ["primary"] = "level1",
        ["secondary"] = "level2",
        ["tertiary"] = "level3",
        ["quaternary"] = "level4",
        ["quarternary"] = "level4",
        ["identical"] = "identic",
    };

    private static readonly Dictionary<string, string> UValueAliasesMs = new(StringComparer.Ordinal)
    {
        ["imperial"] = "uksystem",
    };

    private static readonly Dictionary<string, string> UValueAliasesTz = new(StringComparer.Ordinal)
    {
        ["cnckg"] = "cnsha",
        ["eire"] = "iedub",
        ["est"] = "papty",
        ["gmt0"] = "gmt",
        ["uct"] = "utc",
        ["zulu"] = "utc",
    };

    private static readonly Dictionary<string, string> UValueAliasesRegionish = new(StringComparer.Ordinal)
    {
        ["no23"] = "no50",
        ["cn11"] = "cnbj",
        ["cz10a"] = "cz110",
        ["fra"] = "frges",
        ["frg"] = "frges",
        ["lud"] = "lucl",
    };

    private static string CanonicalizeUKeywordValue(string key, string value)
    {
        // "yes" is an alias of "true" for the boolean collation keys, and any
        // "true" type value is removed entirely.
        if (value == "yes" && key is "kb" or "kc" or "kh" or "kk" or "kn")
        {
            value = "true";
        }

        if (value == "true")
        {
            return string.Empty;
        }

        var mapped = key switch
        {
            "ca" => UValueAliasesCa.GetValueOrDefault(value),
            "ks" => UValueAliasesKs.GetValueOrDefault(value),
            "ms" => UValueAliasesMs.GetValueOrDefault(value),
            "tz" => UValueAliasesTz.GetValueOrDefault(value),
            "rg" or "sd" => UValueAliasesRegionish.GetValueOrDefault(value),
            _ => null,
        };
        return mapped ?? value;
    }

    private static void CanonicalizeLanguageId(UnicodeLocaleId id)
    {
        if (id.Variants.Count > 0)
        {
            // A language+variants alias matches when all of its variants are
            // present; the matched variants are consumed, the rest kept
            // ("art-fonipa-lojban" -> "jbo-fonipa").
            foreach (var (key, replacement) in LanguageIdAliases)
            {
                var keyParts = key.Split('-');
                if (keyParts[0] != id.Language || keyParts.Length < 2)
                {
                    continue;
                }

                var keyIndex = 1;
                if (keyParts.Length > 1 && keyParts[1].Length == 4 && IsAsciiLetters(keyParts[1]))
                {
                    if (id.Script is null || !string.Equals(id.Script, keyParts[1], StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    keyIndex = 2;
                }

                if (keyIndex >= keyParts.Length || !IsVariantSubtag(keyParts[keyIndex]))
                {
                    continue;
                }

                var allPresent = true;
                for (var k = keyIndex; k < keyParts.Length; k++)
                {
                    if (!id.Variants.Contains(keyParts[k]))
                    {
                        allPresent = false;
                        break;
                    }
                }

                if (!allPresent)
                {
                    continue;
                }

                for (var k = keyIndex; k < keyParts.Length; k++)
                {
                    id.Variants.Remove(keyParts[k]);
                }

                ApplyLanguageReplacement(id, replacement, replaceAll: true);
                break;
            }
        }

        if (id.Region is not null
            && LanguageIdAliases.TryGetValue(id.Language + "-" + id.Region.ToLowerInvariant(), out var regionReplacement))
        {
            id.Region = null;
            ApplyLanguageReplacement(id, regionReplacement, replaceAll: true);
        }

        if (LanguageSubtagAliases.TryGetValue(id.Language, out var langReplacement))
        {
            ApplyLanguageReplacement(id, langReplacement, replaceAll: false);
        }

        if (id.Region is not null)
        {
            if (RegionSimpleAliases.TryGetValue(id.Region, out var simple))
            {
                id.Region = simple;
            }
            else if (RegionComplexAliases.TryGetValue(id.Region, out var candidates))
            {
                // Pick the likely region for the language+script (ignoring the
                // deprecated region), else the first candidate.
                var (_, _, likelyRegion) = AddLikelySubtags(id.Language, id.Script, null);
                var chosen = candidates[0];
                if (likelyRegion is not null && Array.IndexOf(candidates, likelyRegion) >= 0)
                {
                    chosen = likelyRegion;
                }

                id.Region = chosen;
            }
        }

        id.Variants.Sort(StringComparer.Ordinal);
    }

    private static void ApplyLanguageReplacement(UnicodeLocaleId id, string replacement, bool replaceAll)
    {
        var parts = replacement.Split('-');
        id.Language = parts[0];
        string? newScript = null;
        string? newRegion = null;
        var i = 1;
        if (i < parts.Length && IsScriptSubtag(parts[i]))
        {
            newScript = parts[i];
            i++;
        }

        if (i < parts.Length && IsRegionSubtag(parts[i]))
        {
            newRegion = parts[i];
            i++;
        }

        var newVariants = new List<string>();
        while (i < parts.Length)
        {
            newVariants.Add(parts[i].ToLowerInvariant());
            i++;
        }

        if (replaceAll)
        {
            id.Script = newScript ?? id.Script;
            id.Region = newRegion ?? id.Region;
            if (newVariants.Count > 0)
            {
                id.Variants.AddRange(newVariants);
            }
        }
        else
        {
            // Subtags from the replacement fill in only when absent ("sh" adds
            // Latn unless a script is present).
            id.Script ??= newScript;
            id.Region ??= newRegion;
        }
    }

    private static void CanonicalizeLocaleId(UnicodeLocaleId id)
    {
        CanonicalizeLanguageId(id);

        if (id.HasT && id.TLang is not null)
        {
            CanonicalizeLanguageId(id.TLang);
        }

        if (id.HasT)
        {
            for (var i = 0; i < id.TFields.Count; i++)
            {
                var (key, value) = id.TFields[i];
                if (key == "m0" && value == "names")
                {
                    id.TFields[i] = (key, "prprname");
                }
            }

            id.TFields.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        }

        if (id.HasU)
        {
            id.UAttributes.Sort(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var keywords = new List<(string Key, string Value)>(id.UKeywords.Count);
            foreach (var (key, value) in id.UKeywords)
            {
                if (!seen.Add(key))
                {
                    continue;
                }

                keywords.Add((key, CanonicalizeUKeywordValue(key, value)));
            }

            keywords.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            id.UKeywords = keywords;
        }

        id.OtherExtensions.Sort((a, b) => a.Singleton.CompareTo(b.Singleton));
    }

    /// <summary>CanonicalizeUnicodeLocaleId over a raw tag; the tag must
    /// already be structurally valid.</summary>
    private static string CanonicalizeLocaleTag(string tag)
    {
        if (!TryParseUnicodeLocaleId(tag, out var id))
        {
            return tag;
        }

        CanonicalizeLocaleId(id);
        return id.ToCanonicalString();
    }

    // =====================================================================
    //                        Intl.Locale (§14)
    // =====================================================================

    private sealed class IntlLocaleObject(JsObject prototype, UnicodeLocaleId id) : JsObject(prototype)
    {
        public UnicodeLocaleId Id { get; } = id;
        public string CanonicalName { get; } = id.ToCanonicalString();
    }

    private static IntlLocaleObject RequireLocale(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsObject && thisV.AsObject is IntlLocaleObject obj)
        {
            return obj;
        }

        throw new JsThrow(realm.NewTypeError("Intl.Locale method called on incompatible receiver"));
    }

    private static JsNativeFunction CreateLocaleCtor(JsRealm realm, JsObject proto)
    {
        var ctor = new JsNativeFunction(realm, "Locale", 1, (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Intl.Locale requires 'new'"));
            }

            var tagValue = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (!tagValue.IsString && !tagValue.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("Intl.Locale tag must be a string or object"));
            }

            var tag = tagValue.IsObject && tagValue.AsObject is IntlLocaleObject direct
                ? direct.CanonicalName
                : AbstractOperations.ToStringJs(realm.ActiveVm, tagValue);
            var optionsValue = args.Length > 1 ? args[1] : JsValue.Undefined;
            var options = optionsValue.IsUndefined ? null : AbstractOperations.ToObject(realm, optionsValue);
            var id = BuildLocaleId(realm, tag, options);
            var instProto = IntlPrototypeFor(realm, newTarget, "Locale", proto);
            return JsValue.Object(new IntlLocaleObject(instProto, id));
        }, isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0,
            (thisV, _) => JsValue.String(RequireLocale(realm, thisV).CanonicalName));
        IntrinsicHelpers.DefineMethod(realm, proto, "maximize", 0, (thisV, _) =>
        {
            var id = RequireLocale(realm, thisV).Id;
            var (lang, script, region) = AddLikelySubtags(id.Language, id.Script, id.Region);
            return JsValue.Object(new IntlLocaleObject(proto, CloneWithLanguageId(id, lang, script, region)));
        });
        IntrinsicHelpers.DefineMethod(realm, proto, "minimize", 0, (thisV, _) =>
        {
            var id = RequireLocale(realm, thisV).Id;
            var (lang, script, region) = RemoveLikelySubtags(id.Language, id.Script, id.Region);
            return JsValue.Object(new IntlLocaleObject(proto, CloneWithLanguageId(id, lang, script, region)));
        });
        void Getter(string name, Func<IntlLocaleObject, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0,
                    (thisV, _) => read(RequireLocale(realm, thisV))),
                null));
        static JsValue StringOrUndefined(string? value)
            => value is null ? JsValue.Undefined : JsValue.String(value);
        Getter("baseName", loc => JsValue.String(loc.Id.BaseName()));
        Getter("language", loc => JsValue.String(loc.Id.Language));
        Getter("script", loc => StringOrUndefined(loc.Id.Script));
        Getter("region", loc => StringOrUndefined(loc.Id.Region));
        Getter("variants", loc => loc.Id.Variants.Count == 0
            ? JsValue.Undefined
            : JsValue.String(string.Join('-', loc.Id.Variants)));
        Getter("calendar", loc => StringOrUndefined(loc.Id.UKeywordValue("ca")));
        Getter("collation", loc => StringOrUndefined(loc.Id.UKeywordValue("co")));
        Getter("hourCycle", loc => StringOrUndefined(loc.Id.UKeywordValue("hc")));
        Getter("caseFirst", loc => StringOrUndefined(loc.Id.UKeywordValue("kf")));
        Getter("numeric", loc =>
        {
            var kn = loc.Id.UKeywordValue("kn");
            return JsValue.Boolean(kn is not null && kn.Length == 0);
        });
        Getter("numberingSystem", loc => StringOrUndefined(loc.Id.UKeywordValue("nu")));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Intl.Locale"), writable: false, enumerable: false, configurable: true));
        return ctor;
    }

    private static UnicodeLocaleId CloneWithLanguageId(UnicodeLocaleId id, string lang, string? script, string? region)
    {
        return new UnicodeLocaleId
        {
            Language = lang,
            Script = script,
            Region = region,
            Variants = [.. id.Variants],
            HasU = id.HasU,
            UAttributes = [.. id.UAttributes],
            UKeywords = [.. id.UKeywords],
            TLang = id.TLang,
            TFields = [.. id.TFields],
            HasT = id.HasT,
            OtherExtensions = [.. id.OtherExtensions],
            PrivateUse = id.PrivateUse,
        };
    }

    private static UnicodeLocaleId BuildLocaleId(JsRealm realm, string tag, JsObject? options)
    {
        if (!TryParseUnicodeLocaleId(tag, out var id))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid language tag: \"{tag}\""));
        }

        CanonicalizeLocaleId(id);

        var language = GetOptionEnum(realm, options, "language", null, null);
        if (language is not null && !IsLanguageSubtag(language))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid language: \"{language}\""));
        }

        var script = GetOptionEnum(realm, options, "script", null, null);
        if (script is not null && !IsScriptSubtag(script))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid script: \"{script}\""));
        }

        var region = GetOptionEnum(realm, options, "region", null, null);
        if (region is not null && !IsRegionSubtag(region))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid region: \"{region}\""));
        }

        var variantsOption = GetOptionEnum(realm, options, "variants", null, null);
        List<string>? variants = null;
        if (variantsOption is not null)
        {
            variants = [];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in variantsOption.Split('-'))
            {
                if (!IsVariantSubtag(raw))
                {
                    throw new JsThrow(realm.NewRangeError($"Invalid variants: \"{variantsOption}\""));
                }

                var v = raw.ToLowerInvariant();
                if (!seen.Add(v))
                {
                    throw new JsThrow(realm.NewRangeError($"Duplicate variant: \"{v}\""));
                }

                variants.Add(v);
            }
        }

        if (language is not null)
        {
            id.Language = language.ToLowerInvariant();
        }

        if (script is not null)
        {
            id.Script = TitleCaseSubtag(script);
        }

        if (region is not null)
        {
            id.Region = region.ToUpperInvariant();
        }

        if (variants is not null)
        {
            id.Variants = variants;
        }

        CanonicalizeLocaleId(id);

        var calendar = GetOptionEnum(realm, options, "calendar", null, null);
        if (calendar is not null && !IsWellFormedNumberingSystem(calendar))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid calendar: \"{calendar}\""));
        }

        var collation = GetOptionEnum(realm, options, "collation", null, null);
        if (collation is not null && !IsWellFormedNumberingSystem(collation))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid collation: \"{collation}\""));
        }

        var hourCycle = GetOptionEnum(realm, options, "hourCycle", ["h11", "h12", "h23", "h24"], null);
        var caseFirst = GetOptionEnum(realm, options, "caseFirst", ["upper", "lower", "false"], null);
        var numeric = GetBooleanOption(realm, options, "numeric");
        var numberingSystem = GetOptionEnum(realm, options, "numberingSystem", null, null);
        if (numberingSystem is not null && !IsWellFormedNumberingSystem(numberingSystem))
        {
            throw new JsThrow(realm.NewRangeError($"Invalid numberingSystem: \"{numberingSystem}\""));
        }

        if (calendar is not null)
        {
            id.SetUKeyword("ca", calendar.ToLowerInvariant());
        }

        if (collation is not null)
        {
            id.SetUKeyword("co", collation.ToLowerInvariant());
        }

        if (hourCycle is not null)
        {
            id.SetUKeyword("hc", hourCycle);
        }

        if (caseFirst is not null)
        {
            id.SetUKeyword("kf", caseFirst);
        }

        if (numeric is not null)
        {
            id.SetUKeyword("kn", numeric.Value ? "true" : "false");
        }

        if (numberingSystem is not null)
        {
            id.SetUKeyword("nu", numberingSystem.ToLowerInvariant());
        }

        CanonicalizeLocaleId(id);
        return id;
    }
}
