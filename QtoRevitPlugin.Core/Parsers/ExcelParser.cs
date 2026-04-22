using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Parsers
{
    /// <summary>
    /// Parser Excel per listini prezzi (.xlsx/.xlsm). ClosedXML NON supporta .xls binario legacy:
    /// per .xls il parser emette un warning chiaro e ritorna items vuoti.
    /// Il column mapping riusa la stessa euristica case-insensitive di CsvParser (identica logica),
    /// applicata alla prima riga non vuota del primo foglio con almeno 2 righe non vuote.
    /// </summary>
    public class ExcelParser : IPriceListParser
    {
        // Estensioni gestite (controllo case-insensitive).
        private static readonly string[] SupportedExtensions = { ".xlsx", ".xlsm", ".xls" };

        // Safety-net su fogli con range used giganti (es. corruption o formule col-wide vuote).
        private const int MaxRowsHardLimit = 100_000;

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
                var result = Parse(stream, sourceName);
                // Se estensione .xls o .xlsm, correggi metadata.Source in modo coerente.
                var ext = (Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();
                if (ext == ".xls") result.Metadata.Source = "XLS";
                else if (ext == ".xlsm") result.Metadata.Source = "XLSM";
                else result.Metadata.Source = "XLSX";
                return result;
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
                    Source = "XLSX",
                    ImportedAt = DateTime.UtcNow
                }
            };

            // Se il caller ha fornito hint sull'estensione nel sourceName, emetti warning legacy.
            // Heuristica: sourceName può contenere ".xls" finale (non comune), ma il check primario
            // per .xls avviene in Parse(string). Qui copriamo i casi di stream già aperto da .xls.
            if (sourceName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add(
                    "Formato .xls legacy non supportato da ClosedXML: converti in .xlsx per risultati migliori.");
                return result;
            }

            // 1) Buffer in memoria (ClosedXML richiede uno stream seekable; se già MemoryStream evitiamo copia).
            Stream workbookStream = stream;
            MemoryStream? ownedMs = null;
            try
            {
                if (!stream.CanSeek)
                {
                    ownedMs = new MemoryStream();
                    stream.CopyTo(ownedMs);
                    ownedMs.Position = 0;
                    workbookStream = ownedMs;
                }

                // 2) Apri workbook — è il punto più fragile: corruzione file / formato non xlsx.
                XLWorkbook workbook;
                try
                {
                    workbook = new XLWorkbook(workbookStream);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"File non valido o corrotto: {ex.Message}");
                    return result;
                }

                using (workbook)
                {
                    // 3) Seleziona il foglio attivo: primo foglio con >= 2 righe non vuote.
                    var candidates = new List<IXLWorksheet>();
                    foreach (var sheet in workbook.Worksheets)
                    {
                        if (CountNonEmptyRows(sheet, limit: 2) >= 2)
                            candidates.Add(sheet);
                    }

                    if (candidates.Count == 0)
                    {
                        result.Warnings.Add("Nessun foglio contiene almeno 2 righe non vuote.");
                        return result;
                    }

                    var worksheet = candidates[0];
                    if (candidates.Count > 1)
                    {
                        result.Warnings.Add(
                            $"Trovati {candidates.Count} fogli validi, usato '{worksheet.Name}' — specificare manualmente con override se errato.");
                    }

                    // 4) Determina range e trova header row (prima riga non vuota del foglio attivo).
                    var used = worksheet.RangeUsed();
                    if (used == null)
                    {
                        result.Warnings.Add($"Foglio '{worksheet.Name}' vuoto.");
                        return result;
                    }

                    var lastRowNumber = used.LastRow().RowNumber();
                    var lastColNumber = used.LastColumn().ColumnNumber();

                    if (lastRowNumber > MaxRowsHardLimit)
                    {
                        result.Warnings.Add(
                            $"Limite sicurezza raggiunto: troncato a {MaxRowsHardLimit} righe (rilevate {lastRowNumber}).");
                        lastRowNumber = MaxRowsHardLimit;
                    }

                    int headerRowNumber = -1;
                    for (int r = 1; r <= lastRowNumber; r++)
                    {
                        if (!IsRowEmpty(worksheet, r, lastColNumber))
                        {
                            headerRowNumber = r;
                            break;
                        }
                    }

                    if (headerRowNumber < 0)
                    {
                        result.Warnings.Add("Nessuna riga header rilevata nel foglio attivo.");
                        return result;
                    }

                    // 5) Estrai header e costruisci column map via helper condiviso.
                    var headerCells = new List<string>();
                    for (int c = 1; c <= lastColNumber; c++)
                    {
                        headerCells.Add(GetCellString(worksheet, headerRowNumber, c));
                    }

                    var map = ColumnMapping.FromHeader(headerCells);

                    // Validazione colonne obbligatorie — stesso contratto CsvParser.
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

                    // 6) Loop righe dati.
                    for (int r = headerRowNumber + 1; r <= lastRowNumber; r++)
                    {
                        if (IsRowEmpty(worksheet, r, lastColNumber))
                            continue;

                        result.TotalRowsDetected++;

                        var code = ParsingHelpers.SafeTrim(GetCellString(worksheet, r, map.Code + 1));
                        if (string.IsNullOrEmpty(code))
                        {
                            result.Warnings.Add($"Riga {r}: Code vuoto, riga scartata");
                            continue;
                        }

                        var item = new PriceItem
                        {
                            Code = code,
                            Description = ParsingHelpers.SafeTrim(GetCellString(worksheet, r, map.Description + 1)),
                            ShortDesc = map.ShortDesc >= 0
                                ? ParsingHelpers.SafeTrim(GetCellString(worksheet, r, map.ShortDesc + 1))
                                : string.Empty,
                            Unit = map.Unit >= 0
                                ? ParsingHelpers.SafeTrim(GetCellString(worksheet, r, map.Unit + 1))
                                : string.Empty
                        };

                        var rawPrice = GetCellString(worksheet, r, map.Price + 1);
                        if (!ParsingHelpers.TryParseDecimal(rawPrice, out var price))
                        {
                            result.Warnings.Add($"Riga {r}: prezzo '{rawPrice}' non parsabile, UnitPrice=0");
                            item.UnitPrice = 0;
                        }
                        else
                        {
                            item.UnitPrice = price;
                        }

                        item.SuperChapter = map.SuperChapter >= 0
                            ? ParsingHelpers.SafeTrim(GetCellString(worksheet, r, map.SuperChapter + 1))
                            : ParsingHelpers.DeriveSuperChapter(code);

                        item.Chapter = map.Chapter >= 0
                            ? ParsingHelpers.SafeTrim(GetCellString(worksheet, r, map.Chapter + 1))
                            : ParsingHelpers.DeriveChapter(code);

                        item.SubChapter = map.SubChapter >= 0
                            ? ParsingHelpers.SafeTrim(GetCellString(worksheet, r, map.SubChapter + 1))
                            : string.Empty;

                        result.Items.Add(item);
                    }

                    result.Metadata.RowCount = result.Items.Count;
                    return result;
                }
            }
            finally
            {
                ownedMs?.Dispose();
            }
        }

        // --- Helpers privati --------------------------------------------------

        /// <summary>
        /// Conta righe non vuote in un foglio, limitato a <paramref name="limit"/> per efficienza
        /// (usato per scegliere il primo foglio con >= 2 righe). Stop anticipato.
        /// </summary>
        private static int CountNonEmptyRows(IXLWorksheet sheet, int limit)
        {
            var used = sheet.RangeUsed();
            if (used == null) return 0;

            var lastRow = used.LastRow().RowNumber();
            var lastCol = used.LastColumn().ColumnNumber();

            int count = 0;
            for (int r = 1; r <= lastRow && count < limit; r++)
            {
                if (!IsRowEmpty(sheet, r, lastCol))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Riga vuota se tutte le celle da 1 a <paramref name="lastCol"/> sono whitespace o vuote.
        /// </summary>
        private static bool IsRowEmpty(IXLWorksheet sheet, int rowNumber, int lastCol)
        {
            for (int c = 1; c <= lastCol; c++)
            {
                var s = GetCellString(sheet, rowNumber, c);
                if (!string.IsNullOrWhiteSpace(s)) return false;
            }
            return true;
        }

        /// <summary>
        /// Estrae la rappresentazione stringa della cella (gestisce formule, date, percentuali, numeri).
        /// ClosedXML's GetString() ritorna la formatted string quando presente; per i numeri usa
        /// GetFormattedString() con InvariantCulture fallback per evitare locale del processo.
        /// </summary>
        private static string GetCellString(IXLWorksheet sheet, int row, int col)
        {
            if (row < 1 || col < 1) return string.Empty;
            try
            {
                var cell = sheet.Cell(row, col);
                if (cell == null || cell.IsEmpty()) return string.Empty;

                // Per celle numeriche restituiamo una stringa InvariantCulture, così TryParseDecimal
                // la gestisce coerentemente indipendentemente dal formato visualizzazione (es. "€ 12,50").
                if (cell.DataType == XLDataType.Number)
                {
                    try
                    {
                        var d = cell.GetDouble();
                        return d.ToString("G17", CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        // Fallback: rappresentazione stringa raw.
                    }
                }

                // GetString restituisce il testo cella per Text/Boolean/DateTime/errori; più robusto di Value.ToString().
                return cell.GetString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Helper statico condiviso per il mapping euristico header → colonne PriceItem.
    /// Estratto per essere riusato da CsvParser/ExcelParser e futuri parser tabellari.
    /// Match case-insensitive: prima match esatto (ordine priorità), poi "contains".
    /// </summary>
    public static class ColumnMapping
    {
        /// <summary>
        /// Costruisce un <see cref="ColumnMap"/> da una lista di header (stringhe così come lette dal file).
        /// Indici -1 per colonne non trovate.
        /// </summary>
        public static ColumnMap FromHeader(IReadOnlyList<string> headers)
        {
            if (headers == null) throw new ArgumentNullException(nameof(headers));

            var normalized = new List<string>(headers.Count);
            for (int i = 0; i < headers.Count; i++)
                normalized.Add(NormalizeHeader(headers[i]));

            var map = new ColumnMap
            {
                Code = FindColumn(normalized,
                    exact: new[] { "codice", "code" },
                    contains: new[] { "codice", "code", "cod" }),

                Description = FindColumn(normalized,
                    exact: new[] { "descrizione completa", "descrizione", "description" },
                    contains: new[] { "descrizione completa", "descrizione", "description", "desc" },
                    exclude: new[] { "breve", "short", "abbreviaz" }),

                ShortDesc = FindColumn(normalized,
                    exact: new[] { "descrizione breve", "descr.breve", "shortdesc", "abbreviazione" },
                    contains: new[] { "descrizione breve", "descr.breve", "descr breve", "shortdesc", "short desc", "abbreviaz" }),

                Unit = FindColumn(normalized,
                    exact: new[] { "u.m.", "um", "unit", "unità", "unita", "unità di misura", "unita di misura" },
                    contains: new[] { "unità di misura", "unita di misura", "u.m.", "unità", "unita", "unit", "um" }),

                Price = FindColumn(normalized,
                    exact: new[] { "prezzo unitario", "prezzo", "price", "importo" },
                    contains: new[] { "prezzo unitario", "prezzo", "price", "importo" }),

                Chapter = FindColumn(normalized,
                    exact: new[] { "capitolo", "chapter" },
                    contains: new[] { "capitolo", "chapter" },
                    exclude: new[] { "super", "sotto", "sub" }),

                SuperChapter = FindColumn(normalized,
                    exact: new[] { "super capitolo", "supercapitolo", "categoria" },
                    contains: new[] { "super capitolo", "supercapitolo", "categoria" }),

                SubChapter = FindColumn(normalized,
                    exact: new[] { "sottocapitolo", "sotto capitolo", "subcapitolo" },
                    contains: new[] { "sottocapitolo", "sotto capitolo", "subcapitolo", "sub capitolo" })
            };

            return map;
        }

        private static string NormalizeHeader(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw!.Trim().ToLower(CultureInfo.InvariantCulture);
        }

        private static int FindColumn(
            List<string> normalizedHeaders,
            string[] exact,
            string[] contains,
            string[]? exclude = null)
        {
            // Pass 1: match esatto per priorità.
            foreach (var key in exact)
            {
                for (int i = 0; i < normalizedHeaders.Count; i++)
                {
                    var h = normalizedHeaders[i];
                    if (h == key && !IsExcluded(h, exclude))
                        return i;
                }
            }

            // Pass 2: contains per priorità.
            foreach (var key in contains)
            {
                for (int i = 0; i < normalizedHeaders.Count; i++)
                {
                    var h = normalizedHeaders[i];
                    if (h.Contains(key) && !IsExcluded(h, exclude))
                        return i;
                }
            }

            return -1;
        }

        private static bool IsExcluded(string header, string[]? exclude)
        {
            if (exclude == null) return false;
            foreach (var token in exclude)
                if (header.Contains(token)) return true;
            return false;
        }
    }

    /// <summary>
    /// Indici colonna (-1 se non mappata) per ogni proprietà <see cref="PriceItem"/> riconosciuta.
    /// </summary>
    public class ColumnMap
    {
        public int Code { get; set; } = -1;
        public int Description { get; set; } = -1;
        public int ShortDesc { get; set; } = -1;
        public int Unit { get; set; } = -1;
        public int Price { get; set; } = -1;
        public int Chapter { get; set; } = -1;
        public int SuperChapter { get; set; } = -1;
        public int SubChapter { get; set; } = -1;
    }
}
