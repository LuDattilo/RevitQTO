using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Xpwe
{
    /// <summary>
    /// Legge un file .xpwe (formato PriMus) e costruisce un XpweImportResult.
    /// Tollerante: attributi/elementi mancanti producono Warnings, non eccezioni
    /// (tranne la mancanza del root &lt;PweDocumento&gt; che è bloccante).
    /// Convenzioni:
    /// - Decimali con '.' (CultureInfo.InvariantCulture)
    /// - Date "DD/MM/YYYY"; "30/12/1899" (Excel zero) = null
    /// - ID=0 negli elementi di riferimento = "nessun riferimento" → null
    /// </summary>
    public class XpweDeserializer
    {
        public XpweImportResult ParseFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var doc = XDocument.Load(path);
            return Parse(doc);
        }

        public XpweImportResult ParseString(string xml)
        {
            var doc = XDocument.Parse(xml);
            return Parse(doc);
        }

        private XpweImportResult Parse(XDocument doc)
        {
            var root = doc.Root;
            if (root == null || root.Name.LocalName != "PweDocumento")
                throw new InvalidDataException("Root element non trovato o non è <PweDocumento>");

            var result = new XpweImportResult();

            // Header
            result.Document.TipoDocumento = ToInt(Elem(root, "TipoDocumento"), 0);
            result.Document.Versione = Elem(root, "Versione") ?? "5.04";
            result.Document.Fgs = ToLong(Elem(root, "Fgs"), 2147614720L);
            result.Document.Currency = "EUR";
            result.Document.CreatedAt = DateTime.UtcNow;
            result.Document.UpdatedAt = DateTime.UtcNow;

            // DatiGenerali/Progetto
            var progetto = root.Element("PweDatiGenerali")?
                                .Element("PweDGProgetto")?
                                .Element("PweDGDatiGenerali");
            if (progetto != null)
            {
                result.Document.Comune = Elem(progetto, "Comune");
                result.Document.Provincia = Elem(progetto, "Provincia");
                result.Document.Oggetto = Elem(progetto, "Oggetto");
                result.Document.Committente = Elem(progetto, "Committente");
                result.Document.Impresa = Elem(progetto, "Impresa");
                result.Document.ParteOpera = Elem(progetto, "ParteOpera");
                result.Document.PercPrezzi = ToDouble(Elem(progetto, "PercPrezzi"));
            }

            // Classificatori
            var cc = root.Element("PweDatiGenerali")?.Element("PweDGCapitoliCategorie");
            if (cc != null)
            {
                ParseChapterList(cc.Element("PweDGSuperCapitoli"), "DGSuperCapitoliItem",
                                 "SpCap", result.SuperCapitoli, result.Warnings);
                ParseChapterList(cc.Element("PweDGCapitoli"), "DGCapitoliItem",
                                 "Cap", result.Capitoli, result.Warnings);
                ParseChapterList(cc.Element("PweDGSubCapitoli"), "DGSubCapitoliItem",
                                 "SbCap", result.SubCapitoli, result.Warnings);
                ParseCategoryList(cc.Element("PweDGSuperCategorie"), "DGSuperCategorieItem",
                                  "SpCat", result.SuperCategorie, result.Warnings);
                ParseCategoryList(cc.Element("PweDGCategorie"), "DGCategorieItem",
                                  "Cat", result.Categorie, result.Warnings);
                ParseCategoryList(cc.Element("PweDGSubCategorie"), "DGSubCategorieItem",
                                  "SbCat", result.SubCategorie, result.Warnings);
            }

            // Misurazioni
            var mis = root.Element("PweMisurazioni");
            if (mis != null)
            {
                ParsePriceItems(mis.Element("PweElencoPrezzi"), result);
                ParseMeasurements(mis.Element("PweVociComputo"), result);
            }

            return result;
        }

        private static void ParseChapterList(
            XElement? container, string itemName, string level,
            List<XpweChapterItem> outList, List<string> warnings)
        {
            if (container == null) return;
            int sortOrder = 1;
            foreach (var el in container.Elements(itemName))
            {
                int id = ToInt(el.Attribute("ID")?.Value, 0);
                if (id == 0) { warnings.Add($"{itemName} senza ID ignorato"); continue; }
                outList.Add(new XpweChapterItem
                {
                    XpweId = id,
                    Node = new ChapterNode
                    {
                        Level = level,
                        Codice = Elem(el, "Codice") ?? "",
                        DesSintetica = Elem(el, "DesSintetica") ?? "",
                        DesEstesa = Elem(el, "DesEstesa"),
                        DataInit = NormalizeDate(Elem(el, "DataInit")),
                        Durata = ToInt(Elem(el, "Durata"), 0),
                        CodFase = Elem(el, "CodFase"),
                        Percentuale = ToDouble(Elem(el, "Percentuale")),
                        SortOrder = sortOrder++,
                        IsActive = true
                    }
                });
            }
        }

        private static void ParseCategoryList(
            XElement? container, string itemName, string level,
            List<XpweCategoryItem> outList, List<string> warnings)
        {
            if (container == null) return;
            int sortOrder = 1;
            foreach (var el in container.Elements(itemName))
            {
                int id = ToInt(el.Attribute("ID")?.Value, 0);
                if (id == 0) { warnings.Add($"{itemName} senza ID ignorato"); continue; }
                outList.Add(new XpweCategoryItem
                {
                    XpweId = id,
                    Node = new CategoryNode
                    {
                        Level = level,
                        Codice = Elem(el, "Codice") ?? "",
                        DesSintetica = Elem(el, "DesSintetica") ?? "",
                        DesEstesa = Elem(el, "DesEstesa"),
                        DataInit = NormalizeDate(Elem(el, "DataInit")),
                        Durata = ToInt(Elem(el, "Durata"), 0),
                        CodFase = Elem(el, "CodFase"),
                        Percentuale = ToDouble(Elem(el, "Percentuale")),
                        SortOrder = sortOrder++,
                        IsActive = true
                    }
                });
            }
        }

        private static void ParsePriceItems(XElement? container, XpweImportResult result)
        {
            if (container == null) return;
            int sortOrder = 1;
            foreach (var el in container.Elements("EPItem"))
            {
                int id = ToInt(el.Attribute("ID")?.Value, 0);
                if (id == 0) { result.Warnings.Add("EPItem senza ID ignorato"); continue; }
                var pi = new XpwePriceItem
                {
                    XpweId = id,
                    Data = new PriceItemXpwe
                    {
                        Tariffa = Elem(el, "Tariffa") ?? "",
                        Articolo = Elem(el, "Articolo") ?? "",
                        DesRidotta = Elem(el, "DesRidotta") ?? "",
                        DesEstesa = Elem(el, "DesEstesa") ?? "",
                        UnMisura = Elem(el, "UnMisura") ?? "",
                        Prezzo1 = ToDouble(Elem(el, "Prezzo1")),
                        Prezzo2 = ToDouble(Elem(el, "Prezzo2")),
                        Prezzo3 = ToDouble(Elem(el, "Prezzo3")),
                        Prezzo4 = ToDouble(Elem(el, "Prezzo4")),
                        Prezzo5 = ToDouble(Elem(el, "Prezzo5")),
                        CnfQt = Elem(el, "CnfQt") ?? "",
                        Data = NormalizeDate(Elem(el, "Data")) ?? "",
                        DesBreve = Elem(el, "DesBreve") ?? "",
                        IncMDO = ToDouble(Elem(el, "IncMDO")),
                        IncMAT = ToDouble(Elem(el, "IncMAT")),
                        IncSIC = ToDouble(Elem(el, "IncSIC")),
                        TipoRisorsa = ToInt(Elem(el, "TipoRisorsa"), 0),
                        Flags = ToInt(Elem(el, "Flags"), 512),
                        AdrInternet = Elem(el, "AdrInternet") ?? ""
                    },
                    IDSpCap = ToNullableIntRef(Elem(el, "IDSpCap")),
                    IDCap = ToNullableIntRef(Elem(el, "IDCap")),
                    IDSbCap = ToNullableIntRef(Elem(el, "IDSbCap")),
                    CodiceWBSCAP = Elem(el, "CodiceWBSCAP")
                };
                result.PriceItems.Add(pi);
                sortOrder++;
            }
        }

        private static void ParseMeasurements(XElement? container, XpweImportResult result)
        {
            if (container == null) return;
            int sortOrder = 1;
            foreach (var vc in container.Elements("VCItem"))
            {
                int id = ToInt(vc.Attribute("ID")?.Value, 0);
                if (id == 0) continue;
                var item = new XpweMeasurementItem
                {
                    XpweId = id,
                    IDEP = ToInt(Elem(vc, "IDEP"), 0),
                    Row = new MeasurementRow
                    {
                        Quantita = ToDouble(Elem(vc, "Quantita")),
                        DataMis = NormalizeDate(Elem(vc, "DataMis")),
                        Flags = ToInt(Elem(vc, "Flags"), 0),
                        SortOrder = sortOrder++
                    }
                };
                var mis = vc.Element("PweVCMisure");
                if (mis != null)
                {
                    int subOrder = 1;
                    foreach (var rg in mis.Elements("RGItem"))
                    {
                        int rgId = ToInt(rg.Attribute("ID")?.Value, 0);
                        item.SubRows.Add(new XpweMeasurementSubItem
                        {
                            XpweId = rgId,
                            SubRow = new MeasurementSubRow
                            {
                                IDVV = ToInt(Elem(rg, "IDVV"), 0),
                                Descrizione = Elem(rg, "Descrizione"),
                                PartiUguali = ToDouble(Elem(rg, "PartiUguali"), 1.0),
                                Lunghezza = ToNullableDouble(Elem(rg, "Lunghezza")),
                                Larghezza = ToNullableDouble(Elem(rg, "Larghezza")),
                                HPeso = ToNullableDouble(Elem(rg, "HPeso")),
                                Quantita = ToDouble(Elem(rg, "Quantita")),
                                Flags = ToInt(Elem(rg, "Flags"), 0),
                                SortOrder = subOrder++
                            }
                        });
                    }
                }
                result.MeasurementRows.Add(item);
            }
        }

        // ------------- Helpers -------------

        private static string? Elem(XElement el, string name)
        {
            var v = el.Element(name)?.Value?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static int ToInt(string? s, int fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static long ToLong(string? s, long fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static double ToDouble(string? s, double fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static double? ToNullableDouble(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        }

        /// <summary>XPWE usa ID=0 come "nessun riferimento" → null.</summary>
        private static int? ToNullableIntRef(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (!int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return null;
            return v == 0 ? (int?)null : v;
        }

        /// <summary>
        /// "30/12/1899" = Excel serial zero = non valorizzato → null.
        /// Altrimenti ritorna la stringa tale e quale (formato DD/MM/YYYY).
        /// </summary>
        private static string? NormalizeDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (s.Trim() == "30/12/1899") return null;
            return s.Trim();
        }
    }
}
