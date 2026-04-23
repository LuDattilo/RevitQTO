using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Esporta il computo in formato XPWE (XML PriMus-WEB) conforme al dialetto
    /// ACCA PriMus, validato contro file reale <c>CME_Sample.xpwe</c>.
    ///
    /// <para><b>Struttura</b> (file PriMus v5.04):</para>
    /// <list type="bullet">
    ///   <item>Processing instruction <c>&lt;?mso-application progid="PriMus.Document.XPWE"?&gt;</c></item>
    ///   <item>Root <c>&lt;PweDocumento&gt;</c> (NO namespace xmlns)</item>
    ///   <item>Header: CopyRight, TipoDocumento=1 (Computo), TipoFormato=XMLPwe,
    ///     Versione=5.04, SourceVersione, SourceNome, FileNameDocumento</item>
    ///   <item><c>&lt;PweDatiGenerali&gt;</c>:
    ///     <list type="bullet">
    ///       <item>PweDGProgetto/PweDGDatiGenerali con Comune, Provincia, Oggetto, Committente, Impresa, ParteOpera, PercPrezzi</item>
    ///       <item>PweDGCapitoliCategorie con 6 tabelle flat:
    ///         <b>PweDGSuperCapitoli</b>/PweDGCapitoli/PweDGSubCapitoli (assi listini)
    ///         + <b>PweDGSuperCategorie</b>/PweDGCategorie/PweDGSubCategorie (assi categorie computo)</item>
    ///       <item>PweDGWBS/PweDGWBSCAP (disattivate)</item>
    ///       <item>PweDGModuli/PweDGAnalisi (SpeseUtili, SpeseGenerali, UtiliImpresa, ConfQuantita)</item>
    ///       <item>PweDGConfigurazione/PweDGConfigNumeri (formattazione numeri: Divisa, fattori conversione, precisioni)</item>
    ///     </list>
    ///   </item>
    ///   <item><c>&lt;PweMisurazioni&gt;</c>:
    ///     <list type="bullet">
    ///       <item>PweElencoPrezzi → EPItem con TipoEP, Tariffa, Articolo, DesRidotta, DesEstesa,
    ///         UnMisura, Prezzo1..5, IDSpCap FK, IDCap FK, IDSbCap FK, IncSIC, IncMDO, IncMAT, IncATTR, TagBIM, PweEPAnalisi</item>
    ///       <item>PweVociComputo → VCItem con IDEP FK, Quantita, DataMis, IDSpCat FK, IDCat FK, IDSbCat FK
    ///         + PweVCMisure/RGItem (riga misurazione: Descrizione, PartiUguali, Lunghezza, Larghezza, HPeso, Quantita)</item>
    ///     </list>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Mapping dal nostro data model</b>:</para>
    /// <list type="bullet">
    ///   <item>I nostri <c>ComputoChapter</c> (3 livelli Super/Cat/Sub) sono mappati
    ///     sulle <b>Categorie PriMus</b> (SuperCategorie/Categorie/SubCategorie) — non sui Capitoli.
    ///     Questo rispecchia la semantica reale: le nostre "Demolizioni / Strutturali / SOLAI"
    ///     sono tagging analitici, non chapters di listino.</item>
    ///   <item>Un singolo <c>&lt;DGSuperCapitoliItem&gt;</c> "placeholder" viene inserito per il computo
    ///     con descrizione = titolo progetto (PriMus lo usa come "documento sorgente prezzi").</item>
    ///   <item>Ogni <c>ReportEntry</c> genera un <c>&lt;EPItem&gt;</c> + un <c>&lt;VCItem&gt;</c>.
    ///     PartiUguali = Quantity, Lunghezza/Larghezza/HPeso vuoti.</item>
    /// </list>
    /// </summary>
    public class XpweExporter : IReportExporter
    {
        // Costanti header ACCA (valori da CME_Sample.xpwe)
        private const string CopyRight = "Copyright ACCA software S.p.A.";
        private const string TipoFormato = "XMLPwe";
        private const string Versione = "5.04";
        private const string TipoDocumentoComputo = "1";  // 1 = Computo (osservato)
        private const string SourceNome = "RevitQTO-CME";
        private const string SourceVersione = "RevitQTO 1.0";

        // Placeholder "data vuota" usato da PriMus per campi DateTime non valorizzati
        private const string EmptyDate = "30/12/1899";

        // Processing instruction richiesta da PriMus per aprire il file con associazione
        private const string PiTarget = "mso-application";
        private const string PiData = "progid=\"PriMus.Document.XPWE\"";

        public string FormatName => "XPWE";
        public string FileExtension => ".xpwe";
        public string FileFilter => "PriMus XPWE (*.xpwe;*.xml)|*.xpwe;*.xml|Tutti i file (*.*)|*.*";
        public ReportExportOptions DefaultOptions => new ReportExportOptions();

        public void Export(ReportDataSet data, string outputPath, ReportExportOptions options)
        {
            // Indicizzazione chapters su asse Categorie (SuperCat/Cat/SubCat)
            var catIndex = new CategoryIndex();
            catIndex.Build(data);

            // Write su StringBuilder prima di salvare su file (così possiamo
            // controllare esattamente formattazione minima — PriMus salva tutto
            // su singola riga senza indentazione).
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false,
                OmitXmlDeclaration = true  // PriMus non emette <?xml ... ?>
            };

            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = XmlWriter.Create(stream, settings);

            // Processing instruction mso-application (richiesta da PriMus)
            writer.WriteProcessingInstruction(PiTarget, PiData);

            writer.WriteStartElement("PweDocumento");

            // Header documento
            writer.WriteElementString("CopyRight", CopyRight);
            writer.WriteElementString("TipoDocumento", TipoDocumentoComputo);
            writer.WriteElementString("TipoFormato", TipoFormato);
            writer.WriteElementString("Versione", Versione);
            writer.WriteElementString("SourceVersione", SourceVersione);
            writer.WriteElementString("SourceNome", SourceNome);
            writer.WriteElementString("FileNameDocumento", data.Session?.SessionName ?? "computo.dcf");

            WriteDatiGenerali(writer, data, catIndex);
            WriteMisurazioni(writer, data, catIndex);

            writer.WriteEndElement(); // PweDocumento
        }

        // =====================================================================
        // PweDatiGenerali
        // =====================================================================

        private static void WriteDatiGenerali(XmlWriter writer, ReportDataSet data, CategoryIndex catIndex)
        {
            writer.WriteStartElement("PweDatiGenerali");

            // Progetto (Sprint 10: arricchito con campi ProjectInfo per compatibilità PriMus)
            writer.WriteStartElement("PweDGProgetto");
            writer.WriteStartElement("PweDGDatiGenerali");
            writer.WriteElementString("PercPrezzi", data.Header.RibassoPercentuale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteElementString("Comune", data.Header.Comune);
            writer.WriteElementString("Provincia", data.Header.Provincia);
            writer.WriteElementString("Oggetto", string.IsNullOrEmpty(data.Header.Titolo) ? "" : data.Header.Titolo);
            writer.WriteElementString("Committente", data.Header.Committente);
            writer.WriteElementString("Impresa", data.Header.Impresa);
            writer.WriteStartElement("ParteOpera"); writer.WriteEndElement();
            // Campi PriMus-net extra — includiamo se valorizzati
            if (!string.IsNullOrEmpty(data.Header.RUP))
                writer.WriteElementString("RUP", data.Header.RUP);
            if (!string.IsNullOrEmpty(data.Header.DirettoreLavori))
                writer.WriteElementString("DirettoreLavori", data.Header.DirettoreLavori);
            if (!string.IsNullOrEmpty(data.Header.CIG))
                writer.WriteElementString("CIG", data.Header.CIG);
            if (!string.IsNullOrEmpty(data.Header.CUP))
                writer.WriteElementString("CUP", data.Header.CUP);
            if (!string.IsNullOrEmpty(data.Header.Luogo))
                writer.WriteElementString("Luogo", data.Header.Luogo);
            if (data.Header.DataComputo.HasValue)
                writer.WriteElementString("DataComputo", data.Header.DataComputo.Value.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture));
            if (data.Header.DataPrezzi.HasValue)
                writer.WriteElementString("DataPrezzi", data.Header.DataPrezzi.Value.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(data.Header.RiferimentoPrezzario))
                writer.WriteElementString("RiferimentoPrezzario", data.Header.RiferimentoPrezzario);
            writer.WriteEndElement(); // PweDGDatiGenerali
            writer.WriteEndElement(); // PweDGProgetto

            // Capitoli/Categorie
            writer.WriteStartElement("PweDGCapitoliCategorie");

            // SuperCapitoli: un solo item placeholder (= "documento sorgente prezzi")
            writer.WriteStartElement("PweDGSuperCapitoli");
            WriteDgItem(writer, "DGSuperCapitoliItem", id: 1,
                desSintetica: data.Header.Titolo.Length > 0 ? data.Header.Titolo : "Computo",
                codice: "", codFase: null);
            writer.WriteEndElement();

            // Capitoli e SubCapitoli vuoti (i nostri ComputoChapter vanno su Categorie)
            writer.WriteStartElement("PweDGCapitoli"); writer.WriteEndElement();
            writer.WriteStartElement("PweDGSubCapitoli"); writer.WriteEndElement();

            // SuperCategorie (nostro livello 1) — SoaCode effettivo in CodFase
            writer.WriteStartElement("PweDGSuperCategorie");
            foreach (var ic in catIndex.Super)
                WriteDgItem(writer, "DGSuperCategorieItem", ic.PwId, ic.Name, ic.Code, ic.EffectiveSoaCode);
            writer.WriteEndElement();

            // Categorie (nostro livello 2)
            writer.WriteStartElement("PweDGCategorie");
            foreach (var ic in catIndex.Cat)
                WriteDgItem(writer, "DGCategorieItem", ic.PwId, ic.Name, ic.Code, ic.EffectiveSoaCode);
            writer.WriteEndElement();

            // SubCategorie (nostro livello 3)
            writer.WriteStartElement("PweDGSubCategorie");
            foreach (var ic in catIndex.Sub)
                WriteDgItem(writer, "DGSubCategorieItem", ic.PwId, ic.Name, ic.Code, ic.EffectiveSoaCode);
            writer.WriteEndElement();

            writer.WriteEndElement(); // PweDGCapitoliCategorie

            // WBS disattivate (vuote)
            writer.WriteStartElement("PweDGWBS");
            writer.WriteElementString("DGWBSAttiva", "0");
            writer.WriteEndElement();

            writer.WriteStartElement("PweDGWBSCAP");
            writer.WriteElementString("DGWBSCAPAttiva", "0");
            writer.WriteEndElement();

            // Moduli analisi (valori osservati in CME_Sample.xpwe)
            writer.WriteStartElement("PweDGModuli");
            writer.WriteStartElement("PweDGAnalisi");
            writer.WriteElementString("SpeseUtili", "-1");
            writer.WriteElementString("SpeseGenerali", "16");
            writer.WriteElementString("UtiliImpresa", "10");
            writer.WriteElementString("OneriAccessoriSc", "0");
            writer.WriteElementString("ConfQuantita", "11.3|1");
            writer.WriteElementString("OneriSociali", "0");
            writer.WriteEndElement();
            writer.WriteEndElement();

            // Configurazione numeri (default euro + precisioni italiane)
            writer.WriteStartElement("PweDGConfigurazione");
            writer.WriteStartElement("PweDGConfigNumeri");
            writer.WriteElementString("Divisa", "euro");
            writer.WriteElementString("ConversioniIN", "lire");
            writer.WriteElementString("FattoreConversione", "1936.27");
            writer.WriteElementString("Cambio", "1");
            writer.WriteElementString("PartiUguali", "8.2|0");
            writer.WriteElementString("Lunghezza", "8.2|0");
            writer.WriteElementString("Larghezza", "9.3|0");
            writer.WriteElementString("HPeso", "9.3|0");
            writer.WriteElementString("Quantita", "10.2|1");
            writer.WriteElementString("Prezzi", "10.2|1");
            writer.WriteElementString("PrezziTotale", "14.2|1");
            writer.WriteElementString("ConvPrezzi", "11.0|1");
            writer.WriteElementString("ConvPrezziTotale", "15.0|1");
            writer.WriteElementString("IncidenzaPercentuale", "7.3|0");
            writer.WriteElementString("Aliquote", "7.3|0");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteEndElement(); // PweDatiGenerali
        }

        private static void WriteDgItem(XmlWriter writer, string itemName, int id, string desSintetica, string codice, string? codFase)
        {
            writer.WriteStartElement(itemName);
            writer.WriteAttributeString("ID", id.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("DesSintetica", desSintetica);
            writer.WriteStartElement("DesEstesa"); writer.WriteEndElement();
            writer.WriteElementString("DataInit", EmptyDate);
            writer.WriteElementString("Durata", "0");
            // CodFase = codice SOA (OG/OS) se assegnato/ereditato. Vuoto altrimenti.
            if (string.IsNullOrEmpty(codFase))
            {
                writer.WriteStartElement("CodFase"); writer.WriteEndElement();
            }
            else
            {
                writer.WriteElementString("CodFase", codFase);
            }
            writer.WriteElementString("Percentuale", "0");
            writer.WriteElementString("Codice", codice);
            writer.WriteEndElement();
        }

        // =====================================================================
        // PweMisurazioni
        // =====================================================================

        private static void WriteMisurazioni(XmlWriter writer, ReportDataSet data, CategoryIndex catIndex)
        {
            writer.WriteStartElement("PweMisurazioni");

            // ElencoPrezzi: una voce EPItem per ogni riga del report (anche duplicati EpCode
            // vengono distinti da ID crescente — PriMus tollera più EPItem con stesso Tariffa
            // se ID diverso, ma de-duplicare è cleaner).
            writer.WriteStartElement("PweElencoPrezzi");
            var epByCode = new Dictionary<string, int>(System.StringComparer.Ordinal);
            int epIdSeq = 1;
            foreach (var entry in EnumerateEntries(data))
            {
                if (epByCode.ContainsKey(entry.Entry.EpCode)) continue;
                var epId = epIdSeq++;
                epByCode[entry.Entry.EpCode] = epId;
                WriteEPItem(writer, entry.Entry, epId);
            }
            writer.WriteEndElement(); // PweElencoPrezzi

            // VociComputo: una VCItem per riga entry (più righe possono condividere stesso EP)
            writer.WriteStartElement("PweVociComputo");
            int vcIdSeq = 100;   // PriMus usa ID >= 100 per VCItem (osservato)
            int rgIdSeq = 2;     // RGItem ID sequenziale interno
            foreach (var (entry, chain) in EnumerateEntriesWithChain(data, catIndex))
            {
                var epId = epByCode[entry.EpCode];
                WriteVCItem(writer, entry, epId, vcIdSeq++, ref rgIdSeq, chain);
            }
            writer.WriteEndElement(); // PweVociComputo

            writer.WriteEndElement(); // PweMisurazioni
        }

        private static void WriteEPItem(XmlWriter writer, ReportEntry entry, int id)
        {
            writer.WriteStartElement("EPItem");
            writer.WriteAttributeString("ID", id.ToString(CultureInfo.InvariantCulture));

            writer.WriteElementString("TipoEP", "0");
            writer.WriteElementString("Tariffa", entry.EpCode);
            writer.WriteElementString("Articolo", entry.EpCode);
            writer.WriteElementString("DesRidotta", Truncate(entry.EpDescription, 200));
            writer.WriteElementString("DesEstesa", entry.EpDescription);
            writer.WriteStartElement("DesBreve"); writer.WriteEndElement();
            writer.WriteElementString("UnMisura", entry.Unit);
            writer.WriteElementString("Prezzo1", entry.UnitPrice.ToString("F5", CultureInfo.InvariantCulture));
            writer.WriteElementString("Prezzo2", "0");
            writer.WriteElementString("Prezzo3", "0");
            writer.WriteElementString("Prezzo4", "0");
            writer.WriteElementString("Prezzo5", "0");
            writer.WriteElementString("IDSpCap", "1");  // tutti puntano al placeholder
            writer.WriteElementString("IDCap", "0");
            writer.WriteElementString("IDSbCap", "0");
            writer.WriteStartElement("CodiceWBSCAP"); writer.WriteEndElement();
            writer.WriteElementString("Flags", "0");
            writer.WriteElementString("Data", EmptyDate);
            writer.WriteStartElement("AdrInternet"); writer.WriteEndElement();
            writer.WriteElementString("IncSIC", "0");
            writer.WriteElementString("IncMDO", "0.000000000000000000");
            writer.WriteElementString("IncMAT", "0.000000000000000000");
            writer.WriteElementString("IncATTR", "0.000000000000000000");
            writer.WriteStartElement("TagBIM"); writer.WriteEndElement();
            writer.WriteStartElement("PweEPAnalisi"); writer.WriteEndElement();

            writer.WriteEndElement(); // EPItem
        }

        private static void WriteVCItem(XmlWriter writer, ReportEntry entry, int epId, int vcId, ref int rgIdSeq, CategoryChain chain)
        {
            writer.WriteStartElement("VCItem");
            writer.WriteAttributeString("ID", vcId.ToString(CultureInfo.InvariantCulture));

            writer.WriteElementString("IDEP", epId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("Quantita", entry.Quantity.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("DataMis", System.DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            writer.WriteElementString("Flags", "0");

            // FK a Categorie (il nostro ComputoChapter)
            writer.WriteElementString("IDSpCat", (chain.SuperId ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("IDCat", (chain.CatId ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("IDSbCat", (chain.SubId ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteStartElement("CodiceWBS"); writer.WriteEndElement();

            // Misurazioni: una sola riga RGItem con PartiUguali = Quantity
            writer.WriteStartElement("PweVCMisure");
            writer.WriteStartElement("RGItem");
            writer.WriteAttributeString("ID", (rgIdSeq++).ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("IDVV", "-2");
            writer.WriteElementString("Descrizione", BuildMisuraNote(entry));
            writer.WriteElementString("PartiUguali", entry.Quantity.ToString("F2", CultureInfo.InvariantCulture));
            writer.WriteStartElement("Lunghezza"); writer.WriteEndElement();
            writer.WriteStartElement("Larghezza"); writer.WriteEndElement();
            writer.WriteElementString("HPeso", "1.000");
            writer.WriteElementString("Quantita", entry.Quantity.ToString("F2", CultureInfo.InvariantCulture));
            writer.WriteElementString("Flags", "0");
            writer.WriteEndElement(); // RGItem
            writer.WriteEndElement(); // PweVCMisure

            writer.WriteEndElement(); // VCItem
        }

        private static string BuildMisuraNote(ReportEntry entry)
        {
            // Include ElementId (traccia Revit) + categoria come pro-memoria nella descrizione
            if (!string.IsNullOrEmpty(entry.ElementId))
                return $"Revit ID={entry.ElementId} · {entry.Category}";
            return entry.EpDescription.Length > 100 ? entry.EpDescription.Substring(0, 100) : entry.EpDescription;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // =====================================================================
        // Enumerazione entries con category chain
        // =====================================================================

        private static IEnumerable<(ReportEntry Entry, CategoryChain Chain)> EnumerateEntries(ReportDataSet data)
        {
            foreach (var node in data.Chapters)
                foreach (var x in WalkNode(node, new CategoryChain()))
                    yield return x;
            foreach (var e in data.UnchaperedEntries)
                yield return (e, new CategoryChain());
        }

        private static IEnumerable<(ReportEntry Entry, CategoryChain Chain)> EnumerateEntriesWithChain(
            ReportDataSet data, CategoryIndex catIndex)
        {
            foreach (var node in data.Chapters)
                foreach (var x in WalkNodeMapped(node, new CategoryChain(), catIndex))
                    yield return x;
            foreach (var e in data.UnchaperedEntries)
                yield return (e, new CategoryChain());
        }

        private static IEnumerable<(ReportEntry, CategoryChain)> WalkNode(ReportChapterNode node, CategoryChain parent)
        {
            foreach (var child in node.Children)
                foreach (var x in WalkNode(child, parent))
                    yield return x;
            foreach (var e in node.Entries)
                yield return (e, parent);
        }

        private static IEnumerable<(ReportEntry, CategoryChain)> WalkNodeMapped(
            ReportChapterNode node, CategoryChain parent, CategoryIndex catIndex)
        {
            var chain = parent;
            if (catIndex.TryGetPwId(node.Chapter.Id, out var pwId, out var level))
            {
                if (level == 1) chain.SuperId = pwId;
                else if (level == 2) chain.CatId = pwId;
                else chain.SubId = pwId;
            }
            foreach (var entry in node.Entries)
                yield return (entry, chain);
            foreach (var child in node.Children)
                foreach (var x in WalkNodeMapped(child, chain, catIndex))
                    yield return x;
        }
    }

    // =========================================================================
    // Strutture di supporto
    // =========================================================================

    internal class CategoryMeta
    {
        public int PwId { get; set; }       // ID nel file PriMus (1-based)
        public int DbId { get; set; }       // ID nostro ComputoChapter
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int Level { get; set; }      // 1, 2, 3
        /// <summary>Codice SOA effettivo (proprio o ereditato dal parent). Null se non assegnato.</summary>
        public string? EffectiveSoaCode { get; set; }
    }

    internal struct CategoryChain
    {
        public int? SuperId;
        public int? CatId;
        public int? SubId;
    }

    internal class CategoryIndex
    {
        public List<CategoryMeta> Super { get; } = new List<CategoryMeta>();
        public List<CategoryMeta> Cat { get; } = new List<CategoryMeta>();
        public List<CategoryMeta> Sub { get; } = new List<CategoryMeta>();

        private readonly Dictionary<int, CategoryMeta> _byDbId = new Dictionary<int, CategoryMeta>();

        public void Build(ReportDataSet data)
        {
            int superSeq = 1, catSeq = 1, subSeq = 1;
            foreach (var root in data.Chapters)
                IndexNode(root, 1, ref superSeq, ref catSeq, ref subSeq);
        }

        private void IndexNode(ReportChapterNode node, int level,
            ref int superSeq, ref int catSeq, ref int subSeq)
        {
            var meta = new CategoryMeta
            {
                DbId = node.Chapter.Id,
                Code = node.Chapter.Code,
                Name = string.IsNullOrEmpty(node.Chapter.Name) ? node.Chapter.Code : node.Chapter.Name,
                Level = level
            };

            if (level == 1) { meta.PwId = superSeq++; Super.Add(meta); }
            else if (level == 2) { meta.PwId = catSeq++; Cat.Add(meta); }
            else { meta.PwId = subSeq++; Sub.Add(meta); }

            _byDbId[node.Chapter.Id] = meta;

            foreach (var child in node.Children)
                IndexNode(child, level + 1, ref superSeq, ref catSeq, ref subSeq);
        }

        public bool TryGetPwId(int dbId, out int pwId, out int level)
        {
            if (_byDbId.TryGetValue(dbId, out var meta))
            {
                pwId = meta.PwId;
                level = meta.Level;
                return true;
            }
            pwId = 0; level = 0;
            return false;
        }
    }
}
