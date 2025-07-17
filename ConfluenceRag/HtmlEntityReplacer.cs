namespace ConfluenceRag;

public static partial class HtmlEntityReplacer
{
    public static string ReplaceHtmlEntitiesWithUtf8(string text)
    {
        // Only preserve the five standard XML entities
        var replacements = new Dictionary<string, string>
        {
            {"&amp;", "&amp;"}, // preserve
            {"&lt;", "&lt;"},   // preserve
            {"&gt;", "&gt;"},   // preserve
            {"&quot;", "&quot;"}, // preserve
            {"&apos;", "&apos;"}, // preserve

            // All others: replace with Unicode or remove
            {"&rsquo;", "'"}, {"&lsquo;", "'"}, {"&ldquo;", "\""}, {"&rdquo;", "\""},
            {"&mdash;", "—"}, {"&ndash;", "–"}, {"&hellip;", "…"},
            {"&nbsp;", " "}, {"\u00A0", " "},
            
            // Accented characters
            { "&eacute;", "é"}, {"&egrave;", "è"}, {"&ecirc;", "ê"}, {"&euml;", "ë"},
            {"&aacute;", "á"}, {"&agrave;", "à"}, {"&acirc;", "â"}, {"&atilde;", "ã"}, {"&auml;", "ä"},
            {"&iacute;", "í"}, {"&igrave;", "ì"}, {"&icirc;", "î"}, {"&iuml;", "ï"},
            {"&oacute;", "ó"}, {"&ograve;", "ò"}, {"&ocirc;", "ô"}, {"&otilde;", "õ"}, {"&ouml;", "ö"},
            {"&uacute;", "ú"}, {"&ugrave;", "ù"}, {"&ucirc;", "û"}, {"&uuml;", "ü"},
            {"&ccedil;", "ç"}, {"&ntilde;", "ñ"}, {"&yuml;", "ÿ"},
            {"&oelig;", "œ"}, {"&aelig;", "æ"}, {"&szlig;", "ß"},
            {"&aring;", "å"}, {"&oslash;", "ø"}, {"&eth;", "ð"}, {"&thorn;", "þ"},
            {"&shy;", ""}, // soft hyphen

            // Superscripts and fractions
            {"&sup1;", "¹"}, {"&sup2;", "²"}, {"&sup3;", "³"},
            {"&frac12;", "½"}, {"&frac14;", "¼"}, {"&frac34;", "¾"},

            // Math and symbols
            {"&deg;", "°"}, {"&micro;", "µ"}, {"&middot;", "·"}, {"&times;", "×"}, {"&divide;", "÷"},
            {"&plusmn;", "±"}, {"&le;", "≤"}, {"&ge;", "≥"}, {"&ne;", "≠"}, {"&infin;", "∞"},

            // Greek letters (common ones)
            {"&alpha;", "α"}, {"&beta;", "β"}, {"&gamma;", "γ"}, {"&Delta;", "Δ"}, {"&delta;", "δ"},
            {"&lambda;", "λ"}, {"&pi;", "π"}, {"&sigma;", "σ"}, {"&Omega;", "Ω"}
        };
        foreach (var kvp in replacements)
        {
            // Only replace if not one of the five XML entities
            if (kvp.Key == "&amp;" || kvp.Key == "&lt;" || kvp.Key == "&gt;" || kvp.Key == "&quot;" || kvp.Key == "&apos;")
                continue;
            text = text.Replace(kvp.Key, kvp.Value);
        }

        // Fallback: handle numeric and unknown entities, but preserve the five XML entities
        text = MyRegex().Replace(text, static match =>
        {
            string entity = match.Groups[1].Value;
            string full = match.Value;
            // Preserve the five XML entities
            if (full == "&amp;" || full == "&lt;" || full == "&gt;" || full == "&quot;" || full == "&apos;")
                return full;
            if (entity.StartsWith("#x") || entity.StartsWith("#X"))
            {
                // Hexadecimal numeric entity
                if (int.TryParse(entity.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out int code))
                    return char.ConvertFromUtf32(code);
            }
            else if (entity.StartsWith("#"))
            {
                // Decimal numeric entity
                if (int.TryParse(entity.Substring(1), out int code))
                    return char.ConvertFromUtf32(code);
            }
            // Unknown named entity: remove
            return string.Empty;
        });
        return text;
    }

    [System.Text.RegularExpressions.GeneratedRegex("&(#(x)?[0-9a-fA-F]+|[a-zA-Z]+);")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}