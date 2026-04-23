using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Esporta il computo in formato XPWE (XML PriMus-WEB) conforme al dialetto
    /// usato da ACCA PriMus.
    ///
    /// Schema reale (da file `test.XPWE` fornito da PriMus):
    /// - Root: &lt;PweDocumento&gt; (NO namespace xmlns)
    /// - Header: CopyRight, TipoDocumento, TipoFormato="XMLPwe", Versione="5.01",
    ///   SourceVersione, SourceNome, Fgs
    /// - &lt;PweDatiGenerali&gt; con sotto-tabelle:
    ///   - PweDGProgetto/PweDGDatiGenerali/DesTarif (titolo documento)
    ///   - PweDGCapitoliCategorie con 6 tabelle flat: SuperCapitoli, Capitoli,
    ///     SubCapitoli, SuperCategorie, Categorie, SubCategorie
    /// - &lt;PweMisurazioni&gt;
    ///   - PweElencoPrezzi con EPItem: Tariffa, Articolo, DesRidotta, DesEstesa,
    ///     UnMisura, Prezzo1..Prezzo5, CnfQt, IDSpCap (FK), IDCap (FK), IDSbCap (FK),
    ///     Data, DesBreve, IncMDO, IncMAT, IncSIC, TipoRisorsa, Flags, AdrInternet
    ///   - PweVociComputo con VCItem (schema voci computo da validare con file reale)
    ///
    /// <b>Nota schema computo</b>: il file di test fornito è un listino (PweVociComputo
    /// contiene solo &lt;VCItem/&gt; vuoto). I campi di VCItem effettivo (tariffa, dimensioni,
    /// formula, quantità, capitolo FK) sono ipotizzati dallo stile ACCA e da validare
    /// contro un file XPWE computo reale — vedi <see cref="XpweExporterLegacy"/> per la
    /// versione annidata non conforme, mantenuta solo a fini storici.
    /// </summary>
    public class XpweExporter : IReportExporter
    {
        // Costanti header ACCA
        private const string CopyRight = "Copyright ACCA software S.p.A.";
        private const string TipoFormato = "XMLPwe";
        private const string Versione = "5.01";
        private const string SourceNome = "RevitQTO-CME";
        private const string SourceVersione = "1.0";
        // TipoDocumento: 0 = Generico/Listino, 1 = Computo (valore ipotizzato — verificare).
        private const string TipoDocumentoComputo = "1";
        // Fgs: flag interno PriMus — replichiamo il valore del file di riferimento.
        private const string Fgs = "2147614720";

        // Placeholder "data vuota" usato da PriMus per campi DateTime non valorizzati.
        private const string EmptyDate = "30/12/1899";

        public string FormatName => "XPWE";
        public string FileExtension => ".xml";
        public string FileFilter => "PriMus XPWE (*.xml;*.XPWE)|*.xml;*.XPWE|Tutti i file (*.*)|*.*";
        public ReportExportOptions DefaultOptions => new ReportExportOptions();

        public void Export(ReportDataSet data, string outputPath, ReportExportOptions options)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false,              // PriMus salva tutto su singola riga
                OmitXmlDeclaration = true    // il file di riferimento NON ha <?xml ... ?>
            };

            // Indicizzazione capitoli (3 livelli ACCA: SuperCapitolo, Capitolo, SubCapitolo)
            // con ID sequenziali a partire da 1 — pattern ACCA.
            var chapterIndex = new ChapterIndex();
            chapterIndex.Build(data);

            using var writer = XmlWriter.Create(outputPath, settings);
            writer.WriteStartElement("PweDocumento");

            // Header documento
            writer.WriteElementString("CopyRight", CopyRight);
            writer.WriteElementString("TipoDocumento", TipoDocumentoComputo);
            writer.WriteElementString("TipoFormato", TipoFormato);
            writer.WriteElementString("Versione", Versione);
            writer.WriteElementString("SourceVersione", SourceVersione);
            writer.WriteElementString("SourceNome", SourceNome);
            writer.WriteElementString("Fgs", Fgs);

            // PweDatiGenerali
            writer.WriteStartElement("PweDatiGenerali");

            writer.WriteStartElement("PweDGProgetto");
            writer.WriteStartElement("PweDGDatiGenerali");
            writer.WriteElementString("DesTarif", data.Header.Titolo);
            writer.WriteEndElement(); // PweDGDatiGenerali
            writer.WriteEndElement(); // PweDGProgetto

            writer.WriteStartElement("PweDGConfigurazione");
            writer.WriteStartElement("PweDGConfigNumeri");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("PweDGModuli");
            writer.WriteStartElement("PweDGAnalisi");
            writer.WriteElementString("SpeseUtili", "-1");
            writer.WriteEndElement();
            writer.WriteEndElement();

            // Tabelle Capitoli (flat, con FK)
            writer.WriteStartElement("PweDGCapitoliCategorie");

            // SuperCapitoli (livello 1 — nostri root)
            writer.WriteStartElement("PweDGSuperCapitoli");
            foreach (var sc in chapterIndex.SuperCapitoli)
                WriteDGChapterItem(writer, "DGSuperCapitoliItem", sc);
            writer.WriteEndElement();

            // Capitoli (livello 2)
            writer.WriteStartElement("PweDGCapitoli");
            foreach (var c in chapterIndex.Capitoli)
                WriteDGChapterItem(writer, "DGCapitoliItem", c);
            writer.WriteEndElement();

            // SubCapitoli (livello 3)
            writer.WriteStartElement("PweDGSubCapitoli");
            foreach (var sb in chapterIndex.SubCapitoli)
                WriteDGChapterItem(writer, "DGSubCapitoliItem", sb);
            writer.WriteEndElement();

            // Tabelle Categorie — non usate (vuote): l'asse "Categorie" ACCA è ortogonale
            // ai Capitoli e nel nostro modello non è mappato. Gli export valgono comunque.
            writer.WriteStartElement("PweDGSuperCategorie"); writer.WriteEndElement();
            writer.WriteStartElement("PweDGCategorie"); writer.WriteEndElement();
            writer.WriteStartElement("PweDGSubCategorie"); writer.WriteEndElement();

            writer.WriteEndElement(); // PweDGCapitoliCategorie

            // WBS non usate
            writer.WriteStartElement("PweDGWBSCAP"); writer.WriteEndElement();
            writer.WriteStartElement("PweDGWBS"); writer.WriteEndElement();

            writer.WriteEndElement(); // PweDatiGenerali

            // PweMisurazioni: ElencoPrezzi (voci EP con FK a capitoli) + VociComputo
            writer.WriteStartElement("PweMisurazioni");

            // ElencoPrezzi: una riga EPItem per ciascuna voce del report
            writer.WriteStartElement("PweElencoPrezzi");
            int epId = chapterIndex.NextAvailableId;
            var epIdByOrder = new Dictionary<int, int>();
            foreach (var (entry, chapterChain) in chapterIndex.EnumerateEntries(data))
            {
                epIdByOrder[entry.OrderIndex] = epId;
                WriteEPItem(writer, entry, chapterChain, epId);
                epId++;
            }
            writer.WriteEndElement(); // PweElencoPrezzi

            // VociComputo: una riga VCItem per ogni misurazione
            // Schema VCItem da validare con file computo reale — per ora generiamo
            // un riferimento minimale (IDEP = ID elenco prezzi) con quantità.
            writer.WriteStartElement("PweVociComputo");
            int vcId = 1;
            foreach (var (entry, _) in chapterIndex.EnumerateEntries(data))
            {
                WriteVCItem(writer, entry, epIdByOrder[entry.OrderIndex], vcId);
                vcId++;
            }
            writer.WriteEndElement(); // PweVociComputo

            writer.WriteEndElement(); // PweMisurazioni

            writer.WriteEndElement(); // PweDocumento
        }

        private static void WriteDGChapterItem(XmlWriter writer, string itemName, IndexedChapter ch)
        {
            writer.WriteStartElement(itemName);
            writer.WriteAttributeString("ID", ch.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("Codice", ch.Code);
            writer.WriteElementString("DesEstesa", ch.Name);
            writer.WriteElementString("DesSintetica", ch.Name);
            writer.WriteElementString("DataInit", "");
            // FK a parent (solo per Capitoli e SubCapitoli)
            if (ch.ParentId.HasValue)
                writer.WriteElementString("IDPadre", ch.ParentId.Value.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        private static void WriteEPItem(XmlWriter writer, ReportEntry entry, ChapterChain chain, int id)
        {
            writer.WriteStartElement("EPItem");
            writer.WriteAttributeString("ID", id.ToString(CultureInfo.InvariantCulture));

            writer.WriteElementString("Tariffa", entry.EpCode);
            writer.WriteElementString("Articolo", entry.EpCode);
            writer.WriteElementString("DesRidotta", Truncate(entry.EpDescription, 200));
            writer.WriteElementString("DesEstesa", entry.EpDescription);
            writer.WriteElementString("UnMisura", entry.Unit);

            // PriMus supporta fino a 5 prezzi per voce (diverse revisioni/committenti).
            // Popoliamo solo Prezzo1, gli altri a 0.
            writer.WriteElementString("Prezzo1", entry.UnitPrice.ToString("F5", CultureInfo.InvariantCulture));
            writer.WriteElementString("Prezzo2", "0");
            writer.WriteElementString("Prezzo3", "0");
            writer.WriteElementString("Prezzo4", "0");
            writer.WriteElementString("Prezzo5", "0");
            writer.WriteElementString("CnfQt", "");

            // FK capitoli (0 se non assegnato)
            writer.WriteElementString("IDSpCap", (chain.SuperId ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("IDCap", (chain.CatId ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("IDSbCap", (chain.SubId ?? 0).ToString(CultureInfo.InvariantCulture));

            writer.WriteElementString("CodiceWBSCAP", "");
            writer.WriteElementString("Data", EmptyDate);
            writer.WriteElementString("DesBreve", "");
            writer.WriteElementString("IncMDO", "0");
            writer.WriteElementString("IncMAT", "0");
            writer.WriteElementString("IncSIC", "0");
            writer.WriteElementString("TipoRisorsa", "0");
            writer.WriteElementString("Flags", "512");
            writer.WriteElementString("AdrInternet", "");

            writer.WriteEndElement();
        }

        private static void WriteVCItem(XmlWriter writer, ReportEntry entry, int epId, int vcId)
        {
            // SCHEMA IPOTIZZATO — da validare con file computo XPWE reale.
            // Il file di riferimento (listino) ha VCItem vuoto <VCItem/>, quindi nomi
            // e ordine dei campi sono estrapolati dallo stile ACCA.
            writer.WriteStartElement("VCItem");
            writer.WriteAttributeString("ID", vcId.ToString(CultureInfo.InvariantCulture));

            // Riferimento alla voce di elenco prezzi
            writer.WriteElementString("IDEP", epId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("Tariffa", entry.EpCode);
            writer.WriteElementString("DesEstesa", entry.EpDescription);
            writer.WriteElementString("UnMisura", entry.Unit);

            // Misurazione: Parti uguali × Lunghezza × Larghezza × Altezza
            // Noi abbiamo solo la quantità finale dall'estrattore Revit —
            // la mettiamo in "PartiUguali" e le dimensioni a 1 (il prodotto dà la quantità).
            writer.WriteElementString("PartiUguali", entry.Quantity.ToString("F5", CultureInfo.InvariantCulture));
            writer.WriteElementString("Lunghezza", "1");
            writer.WriteElementString("Larghezza", "1");
            writer.WriteElementString("HPeso", "1");
            writer.WriteElementString("Quantita", entry.Quantity.ToString("F5", CultureInfo.InvariantCulture));

            // Prezzo e importo calcolato
            writer.WriteElementString("Prezzo", entry.UnitPrice.ToString("F5", CultureInfo.InvariantCulture));
            writer.WriteElementString("Importo", entry.Total.ToString("F2", CultureInfo.InvariantCulture));

            // Note / riferimento al modello Revit (ElementId)
            writer.WriteElementString("Note", $"ElementId={entry.ElementId} · {entry.Category}");

            writer.WriteEndElement();
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }

    // =========================================================================
    // Strutture di supporto interne per indicizzazione capitoli con ID ACCA
    // =========================================================================

    internal class IndexedChapter
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int Level { get; set; }  // 1, 2, 3
    }

    internal struct ChapterChain
    {
        public int? SuperId;
        public int? CatId;
        public int? SubId;
    }

    internal class ChapterIndex
    {
        public List<IndexedChapter> SuperCapitoli { get; } = new List<IndexedChapter>();
        public List<IndexedChapter> Capitoli { get; } = new List<IndexedChapter>();
        public List<IndexedChapter> SubCapitoli { get; } = new List<IndexedChapter>();

        private readonly Dictionary<int, IndexedChapter> _byDbId = new Dictionary<int, IndexedChapter>();
        private int _nextId = 1;

        public int NextAvailableId => _nextId;

        public void Build(ReportDataSet data)
        {
            foreach (var root in data.Chapters)
                IndexNode(root, parent: null, level: 1);
        }

        private IndexedChapter IndexNode(ReportChapterNode node, IndexedChapter? parent, int level)
        {
            var ic = new IndexedChapter
            {
                Id = _nextId++,
                ParentId = parent?.Id,
                Code = node.Chapter.Code,
                Name = node.Chapter.Name,
                Level = level
            };
            _byDbId[node.Chapter.Id] = ic;
            if (level == 1) SuperCapitoli.Add(ic);
            else if (level == 2) Capitoli.Add(ic);
            else SubCapitoli.Add(ic);

            foreach (var child in node.Children)
                IndexNode(child, ic, level + 1);

            return ic;
        }

        /// <summary>
        /// Enumera tutte le voci (chaptered + unchaptered) con relativa catena capitoli.
        /// </summary>
        public IEnumerable<(ReportEntry Entry, ChapterChain Chain)> EnumerateEntries(ReportDataSet data)
        {
            foreach (var root in data.Chapters)
                foreach (var item in WalkNode(root, new ChapterChain()))
                    yield return item;

            foreach (var entry in data.UnchaperedEntries)
                yield return (entry, new ChapterChain());
        }

        private IEnumerable<(ReportEntry, ChapterChain)> WalkNode(ReportChapterNode node, ChapterChain parentChain)
        {
            var chain = parentChain;
            if (_byDbId.TryGetValue(node.Chapter.Id, out var ic))
            {
                if (ic.Level == 1) chain.SuperId = ic.Id;
                else if (ic.Level == 2) chain.CatId = ic.Id;
                else chain.SubId = ic.Id;
            }
            foreach (var entry in node.Entries)
                yield return (entry, chain);
            foreach (var child in node.Children)
                foreach (var item in WalkNode(child, chain))
                    yield return item;
        }
    }
}
