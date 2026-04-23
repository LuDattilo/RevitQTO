using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Esporta il computo in CSV italiano (separatore ;, decimale ,, UTF-8 con BOM).
    /// Modalità base: 9 colonne. Modalità analitica (IncludeAuditFields=true): +4 colonne audit.
    /// Cella con ; " \n viene racchiusa in "…", doppi apici raddoppiati ("").
    /// </summary>
    public class CsvExporter : IReportExporter
    {
        public string FormatName => "CSV";
        public string FileExtension => ".csv";
        public string FileFilter => "CSV (*.csv)|*.csv|Tutti i file (*.*)|*.*";
        public ReportExportOptions DefaultOptions => new ReportExportOptions();

        public void Export(ReportDataSet data, string outputPath, ReportExportOptions options)
        {
            var it = CultureInfo.GetCultureInfo("it-IT");
            var sb = new StringBuilder();

            // Header
            sb.Append("Capitolo;Codice;Descrizione;UM;Quantità;PrezzoUnit;Importo;ElementId;Categoria");
            if (options.IncludeAuditFields)
                sb.Append(";Version;CreatedBy;CreatedAt;AuditStatus");
            sb.AppendLine();

            // Write entries ricorsivamente
            foreach (var node in data.Chapters)
                WriteChapterNode(sb, node, options, it, path: "");

            foreach (var entry in data.UnchaperedEntries)
                WriteEntry(sb, entry, "(senza capitolo)", options, it);

            // BOM + UTF-8
            var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            File.WriteAllText(outputPath, sb.ToString(), utf8WithBom);
        }

        private static void WriteChapterNode(StringBuilder sb, ReportChapterNode node, ReportExportOptions options, CultureInfo it, string path)
        {
            var currentPath = string.IsNullOrEmpty(path)
                ? $"{node.Chapter.Code} {node.Chapter.Name}"
                : $"{path} / {node.Chapter.Code} {node.Chapter.Name}";
            foreach (var child in node.Children)
                WriteChapterNode(sb, child, options, it, currentPath);
            foreach (var entry in node.Entries)
                WriteEntry(sb, entry, currentPath, options, it);
        }

        private static void WriteEntry(StringBuilder sb, ReportEntry e, string chapterPath, ReportExportOptions options, CultureInfo it)
        {
            var cells = new List<string>
            {
                chapterPath,
                e.EpCode,
                e.EpDescription,
                e.Unit,
                e.Quantity.ToString("0.00", it),
                e.UnitPrice.ToString("0.00", it),
                e.Total.ToString("0.00", it),
                e.ElementId,
                e.Category
            };
            if (options.IncludeAuditFields)
            {
                cells.Add(e.Version.ToString(CultureInfo.InvariantCulture));
                cells.Add(e.CreatedBy);
                cells.Add(e.CreatedAt == default ? "" : e.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                cells.Add(e.AuditStatus);
            }
            for (int i = 0; i < cells.Count; i++)
                cells[i] = Quote(cells[i]);
            sb.AppendLine(string.Join(";", cells));
        }

        private static string Quote(string value)
        {
            if (value == null) return "";
            var needsQuoting = value.Contains(";") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
            if (!needsQuoting) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
