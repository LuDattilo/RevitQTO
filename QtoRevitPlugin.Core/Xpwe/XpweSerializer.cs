using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Xpwe
{
    /// <summary>
    /// Plan C-7: serializza un XpweImportResult in XML formato .xpwe PriMus.
    /// Output deterministico: stesso input → stesso XML byte-per-byte.
    /// Simmetrico a <see cref="XpweDeserializer"/>.
    /// </summary>
    public class XpweSerializer
    {
        public void SaveToFile(XpweImportResult result, string path)
        {
            var xml = SaveToString(result);
            File.WriteAllText(path, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public string SaveToString(XpweImportResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var doc = Build(result);
            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = true,
                Encoding = new UTF8Encoding(false)
            }))
            {
                doc.Save(writer);
            }
            // Prepend PriMus processing instruction come fa PriMus
            return "<?mso-application progid=\"PriMus.Document.XPWE\"?>" + sb.ToString();
        }

        private static XDocument Build(XpweImportResult r)
        {
            var root = new XElement("PweDocumento",
                new XElement("CopyRight", "Copyright ACCA software S.p.A."),
                new XElement("TipoDocumento", r.Document.TipoDocumento.ToString(CultureInfo.InvariantCulture)),
                new XElement("TipoFormato", "XMLPwe"),
                new XElement("Versione", string.IsNullOrEmpty(r.Document.Versione) ? "5.04" : r.Document.Versione),
                new XElement("SourceVersione", "QtoRevitPlugin"),
                new XElement("SourceNome", "QtoRevitPlugin"),
                new XElement("Fgs", r.Document.Fgs.ToString(CultureInfo.InvariantCulture))
            );

            root.Add(BuildDatiGenerali(r));
            root.Add(BuildMisurazioni(r));

            return new XDocument(root);
        }

        private static XElement BuildDatiGenerali(XpweImportResult r)
        {
            var datiGen = new XElement("PweDatiGenerali");

            var progetto = new XElement("PweDGProgetto",
                new XElement("PweDGDatiGenerali",
                    Txt("PercPrezzi", r.Document.PercPrezzi.ToString("0.###", CultureInfo.InvariantCulture)),
                    Txt("Comune", r.Document.Comune),
                    Txt("Provincia", r.Document.Provincia),
                    Txt("Oggetto", r.Document.Oggetto),
                    Txt("Committente", r.Document.Committente),
                    Txt("Impresa", r.Document.Impresa),
                    Txt("ParteOpera", r.Document.ParteOpera)
                )
            );
            datiGen.Add(progetto);

            var classif = new XElement("PweDGCapitoliCategorie",
                BuildChapterGroup(r.SuperCapitoli, "PweDGSuperCapitoli", "DGSuperCapitoliItem"),
                BuildChapterGroup(r.Capitoli, "PweDGCapitoli", "DGCapitoliItem"),
                BuildChapterGroup(r.SubCapitoli, "PweDGSubCapitoli", "DGSubCapitoliItem"),
                BuildCategoryGroup(r.SuperCategorie, "PweDGSuperCategorie", "DGSuperCategorieItem"),
                BuildCategoryGroup(r.Categorie, "PweDGCategorie", "DGCategorieItem"),
                BuildCategoryGroup(r.SubCategorie, "PweDGSubCategorie", "DGSubCategorieItem")
            );
            datiGen.Add(classif);

            datiGen.Add(new XElement("PweDGWBSCAP"));
            datiGen.Add(new XElement("PweDGWBS"));

            return datiGen;
        }

        private static XElement BuildChapterGroup(
            List<XpweChapterItem> items,
            string containerName, string itemName)
        {
            var el = new XElement(containerName);
            foreach (var ch in items)
            {
                var node = ch.Node;
                el.Add(new XElement(itemName,
                    new XAttribute("ID", ch.XpweId.ToString(CultureInfo.InvariantCulture)),
                    Txt("Codice", node.Codice),
                    Txt("DesSintetica", node.DesSintetica),
                    Txt("DesEstesa", node.DesEstesa),
                    Txt("DataInit", node.DataInit ?? "30/12/1899"),
                    Txt("Durata", node.Durata.ToString(CultureInfo.InvariantCulture)),
                    Txt("CodFase", node.CodFase),
                    Txt("Percentuale", node.Percentuale.ToString("0.###", CultureInfo.InvariantCulture))
                ));
            }
            return el;
        }

        private static XElement BuildCategoryGroup(
            List<XpweCategoryItem> items,
            string containerName, string itemName)
        {
            var el = new XElement(containerName);
            foreach (var ch in items)
            {
                var node = ch.Node;
                el.Add(new XElement(itemName,
                    new XAttribute("ID", ch.XpweId.ToString(CultureInfo.InvariantCulture)),
                    Txt("Codice", node.Codice),
                    Txt("DesSintetica", node.DesSintetica),
                    Txt("DesEstesa", node.DesEstesa),
                    Txt("DataInit", node.DataInit ?? "30/12/1899"),
                    Txt("Durata", node.Durata.ToString(CultureInfo.InvariantCulture)),
                    Txt("CodFase", node.CodFase),
                    Txt("Percentuale", node.Percentuale.ToString("0.###", CultureInfo.InvariantCulture))
                ));
            }
            return el;
        }

        private static XElement BuildMisurazioni(XpweImportResult r)
        {
            var mis = new XElement("PweMisurazioni");

            var ep = new XElement("PweElencoPrezzi");
            foreach (var item in r.PriceItems)
            {
                var d = item.Data;
                ep.Add(new XElement("EPItem",
                    new XAttribute("ID", item.XpweId.ToString(CultureInfo.InvariantCulture)),
                    Txt("Tariffa", d.Tariffa),
                    Txt("Articolo", d.Articolo),
                    Txt("DesRidotta", d.DesRidotta),
                    Txt("DesEstesa", d.DesEstesa),
                    Txt("UnMisura", d.UnMisura),
                    Txt("Prezzo1", Fmt(d.Prezzo1)),
                    Txt("Prezzo2", Fmt(d.Prezzo2)),
                    Txt("Prezzo3", Fmt(d.Prezzo3)),
                    Txt("Prezzo4", Fmt(d.Prezzo4)),
                    Txt("Prezzo5", Fmt(d.Prezzo5)),
                    Txt("CnfQt", d.CnfQt),
                    Txt("IDSpCap", (item.IDSpCap ?? 0).ToString(CultureInfo.InvariantCulture)),
                    Txt("IDCap", (item.IDCap ?? 0).ToString(CultureInfo.InvariantCulture)),
                    Txt("IDSbCap", (item.IDSbCap ?? 0).ToString(CultureInfo.InvariantCulture)),
                    Txt("CodiceWBSCAP", item.CodiceWBSCAP),
                    Txt("Data", string.IsNullOrEmpty(d.Data) ? "30/12/1899" : d.Data),
                    Txt("DesBreve", d.DesBreve),
                    Txt("IncMDO", Fmt(d.IncMDO)),
                    Txt("IncMAT", Fmt(d.IncMAT)),
                    Txt("IncSIC", Fmt(d.IncSIC)),
                    Txt("TipoRisorsa", d.TipoRisorsa.ToString(CultureInfo.InvariantCulture)),
                    Txt("Flags", d.Flags.ToString(CultureInfo.InvariantCulture)),
                    Txt("AdrInternet", d.AdrInternet)
                ));
            }
            mis.Add(ep);

            var vc = new XElement("PweVociComputo");
            if (r.MeasurementRows.Count == 0)
            {
                vc.Add(new XElement("VCItem"));
            }
            else
            {
                foreach (var mr in r.MeasurementRows)
                {
                    var row = mr.Row;
                    var vcItem = new XElement("VCItem",
                        new XAttribute("ID", mr.XpweId.ToString(CultureInfo.InvariantCulture)),
                        Txt("IDEP", mr.IDEP.ToString(CultureInfo.InvariantCulture)),
                        Txt("Quantita", Fmt(row.Quantita)),
                        Txt("DataMis", row.DataMis ?? "30/12/1899"),
                        Txt("Flags", row.Flags.ToString(CultureInfo.InvariantCulture)),
                        Txt("IDSpCat", (row.SpCatId ?? 0).ToString(CultureInfo.InvariantCulture)),
                        Txt("IDCat", (row.CatId ?? 0).ToString(CultureInfo.InvariantCulture)),
                        Txt("IDSbCat", (row.SbCatId ?? 0).ToString(CultureInfo.InvariantCulture)),
                        Txt("CodiceWBS", "")
                    );

                    if (mr.SubRows.Count > 0)
                    {
                        var misure = new XElement("PweVCMisure");
                        foreach (var sub in mr.SubRows)
                        {
                            var s = sub.SubRow;
                            misure.Add(new XElement("RGItem",
                                new XAttribute("ID", sub.XpweId.ToString(CultureInfo.InvariantCulture)),
                                Txt("IDVV", s.IDVV.ToString(CultureInfo.InvariantCulture)),
                                Txt("Descrizione", s.Descrizione),
                                Txt("PartiUguali", Fmt(s.PartiUguali)),
                                Txt("Lunghezza", s.Lunghezza.HasValue ? Fmt(s.Lunghezza.Value) : ""),
                                Txt("Larghezza", s.Larghezza.HasValue ? Fmt(s.Larghezza.Value) : ""),
                                Txt("HPeso", s.HPeso.HasValue ? Fmt(s.HPeso.Value) : ""),
                                Txt("Quantita", Fmt(s.Quantita)),
                                Txt("Flags", s.Flags.ToString(CultureInfo.InvariantCulture))
                            ));
                        }
                        vcItem.Add(misure);
                    }
                    vc.Add(vcItem);
                }
            }
            mis.Add(vc);

            return mis;
        }

        /// <summary>Crea un elemento con testo, o vuoto se null/empty.</summary>
        private static XElement Txt(string name, string? value) =>
            new XElement(name, value ?? "");

        /// <summary>Formatta double con '.' come separatore e cultura invariante, senza trailing zeros.</summary>
        private static string Fmt(double d) =>
            d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
