using ClosedXML.Excel;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Esporta il computo in Excel (.xlsx) con 2 sheet: Computo + Metadati.
    /// Colonne Computo: N°, Capitolo, Codice, Descrizione, UM, Quantità, Prezzo, Importo.
    /// Raggruppato per capitolo con subtotali evidenziati.
    /// </summary>
    public class ExcelExporter : IReportExporter
    {
        public string FormatName => "Excel";
        public string FileExtension => ".xlsx";
        public string FileFilter => "Excel (*.xlsx)|*.xlsx|Tutti i file (*.*)|*.*";
        public ReportExportOptions DefaultOptions => new ReportExportOptions();

        public void Export(ReportDataSet data, string outputPath, ReportExportOptions options)
        {
            using var wb = new XLWorkbook();

            var wsComputo = wb.Worksheets.Add("Computo");
            WriteHeader(wsComputo);
            int row = 2;
            foreach (var node in data.Chapters)
                row = WriteChapterNode(wsComputo, node, row, path: "");
            foreach (var entry in data.UnchaperedEntries)
            {
                WriteEntry(wsComputo, entry, "(senza capitolo)", row);
                row++;
            }
            WriteGrandTotal(wsComputo, row, data.GrandTotal);

            FormatColumns(wsComputo);

            var wsMeta = wb.Worksheets.Add("Metadati");
            wsMeta.Cell(1, 1).Value = "Titolo"; wsMeta.Cell(1, 2).Value = data.Header.Titolo;
            wsMeta.Cell(2, 1).Value = "Committente"; wsMeta.Cell(2, 2).Value = data.Header.Committente;
            wsMeta.Cell(3, 1).Value = "Direttore Lavori"; wsMeta.Cell(3, 2).Value = data.Header.DirettoreLavori;
            wsMeta.Cell(4, 1).Value = "Data"; wsMeta.Cell(4, 2).Value = data.Header.DataCreazione;
            wsMeta.Cell(5, 1).Value = "Progetto"; wsMeta.Cell(5, 2).Value = data.Session.ProjectName;
            wsMeta.Cell(6, 1).Value = "Sessione"; wsMeta.Cell(6, 2).Value = data.Session.SessionName;
            wsMeta.Cell(7, 1).Value = "Totale Generale"; wsMeta.Cell(7, 2).Value = data.GrandTotal;
            wsMeta.Columns().AdjustToContents();

            wb.SaveAs(outputPath);
        }

        private static void WriteHeader(IXLWorksheet ws)
        {
            var headers = new[] { "N°", "Capitolo", "Codice", "Descrizione", "UM", "Quantità", "Prezzo", "Importo" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E6FD9");
                cell.Style.Font.FontColor = XLColor.White;
            }
            ws.SheetView.FreezeRows(1);
        }

        private static int WriteChapterNode(IXLWorksheet ws, ReportChapterNode node, int row, string path)
        {
            var currentPath = string.IsNullOrEmpty(path)
                ? $"{node.Chapter.Code} {node.Chapter.Name}"
                : $"{path} / {node.Chapter.Code} {node.Chapter.Name}";
            foreach (var child in node.Children)
                row = WriteChapterNode(ws, child, row, currentPath);
            foreach (var entry in node.Entries)
            {
                WriteEntry(ws, entry, currentPath, row);
                row++;
            }
            // Subtotale
            ws.Cell(row, 2).Value = $"Subtotale {currentPath}";
            ws.Cell(row, 8).Value = node.Subtotal;
            ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0F0F0");
            ws.Range(row, 1, row, 8).Style.Font.Bold = true;
            return row + 1;
        }

        private static void WriteEntry(IXLWorksheet ws, ReportEntry e, string chapterPath, int row)
        {
            ws.Cell(row, 1).Value = e.OrderIndex;
            ws.Cell(row, 2).Value = chapterPath;
            ws.Cell(row, 3).Value = e.EpCode;
            ws.Cell(row, 4).Value = e.EpDescription;
            ws.Cell(row, 5).Value = e.Unit;
            ws.Cell(row, 6).Value = e.Quantity;
            ws.Cell(row, 7).Value = e.UnitPrice;
            ws.Cell(row, 8).Value = e.Total;
        }

        private static void WriteGrandTotal(IXLWorksheet ws, int row, decimal total)
        {
            ws.Cell(row, 2).Value = "TOTALE GENERALE";
            ws.Cell(row, 8).Value = total;
            ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E6FD9");
            ws.Range(row, 1, row, 8).Style.Font.FontColor = XLColor.White;
            ws.Range(row, 1, row, 8).Style.Font.Bold = true;
        }

        private static void FormatColumns(IXLWorksheet ws)
        {
            ws.Column(6).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(7).Style.NumberFormat.Format = "#,##0.00 €";
            ws.Column(8).Style.NumberFormat.Format = "#,##0.00 €";
            ws.Columns().AdjustToContents();
        }
    }
}
