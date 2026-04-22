using System;
using System.Globalization;

namespace QtoRevitPlugin.Parsers
{
    /// <summary>
    /// Utility di parsing riutilizzate da tutti i parser (DCF, CSV, Excel).
    /// </summary>
    internal static class ParsingHelpers
    {
        /// <summary>
        /// Parse robusto di numeri decimali da stringa con formato italiano/internazionale misto.
        /// Strategia: rimuove separatore migliaia, sostituisce virgola decimale con punto,
        /// parse InvariantCulture. Restituisce 0 in caso di fallimento (con flag).
        /// </summary>
        public static bool TryParseDecimal(string? raw, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            // Dopo IsNullOrWhiteSpace il compiler non deduce non-null: null-forgiving ok.
            var normalized = raw!.Trim();

            // Rimuove separatore migliaia italiano (punto) SOLO se c'è anche una virgola decimale:
            // "1.234,56" → "1234,56" → "1234.56"
            // Ma "12.50" (punto decimale internazionale) NON va toccato.
            if (normalized.Contains(",") && normalized.Contains("."))
                normalized = normalized.Replace(".", "");
            normalized = normalized.Replace(",", ".");

            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Deriva SuperChapter da un codice gerarchico tipo "A.01.001" → "A" (prima parte prima del primo separatore).</summary>
        public static string DeriveSuperChapter(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var idx = code.IndexOfAny(new[] { '.', '-', '_' });
            return idx > 0 ? code.Substring(0, idx) : code;
        }

        /// <summary>Deriva Chapter da codice "A.01.001" → "A.01" (primi due token). Vuoto se il codice non ha almeno 2 token.</summary>
        public static string DeriveChapter(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var tokens = code.Split(new[] { '.', '-', '_' }, StringSplitOptions.None);
            if (tokens.Length < 2) return string.Empty;
            return tokens[0] + "." + tokens[1];
        }

        /// <summary>Trim robusto che gestisce anche null.</summary>
        public static string SafeTrim(string? s) => s?.Trim() ?? string.Empty;
    }
}
