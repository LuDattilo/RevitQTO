using System.Globalization;
using System.Text;
using System.Xml;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// [DEPRECATO] Exporter XPWE con schema inventato (root &lt;PriMus&gt; + namespace).
    /// Non compatibile con PriMus ACCA reale (root &lt;PweDocumento&gt;, struttura flat con FK).
    /// Vedi <see cref="XpweExporter"/> per implementazione aderente al formato ACCA.
    /// Mantenuto solo per backward-compatibility con test esistenti — non usare in produzione.
    /// </summary>
    public class XpweExporterLegacy : IReportExporter
    {
        public const string XpweNamespace = "http://www.acca.it/primus/xpwe/v1";

        public string FormatName => "XPWE";
        public string FileExtension => ".xml";
        public string FileFilter => "PriMus XPWE (*.xml)|*.xml|Tutti i file (*.*)|*.*";
        public ReportExportOptions DefaultOptions => new ReportExportOptions();

        public void Export(ReportDataSet data, string outputPath, ReportExportOptions options)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = false
            };

            using var writer = XmlWriter.Create(outputPath, settings);
            writer.WriteStartDocument();
            writer.WriteStartElement("PriMus", XpweNamespace);
            writer.WriteAttributeString("versione", "1.0");

            // Intestazione
            writer.WriteStartElement("Intestazione", XpweNamespace);
            writer.WriteElementString("Titolo", XpweNamespace, data.Header.Titolo);
            writer.WriteElementString("Committente", XpweNamespace, data.Header.Committente);
            writer.WriteElementString("DirettoreLavori", XpweNamespace, data.Header.DirettoreLavori);
            writer.WriteElementString("DataCreazione", XpweNamespace, data.Header.DataCreazione.ToString("s", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Computo
            writer.WriteStartElement("Computo", XpweNamespace);
            foreach (var superNode in data.Chapters)
                WriteSuperCategoria(writer, superNode);

            if (data.UnchaperedEntries.Count > 0)
            {
                writer.WriteStartElement("SuperCategoria", XpweNamespace);
                writer.WriteAttributeString("codice", "00");
                writer.WriteAttributeString("descrizione", "(senza capitolo)");
                writer.WriteStartElement("Categoria", XpweNamespace);
                writer.WriteAttributeString("codice", "00.A");
                writer.WriteAttributeString("descrizione", "(senza capitolo)");
                writer.WriteStartElement("SubCategoria", XpweNamespace);
                writer.WriteAttributeString("codice", "00.A.01");
                writer.WriteAttributeString("descrizione", "(senza capitolo)");
                foreach (var entry in data.UnchaperedEntries)
                    WriteVoce(writer, entry);
                writer.WriteEndElement(); // SubCategoria
                writer.WriteEndElement(); // Categoria
                writer.WriteEndElement(); // SuperCategoria
            }
            writer.WriteEndElement(); // Computo

            // Totali
            writer.WriteStartElement("Totali", XpweNamespace);
            writer.WriteElementString("Totale", XpweNamespace, data.GrandTotal.ToString("F2", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            writer.WriteEndElement(); // PriMus
            writer.WriteEndDocument();
        }

        private static void WriteSuperCategoria(XmlWriter writer, ReportChapterNode node)
        {
            writer.WriteStartElement("SuperCategoria", XpweNamespace);
            writer.WriteAttributeString("codice", node.Chapter.Code);
            writer.WriteAttributeString("descrizione", node.Chapter.Name);
            foreach (var catNode in node.Children)
                WriteCategoria(writer, catNode);
            foreach (var entry in node.Entries)
                WriteVoce(writer, entry);
            writer.WriteEndElement();
        }

        private static void WriteCategoria(XmlWriter writer, ReportChapterNode node)
        {
            writer.WriteStartElement("Categoria", XpweNamespace);
            writer.WriteAttributeString("codice", node.Chapter.Code);
            writer.WriteAttributeString("descrizione", node.Chapter.Name);
            foreach (var subNode in node.Children)
                WriteSubCategoria(writer, subNode);
            foreach (var entry in node.Entries)
                WriteVoce(writer, entry);
            writer.WriteEndElement();
        }

        private static void WriteSubCategoria(XmlWriter writer, ReportChapterNode node)
        {
            writer.WriteStartElement("SubCategoria", XpweNamespace);
            writer.WriteAttributeString("codice", node.Chapter.Code);
            writer.WriteAttributeString("descrizione", node.Chapter.Name);
            foreach (var entry in node.Entries)
                WriteVoce(writer, entry);
            writer.WriteEndElement();
        }

        private static void WriteVoce(XmlWriter writer, ReportEntry entry)
        {
            writer.WriteStartElement("Voce", XpweNamespace);
            writer.WriteAttributeString("numero", entry.OrderIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("CodiceEP", XpweNamespace, entry.EpCode);
            writer.WriteElementString("Descrizione", XpweNamespace, entry.EpDescription);
            writer.WriteElementString("UM", XpweNamespace, entry.Unit);
            writer.WriteElementString("Quantita", XpweNamespace, entry.Quantity.ToString("F2", CultureInfo.InvariantCulture));
            writer.WriteElementString("PrezzoUnitario", XpweNamespace, entry.UnitPrice.ToString("F5", CultureInfo.InvariantCulture));
            writer.WriteElementString("Importo", XpweNamespace, entry.Total.ToString("F2", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }
    }
}
