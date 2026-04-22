using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Parsers
{
    /// <summary>
    /// Parser CSV/TSV per listini prezzi con auto-detect di delimiter ed encoding,
    /// heuristic column mapping case-insensitive e gestione robusta di quoted fields
    /// (inclusi newline interni e double-quote escape).
    /// Usa solo BCL, compatibile netstandard2.0.
    /// </summary>
    public class CsvParser : IPriceListParser
    {
        // Estensioni gestite (controllo case-insensitive).
        private static readonly string[] SupportedExtensions = { ".csv", ".tsv", ".txt" };

        // Candidati delimiter per auto-detect (ordine di preferenza a parità di conteggio).
        private static readonly char[] DelimiterCandidates = { ';', '\t', ',' };

        /// <inheritdoc />
        public bool CanHandle(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return false;
            return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public PriceListImportResult Parse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath non può essere vuoto", nameof(filePath));

            var sourceName = Path.GetFileNameWithoutExtension(filePath);
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Parse(stream, sourceName);
            }
        }

        /// <inheritdoc />
        public PriceListImportResult Parse(Stream stream, string sourceName)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (sourceName == null) sourceName = string.Empty;

            var result = new PriceListImportResult
            {
                Metadata =
                {
                    Name = sourceName,
                    ImportedAt = DateTime.UtcNow
                }
            };

            // Buffering in memoria: permette auto-detect encoding/delimiter con re-read senza Seek ambiguo.
            byte[] buffer;
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                buffer = ms.ToArray();
            }

            if (buffer.Length == 0)
            {
                result.Warnings.Add("File vuoto.");
                result.Metadata.Source = "CSV";
                return result;
            }

            // 1) Encoding: usa BOM detection; fallback Windows-1252 se UTF-8 genera caratteri invalidi.
            var encoding = DetectEncoding(buffer);

            // 2) Prima riga per delimiter detection.
            string firstLine;
            using (var reader = CreateReader(buffer, encoding))
            {
                firstLine = ReadFirstNonEmptyLine(reader) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(firstLine))
            {
                result.Warnings.Add("Nessuna riga header rilevata.");
                result.Metadata.Source = "CSV";
                return result;
            }

            var delimiter = DetectDelimiter(firstLine);
            result.Metadata.Source = delimiter == '\t' ? "TSV" : "CSV";

            // 3) Parse completo via state machine.
            List<List<string>> rows;
            using (var reader = CreateReader(buffer, encoding))
            {
                rows = ParseCsvRows(reader, delimiter).ToList();
            }

            if (rows.Count == 0)
            {
                result.Warnings.Add("Nessuna riga utile nel file.");
                return result;
            }

            var header = rows[0];
            // Mapping euristico centralizzato in ColumnMapping (condiviso con ExcelParser).
            var map = ColumnMapping.FromHeader(header);

            // Validazione colonne obbligatorie: interrompe con warning, items vuoto.
            if (map.Code < 0)
            {
                result.Warnings.Add("Colonna obbligatoria 'Code' non trovata in header");
                return result;
            }
            if (map.Description < 0)
            {
                result.Warnings.Add("Colonna obbligatoria 'Description' non trovata in header");
                return result;
            }
            if (map.Price < 0)
            {
                result.Warnings.Add("Colonna obbligatoria 'Price' non trovata in header");
                return result;
            }

            var headerCols = header.Count;

            // 4) Loop righe dati.
            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];

                // Skip righe completamente vuote (tutti campi whitespace).
                if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
                    continue;

                result.TotalRowsDetected++;

                // Padding se colonne mancanti.
                if (row.Count < headerCols)
                {
                    result.Warnings.Add($"Riga {i + 1}: {row.Count} colonne, attese {headerCols}");
                    while (row.Count < headerCols) row.Add(string.Empty);
                }

                var code = ParsingHelpers.SafeTrim(GetCell(row, map.Code));
                if (string.IsNullOrEmpty(code))
                {
                    result.Warnings.Add($"Riga {i + 1}: Code vuoto, riga scartata");
                    continue;
                }

                var item = new PriceItem
                {
                    Code = code,
                    Description = ParsingHelpers.SafeTrim(GetCell(row, map.Description)),
                    ShortDesc = ParsingHelpers.SafeTrim(GetCell(row, map.ShortDesc)),
                    Unit = ParsingHelpers.SafeTrim(GetCell(row, map.Unit))
                };

                var rawPrice = GetCell(row, map.Price);
                if (!ParsingHelpers.TryParseDecimal(rawPrice, out var price))
                {
                    result.Warnings.Add($"Riga {i + 1}: prezzo '{rawPrice}' non parsabile, UnitPrice=0");
                    item.UnitPrice = 0;
                }
                else
                {
                    item.UnitPrice = price;
                }

                // Chapter/SuperChapter/SubChapter: da colonna se presente, altrimenti derivati dal Code.
                item.SuperChapter = map.SuperChapter >= 0
                    ? ParsingHelpers.SafeTrim(GetCell(row, map.SuperChapter))
                    : ParsingHelpers.DeriveSuperChapter(code);

                item.Chapter = map.Chapter >= 0
                    ? ParsingHelpers.SafeTrim(GetCell(row, map.Chapter))
                    : ParsingHelpers.DeriveChapter(code);

                item.SubChapter = map.SubChapter >= 0
                    ? ParsingHelpers.SafeTrim(GetCell(row, map.SubChapter))
                    : string.Empty;

                result.Items.Add(item);
            }

            result.Metadata.RowCount = result.Items.Count;
            return result;
        }

        /// <summary>
        /// Parser CSV state-machine che emette righe (liste di celle) rispettando
        /// quoted fields, double-quote escape e newline interni ai campi quoted.
        /// </summary>
        public static IEnumerable<List<string>> ParseCsvRows(TextReader reader, char delimiter)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var field = new StringBuilder();
            var row = new List<string>();
            var state = CsvState.InField;
            var rowHasContent = false;

            int ch;
            while ((ch = reader.Read()) != -1)
            {
                var c = (char)ch;

                switch (state)
                {
                    case CsvState.InField:
                        if (c == '"' && field.Length == 0)
                        {
                            // Apertura quote solo se il campo è ancora vuoto.
                            state = CsvState.InQuotedField;
                            rowHasContent = true;
                        }
                        else if (c == delimiter)
                        {
                            row.Add(field.ToString());
                            field.Clear();
                            rowHasContent = true;
                        }
                        else if (c == '\r')
                        {
                            // Swallow CR in coppia CRLF; la LF successiva chiude la riga.
                            var peek = reader.Peek();
                            if (peek == '\n') reader.Read();
                            row.Add(field.ToString());
                            field.Clear();
                            if (rowHasContent || row.Count > 1 || !string.IsNullOrEmpty(row[0]))
                                yield return row;
                            row = new List<string>();
                            rowHasContent = false;
                        }
                        else if (c == '\n')
                        {
                            row.Add(field.ToString());
                            field.Clear();
                            if (rowHasContent || row.Count > 1 || !string.IsNullOrEmpty(row[0]))
                                yield return row;
                            row = new List<string>();
                            rowHasContent = false;
                        }
                        else
                        {
                            field.Append(c);
                            rowHasContent = true;
                        }
                        break;

                    case CsvState.InQuotedField:
                        if (c == '"')
                        {
                            // Peek per distinguere double-quote escape da chiusura.
                            var peek = reader.Peek();
                            if (peek == '"')
                            {
                                reader.Read();
                                field.Append('"');
                            }
                            else
                            {
                                state = CsvState.AfterClosingQuote;
                            }
                        }
                        else
                        {
                            // Newline interni preservati letteralmente.
                            field.Append(c);
                        }
                        break;

                    case CsvState.AfterClosingQuote:
                        if (c == delimiter)
                        {
                            row.Add(field.ToString());
                            field.Clear();
                            state = CsvState.InField;
                        }
                        else if (c == '\r')
                        {
                            var peek = reader.Peek();
                            if (peek == '\n') reader.Read();
                            row.Add(field.ToString());
                            field.Clear();
                            yield return row;
                            row = new List<string>();
                            rowHasContent = false;
                            state = CsvState.InField;
                        }
                        else if (c == '\n')
                        {
                            row.Add(field.ToString());
                            field.Clear();
                            yield return row;
                            row = new List<string>();
                            rowHasContent = false;
                            state = CsvState.InField;
                        }
                        else if (c == '"')
                        {
                            // Quote isolato dopo chiusura: tolleriamo e rientriamo in quoted.
                            field.Append('"');
                            state = CsvState.InQuotedField;
                        }
                        // Altri caratteri tra quote e delimiter vengono ignorati (whitespace in genere).
                        break;
                }
            }

            // EOF: flush dell'ultima riga se non vuota.
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                if (rowHasContent || row.Count > 1 || !string.IsNullOrEmpty(row[0]))
                    yield return row;
            }
        }

        // --- Helpers privati --------------------------------------------------

        /// <summary>
        /// Rileva encoding: BOM UTF-8/UTF-16 → relativo; altrimenti tenta UTF-8 strict
        /// e fallback Windows-1252 (code page 1252) se rileva byte non validi UTF-8.
        /// </summary>
        private static Encoding DetectEncoding(byte[] buffer)
        {
            // BOM check.
            if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                return new UTF8Encoding(true);
            if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
                return Encoding.Unicode;
            if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            // Prova UTF-8 strict (throw on invalid bytes): se fallisce, Windows-1252.
            try
            {
                var strict = new UTF8Encoding(false, true);
                strict.GetString(buffer);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                // Windows-1252 è superset ASCII compatibile ISO-8859-1 per i caratteri usuali italiani.
                try
                {
                    return Encoding.GetEncoding(1252);
                }
                catch
                {
                    // Fallback estremo: latin1 garantito in BCL.
                    return Encoding.GetEncoding("ISO-8859-1");
                }
            }
        }

        private static StreamReader CreateReader(byte[] buffer, Encoding encoding)
        {
            // detectEncodingFromByteOrderMarks=true così la BOM viene consumata correttamente.
            var ms = new MemoryStream(buffer, writable: false);
            return new StreamReader(ms, encoding, detectEncodingFromByteOrderMarks: true);
        }

        private static string? ReadFirstNonEmptyLine(StreamReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line)) return line;
            }
            return null;
        }

        /// <summary>
        /// Sceglie il delimiter più frequente nella riga fra i candidati; default ';'.
        /// Ignora occorrenze dentro quoted fields (tracking semplice).
        /// </summary>
        private static char DetectDelimiter(string firstLine)
        {
            var counts = new Dictionary<char, int>();
            foreach (var d in DelimiterCandidates) counts[d] = 0;

            var inQuotes = false;
            foreach (var c in firstLine)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (inQuotes) continue;
                if (counts.ContainsKey(c)) counts[c]++;
            }

            var best = ';';
            var bestCount = 0;
            foreach (var d in DelimiterCandidates)
            {
                if (counts[d] > bestCount)
                {
                    bestCount = counts[d];
                    best = d;
                }
            }
            return bestCount > 0 ? best : ';';
        }

        private static string GetCell(List<string> row, int index)
        {
            if (index < 0 || index >= row.Count) return string.Empty;
            return row[index] ?? string.Empty;
        }

        private enum CsvState
        {
            InField,
            InQuotedField,
            AfterClosingQuote
        }
    }
}
