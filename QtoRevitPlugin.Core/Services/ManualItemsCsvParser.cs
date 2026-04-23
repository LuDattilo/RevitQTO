using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Parser CSV per import/export di <see cref="ManualQuantityEntry"/> (§I13).
    ///
    /// <para><b>Formato atteso</b> (separatore <c>;</c>, encoding UTF-8, header obbligatorio):</para>
    /// <code>
    /// EpCode;Description;Quantity;Unit;UnitPrice;Notes
    /// OS.001;Oneri sicurezza;1,00;cad;1200,00;Verbale 12/03
    /// TR.042;Trasporto discarica;5,00;m3;85,00;DDT n.142
    /// </code>
    ///
    /// <para><b>Convenzioni</b>:</para>
    /// <list type="bullet">
    ///   <item>Separatore campo: <c>;</c> (convenzione italiana Excel)</item>
    ///   <item>Separatore decimale: <c>,</c> (cultura it-IT) oppure <c>.</c> (invariante); il parser accetta entrambi</item>
    ///   <item>Header obbligatorio e case-insensitive; l'ordine delle colonne è libero</item>
    ///   <item>Campi opzionali: <c>Description</c>, <c>Unit</c>, <c>UnitPrice</c>, <c>Notes</c></item>
    ///   <item>Campi obbligatori: <c>EpCode</c>, <c>Quantity</c></item>
    ///   <item>Righe vuote o commenti (che iniziano con <c>#</c>) vengono ignorate</item>
    ///   <item>Campi con <c>;</c> o <c>"</c> possono essere racchiusi in doppie virgolette (standard CSV)</item>
    /// </list>
    ///
    /// <para>Parser puro (statico): nessuno stato, nessun I/O, testabile in isolation.</para>
    /// </summary>
    public static class ManualItemsCsvParser
    {
        private const char Separator = ';';

        private static readonly string[] KnownHeaders =
        {
            "EpCode", "Description", "Quantity", "Unit", "UnitPrice", "Notes"
        };

        /// <summary>
        /// Risultato del parsing: voci importate + eventuali errori per riga.
        /// </summary>
        public sealed class ParseResult
        {
            public List<ManualQuantityEntry> Entries { get; } = new List<ManualQuantityEntry>();
            public List<string> Errors { get; } = new List<string>();
            public int TotalLines { get; set; }
            public bool HasErrors => Errors.Count > 0;
        }

        /// <summary>
        /// Parsing del contenuto CSV (stringa intera). Ritorna sempre un <see cref="ParseResult"/>
        /// (non throw su righe malformate — colleziona errori e procede).
        /// </summary>
        public static ParseResult Parse(string csvContent, int sessionId, string createdBy = "")
        {
            var result = new ParseResult();
            if (string.IsNullOrWhiteSpace(csvContent)) return result;

            var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            result.TotalLines = lines.Length;

            // 1. Trova la header line (prima riga non vuota non commento)
            int headerIdx = -1;
            string[]? header = null;
            for (int i = 0; i < lines.Length; i++)
            {
                var trim = lines[i].Trim();
                if (string.IsNullOrEmpty(trim)) continue;
                if (trim.StartsWith("#")) continue;
                headerIdx = i;
                header = SplitCsvLine(trim);
                break;
            }

            if (header == null || headerIdx < 0)
            {
                result.Errors.Add("File CSV vuoto o senza header valido.");
                return result;
            }

            // 2. Valida header + mappa indici
            var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                var name = header[i].Trim();
                if (!string.IsNullOrEmpty(name)) colIndex[name] = i;
            }
            if (!colIndex.ContainsKey("EpCode"))
            {
                result.Errors.Add("Header mancante: colonna 'EpCode' obbligatoria.");
                return result;
            }
            if (!colIndex.ContainsKey("Quantity"))
            {
                result.Errors.Add("Header mancante: colonna 'Quantity' obbligatoria.");
                return result;
            }

            // 3. Parsing righe
            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                var raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw.TrimStart().StartsWith("#")) continue;

                var fields = SplitCsvLine(raw);

                try
                {
                    var entry = new ManualQuantityEntry
                    {
                        SessionId = sessionId,
                        EpCode = GetField(fields, colIndex, "EpCode").Trim(),
                        EpDescription = GetField(fields, colIndex, "Description").Trim(),
                        Unit = GetField(fields, colIndex, "Unit").Trim(),
                        Notes = GetField(fields, colIndex, "Notes").Trim(),
                        CreatedBy = createdBy,
                        CreatedAt = DateTime.UtcNow
                    };

                    if (string.IsNullOrWhiteSpace(entry.EpCode))
                    {
                        result.Errors.Add($"Riga {i + 1}: EpCode vuoto — skipped.");
                        continue;
                    }

                    entry.Quantity = ParseNumber(GetField(fields, colIndex, "Quantity"));
                    entry.UnitPrice = ParseNumber(GetField(fields, colIndex, "UnitPrice"), defaultValue: 0);

                    result.Entries.Add(entry);
                }
                catch (FormatException ex)
                {
                    result.Errors.Add($"Riga {i + 1}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Parsing da file. Usa UTF-8 di default; se il file è BOM-less ASCII/Latin1
        /// con caratteri italiani, riprova con Encoding.Default.
        /// </summary>
        public static ParseResult ParseFile(string csvFilePath, int sessionId, string createdBy = "")
        {
            if (string.IsNullOrWhiteSpace(csvFilePath))
                throw new ArgumentException("Path CSV non valido.", nameof(csvFilePath));
            if (!File.Exists(csvFilePath))
                throw new FileNotFoundException("File CSV non trovato.", csvFilePath);

            // Leggi con detect BOM / fallback Latin1 se caratteri unicode strani
            string content;
            try
            {
                content = File.ReadAllText(csvFilePath, Encoding.UTF8);
            }
            catch (DecoderFallbackException)
            {
                content = File.ReadAllText(csvFilePath, Encoding.GetEncoding("ISO-8859-1"));
            }

            return Parse(content, sessionId, createdBy);
        }

        /// <summary>
        /// Esporta le voci manuali in formato CSV (stesso layout di import).
        /// Usa cultura invariante per i numeri (il punto come separatore decimale)
        /// per evitare ambiguità cross-locale.
        /// </summary>
        public static string Export(IEnumerable<ManualQuantityEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(Separator.ToString(), KnownHeaders));

            foreach (var e in entries)
            {
                sb.Append(EscapeField(e.EpCode)).Append(Separator)
                  .Append(EscapeField(e.EpDescription)).Append(Separator)
                  .Append(e.Quantity.ToString(CultureInfo.InvariantCulture)).Append(Separator)
                  .Append(EscapeField(e.Unit)).Append(Separator)
                  .Append(e.UnitPrice.ToString(CultureInfo.InvariantCulture)).Append(Separator)
                  .Append(EscapeField(e.Notes))
                  .AppendLine();
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------

        private static string GetField(string[] fields, Dictionary<string, int> index, string name)
        {
            if (!index.TryGetValue(name, out var idx)) return string.Empty;
            if (idx >= fields.Length) return string.Empty;
            return fields[idx] ?? string.Empty;
        }

        /// <summary>
        /// Parsing numero con fallback locale: prova prima invariant (punto decimale),
        /// poi italiana (virgola). Throw <see cref="FormatException"/> se entrambi falliscono.
        /// </summary>
        private static double ParseNumber(string raw, double? defaultValue = null)
        {
            var s = raw?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(s))
            {
                if (defaultValue.HasValue) return defaultValue.Value;
                throw new FormatException("Valore numerico richiesto, trovato vuoto.");
            }

            // Rimuovi separatori delle migliaia possibili (non ambigui: se c'è sia punto che virgola
            // si assume italiano con punto migliaia + virgola decimale)
            if (s.Contains(".") && s.Contains(","))
            {
                s = s.Replace(".", "");
                s = s.Replace(",", ".");
            }
            else if (s.Contains(","))
            {
                // Solo virgola: interpreta come decimale italiano
                s = s.Replace(",", ".");
            }

            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                return val;

            throw new FormatException($"Valore numerico non valido: '{raw}'.");
        }

        /// <summary>
        /// Split CSV line con supporto per campi quoted (es. <c>"Via Roma; 10"</c>).
        /// Non implementa tutto lo standard RFC 4180 ma copre i casi frequenti.
        /// </summary>
        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    // Escape: due doppi apici consecutivi = un apice
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == Separator && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }

        private static string EscapeField(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // Quota se contiene separatore, virgolette, o newline
            if (value!.Contains(Separator.ToString()) || value.Contains("\"") ||
                value.Contains("\n") || value.Contains("\r"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }
    }
}
