using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Parsers
{
    /// <summary>
    /// Parser per listini XML (.dcf / .xpwe / .xml). Supporta tre formati:
    /// <list type="bullet">
    ///   <item>(A) <b>ACCA flat</b>: attributi SuperCapitolo/Capitolo/SottoCapitolo + CodiceVoce/DescrVoce/PrezzoUnitario/UnitaMisura sul VoceEP stesso.</item>
    ///   <item>(B) <b>ACCA gerarchico</b>: elementi SuperCapitolo &gt; Capitolo &gt; SottoCapitolo &gt; VoceEP con attributi Nome/Descrizione sugli ancestors.</item>
    ///   <item>(C) <b>EASY Toscana</b>: valori in child elements (&lt;prezzo&gt;, &lt;um&gt;) e gerarchia in
    ///     child elements CDATA &lt;livello1&gt;..&lt;livello4&gt; duplicati su ogni &lt;Articolo&gt;. Vedi
    ///     listini regionali su prezzariollpp.regione.toscana.it.</item>
    /// </list>
    /// Riconosce nomi elemento e attributi alternativi (Voce, Articolo, CodiceVoce/Codice/Code/codice, ecc.)
    /// in modo case-insensitive e ignora eventuali namespace XML (es. <c>xmlns:EASY=...</c>).
    /// </summary>
    public class DcfParser : IPriceListParser
    {
        // Nomi (case-insensitive sul LocalName) degli elementi "voce" accettati.
        private static readonly string[] VoiceElementNames = { "VoceEP", "Voce", "Articolo" };

        // Nomi elementi di gerarchia (per formato B).
        private static readonly string[] SuperChapterElementNames = { "SuperCapitolo", "SuperChapter" };
        private static readonly string[] ChapterElementNames = { "Capitolo", "Chapter" };
        private static readonly string[] SubChapterElementNames = { "SottoCapitolo", "SubCapitolo", "SubChapter" };

        // Lookup attributi O child elements (OR, case-insensitive).
        private static readonly string[] CodeAttrNames = { "CodiceVoce", "Codice", "Code" };
        private static readonly string[] DescriptionAttrNames = { "DescrVoce", "Descrizione", "Description" };
        private static readonly string[] ShortDescAttrNames = { "DescrBreve", "ShortDesc" };
        private static readonly string[] UnitAttrNames = { "UnitaMisura", "UM", "um", "Unit" };
        private static readonly string[] PriceAttrNames = { "PrezzoUnitario", "Prezzo", "prezzo", "Price" };
        private static readonly string[] ChapterFlatAttrNames = { "Capitolo", "Chapter" };
        private static readonly string[] SuperChapterFlatAttrNames = { "SuperCapitolo", "SuperChapter" };
        private static readonly string[] SubChapterFlatAttrNames = { "SottoCapitolo", "SubChapter", "SubCapitolo" };

        // Attributi per estrarre il "nome" di un elemento di gerarchia (formato B).
        private static readonly string[] ChapterNameAttrNames = { "Nome", "Name", "Titolo" };
        private static readonly string[] ChapterNameFallbackAttrNames = { "Descrizione", "Description" };

        // Formato C (EASY Toscana): child elements CDATA con gerarchia testuale per-articolo.
        private const string EasyLivello1 = "livello1";
        private const string EasyLivello2 = "livello2";
        private const string EasyLivello3 = "livello3";
        private const string EasyLivello4 = "livello4";

        /// <inheritdoc />
        public bool CanHandle(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return false;
            return ext.Equals(".dcf", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".xpwe", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".xml", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public PriceListImportResult Parse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath vuoto", nameof(filePath));

            var sourceName = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = Parse(fs, sourceName);
                // Override Source in base all'estensione reale del file.
                result.Metadata.Source = SourceFromExtension(Path.GetExtension(filePath));
                return result;
            }
        }

        /// <inheritdoc />
        public PriceListImportResult Parse(Stream stream, string sourceName)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var result = new PriceListImportResult
            {
                Metadata =
                {
                    Name = sourceName ?? string.Empty,
                    Source = "DCF",
                    ImportedAt = DateTime.UtcNow,
                }
            };

            XDocument xdoc;
            try
            {
                xdoc = XDocument.Load(stream, LoadOptions.None);
            }
            catch (XmlException ex)
            {
                result.Warnings.Add($"File XML non valido: {ex.Message}");
                return result;
            }
            catch (Exception ex)
            {
                // Errori IO generici: non bloccanti, segnaliamo come warning.
                result.Warnings.Add($"Impossibile leggere XML: {ex.Message}");
                return result;
            }

            if (xdoc.Root == null)
            {
                result.Warnings.Add("Documento XML privo di root element.");
                return result;
            }

            // Trova tutti gli elementi "voce" (filtro su LocalName case-insensitive, ignora namespace).
            var voiceElements = xdoc.Descendants()
                .Where(el => MatchesAnyLocalName(el, VoiceElementNames))
                .ToList();

            result.TotalRowsDetected = voiceElements.Count;

            int rowIndex = 0;
            foreach (var voiceEl in voiceElements)
            {
                rowIndex++;

                var code = ParsingHelpers.SafeTrim(GetValueAnyMode(voiceEl, CodeAttrNames));
                if (string.IsNullOrEmpty(code))
                {
                    result.Warnings.Add($"Riga {rowIndex}: CodiceVoce mancante, voce ignorata.");
                    continue;
                }

                var description = ParsingHelpers.SafeTrim(GetValueAnyMode(voiceEl, DescriptionAttrNames));
                if (string.IsNullOrEmpty(description))
                {
                    // Formato C (EASY Toscana): concatena livello3 + livello4 come descrizione
                    description = BuildDescriptionFromLivelli(voiceEl);
                    if (string.IsNullOrEmpty(description))
                        result.Warnings.Add($"Riga {rowIndex} ({code}): descrizione mancante.");
                }

                var shortDesc = ParsingHelpers.SafeTrim(GetValueAnyMode(voiceEl, ShortDescAttrNames));
                if (string.IsNullOrEmpty(shortDesc))
                {
                    // Formato C: prima riga di livello4 (variante tecnica più specifica)
                    var l4 = GetChildText(voiceEl, EasyLivello4);
                    if (!string.IsNullOrWhiteSpace(l4)) shortDesc = FirstLine(l4!);
                }

                var unit = ParsingHelpers.SafeTrim(GetValueAnyMode(voiceEl, UnitAttrNames));

                double unitPrice = 0;
                var priceRaw = GetValueAnyMode(voiceEl, PriceAttrNames);
                if (!string.IsNullOrWhiteSpace(priceRaw))
                {
                    if (!ParsingHelpers.TryParseDecimal(priceRaw, out unitPrice))
                    {
                        result.Warnings.Add($"Riga {rowIndex} ({code}): PrezzoUnitario non valido '{priceRaw}', impostato a 0.");
                        unitPrice = 0;
                    }
                }
                else
                {
                    result.Warnings.Add($"Riga {rowIndex} ({code}): PrezzoUnitario mancante, impostato a 0.");
                }

                // Super / Chapter / SubChapter: prima tentativo ancestors (formato B), poi flat attrs (A),
                // infine fallback derivato dal code.
                var superChapter = ResolveSuperChapter(voiceEl, code);
                var chapter = ResolveChapter(voiceEl, code);
                var subChapter = ResolveSubChapter(voiceEl);

                var item = new PriceItem
                {
                    Code = code,
                    SuperChapter = superChapter,
                    Chapter = chapter,
                    SubChapter = subChapter,
                    Description = description,
                    ShortDesc = shortDesc,
                    Unit = unit,
                    UnitPrice = unitPrice,
                    Notes = string.Empty,
                };

                result.Items.Add(item);
            }

            result.Metadata.RowCount = result.Items.Count;
            return result;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Mappa un'estensione file al valore di <see cref="PriceList.Source"/>.
        /// </summary>
        private static string SourceFromExtension(string? extension)
        {
            if (string.IsNullOrEmpty(extension)) return "DCF";
            if (extension!.Equals(".xpwe", StringComparison.OrdinalIgnoreCase)) return "XPWE";
            if (extension.Equals(".dcf", StringComparison.OrdinalIgnoreCase)) return "DCF";
            return "DCF";
        }

        /// <summary>
        /// Ritorna il valore del primo attributo trovato tra i nomi forniti, case-insensitive
        /// sul LocalName. Null se nessun attributo corrisponde.
        /// </summary>
        private static string? GetAttrAnyName(XElement element, IReadOnlyList<string> candidateNames)
        {
            foreach (var attr in element.Attributes())
            {
                var local = attr.Name.LocalName;
                for (int i = 0; i < candidateNames.Count; i++)
                {
                    if (string.Equals(local, candidateNames[i], StringComparison.OrdinalIgnoreCase))
                        return attr.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// True se il LocalName di <paramref name="element"/> corrisponde (case-insensitive)
        /// ad uno qualsiasi dei nomi candidati.
        /// </summary>
        private static bool MatchesAnyLocalName(XElement element, IReadOnlyList<string> candidateNames)
        {
            var local = element.Name.LocalName;
            for (int i = 0; i < candidateNames.Count; i++)
            {
                if (string.Equals(local, candidateNames[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Cerca il primo ancestor il cui LocalName matcha, ed estrae il nome (attributo Nome/Name/Titolo
        /// con fallback su Descrizione/Description). Null se non trovato.
        /// </summary>
        private static string? GetAncestorChapterName(XElement element, IReadOnlyList<string> ancestorElementNames)
        {
            foreach (var ancestor in element.Ancestors())
            {
                if (!MatchesAnyLocalName(ancestor, ancestorElementNames)) continue;

                var name = GetAttrAnyName(ancestor, ChapterNameAttrNames);
                if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();

                var fallback = GetAttrAnyName(ancestor, ChapterNameFallbackAttrNames);
                if (!string.IsNullOrWhiteSpace(fallback)) return fallback!.Trim();

                // Ancestor trovato ma senza attributi utili: ritorna stringa vuota così il
                // caller sa che la gerarchia c'è ma è anonima (non ricadiamo sul derive-by-code).
                return string.Empty;
            }
            return null;
        }

        private static string ResolveSuperChapter(XElement voiceEl, string code)
        {
            // Formato B: ancestor SuperCapitolo
            var ancestor = GetAncestorChapterName(voiceEl, SuperChapterElementNames);
            if (ancestor != null && !string.IsNullOrWhiteSpace(ancestor)) return ancestor;

            // Formato A: attributo flat sul voice element
            var flat = GetAttrAnyName(voiceEl, SuperChapterFlatAttrNames);
            if (!string.IsNullOrWhiteSpace(flat)) return flat!.Trim();

            // Formato C (EASY Toscana): child <livello1> CDATA, prima riga come titolo
            var livello1 = GetChildText(voiceEl, EasyLivello1);
            if (!string.IsNullOrWhiteSpace(livello1)) return FirstLine(livello1!);

            // Fallback: deriva dal code.
            return ParsingHelpers.DeriveSuperChapter(code);
        }

        private static string ResolveChapter(XElement voiceEl, string code)
        {
            var ancestor = GetAncestorChapterName(voiceEl, ChapterElementNames);
            if (ancestor != null && !string.IsNullOrWhiteSpace(ancestor)) return ancestor;

            var flat = GetAttrAnyName(voiceEl, ChapterFlatAttrNames);
            if (!string.IsNullOrWhiteSpace(flat)) return flat!.Trim();

            var livello2 = GetChildText(voiceEl, EasyLivello2);
            if (!string.IsNullOrWhiteSpace(livello2)) return FirstLine(livello2!);

            return ParsingHelpers.DeriveChapter(code);
        }

        private static string ResolveSubChapter(XElement voiceEl)
        {
            var ancestor = GetAncestorChapterName(voiceEl, SubChapterElementNames);
            if (ancestor != null && !string.IsNullOrWhiteSpace(ancestor)) return ancestor;

            var flat = GetAttrAnyName(voiceEl, SubChapterFlatAttrNames);
            if (!string.IsNullOrWhiteSpace(flat)) return flat!.Trim();

            var livello3 = GetChildText(voiceEl, EasyLivello3);
            if (!string.IsNullOrWhiteSpace(livello3)) return FirstLine(livello3!);

            // Nessun fallback derivato: il SubChapter è opzionale.
            return string.Empty;
        }

        // ---------------------------------------------------------------------
        // Formato C (EASY Toscana) — child elements + CDATA helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Ritorna il valore di un campo cercando prima tra gli attributi, poi tra i child elements
        /// (per gestire schemi misti come EASY Toscana dove prezzo/um sono child text/CDATA).
        /// </summary>
        private static string? GetValueAnyMode(XElement element, IReadOnlyList<string> candidateNames)
        {
            var attr = GetAttrAnyName(element, candidateNames);
            if (!string.IsNullOrWhiteSpace(attr)) return attr;

            var child = GetChildTextAnyName(element, candidateNames);
            if (!string.IsNullOrWhiteSpace(child)) return child;

            return null;
        }

        /// <summary>
        /// Testo (incluso CDATA) del primo child element con LocalName matching (case-insensitive).
        /// Null se nessun child corrisponde.
        /// </summary>
        private static string? GetChildTextAnyName(XElement element, IReadOnlyList<string> candidateNames)
        {
            foreach (var child in element.Elements())
            {
                var local = child.Name.LocalName;
                for (int i = 0; i < candidateNames.Count; i++)
                {
                    if (string.Equals(local, candidateNames[i], StringComparison.OrdinalIgnoreCase))
                        return child.Value;
                }
            }
            return null;
        }

        /// <summary>Shortcut: testo (CDATA incluso) del primo child con nome esatto specifico.</summary>
        private static string? GetChildText(XElement element, string childName)
        {
            foreach (var child in element.Elements())
            {
                if (string.Equals(child.Name.LocalName, childName, StringComparison.OrdinalIgnoreCase))
                    return child.Value;
            }
            return null;
        }

        /// <summary>
        /// Prima riga non-vuota di una stringa multi-linea (EASY livelloN CDATA tipicamente ha
        /// riga 1 = titolo breve, righe successive = dettagli tecnici).
        /// </summary>
        private static string FirstLine(string multiline)
        {
            if (string.IsNullOrWhiteSpace(multiline)) return string.Empty;
            var newline = multiline.IndexOfAny(new[] { '\r', '\n' });
            return (newline > 0 ? multiline.Substring(0, newline) : multiline).Trim();
        }

        /// <summary>
        /// Combina livello3 + livello4 (EASY) in una descrizione unica, separandoli con "\n\n".
        /// Se solo uno è presente usa quello. Se entrambi assenti, ritorna stringa vuota.
        /// </summary>
        private static string BuildDescriptionFromLivelli(XElement voiceEl)
        {
            var l3 = GetChildText(voiceEl, EasyLivello3);
            var l4 = GetChildText(voiceEl, EasyLivello4);

            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(l3)) parts.Add(l3!.Trim());
            if (!string.IsNullOrWhiteSpace(l4)) parts.Add(l4!.Trim());

            return parts.Count == 0 ? string.Empty : string.Join("\n\n", parts);
        }
    }
}
