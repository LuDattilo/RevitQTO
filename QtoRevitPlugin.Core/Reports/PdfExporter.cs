using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Esporta il computo in PDF A4 via QuestPDF. Header con titolo+data, footer con pag X di Y,
    /// corpo tabellare raggruppato per SuperCategoria → Categoria → SubCategoria con subtotali.
    /// </summary>
    public class PdfExporter : IReportExporter
    {
        static PdfExporter()
        {
            // QuestPDF Community license — uso commerciale consentito sotto €1M fatturato.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public string FormatName => "PDF";
        public string FileExtension => ".pdf";
        public string FileFilter => "PDF (*.pdf)|*.pdf|Tutti i file (*.*)|*.*";
        public ReportExportOptions DefaultOptions => new ReportExportOptions();

        public void Export(ReportDataSet data, string outputPath, ReportExportOptions options)
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text(data.Header.Titolo).FontSize(14).Bold();
                            col.Item().Text($"Committente: {data.Header.Committente}").FontSize(9);
                            col.Item().Text($"DL: {data.Header.DirettoreLavori}").FontSize(9);
                        });
                        row.ConstantItem(120).AlignRight().Text(data.Header.DataCreazione.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // Table header
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(30);  // N°
                                c.ConstantColumn(60);  // Codice
                                c.RelativeColumn(3);   // Descrizione
                                c.ConstantColumn(40);  // UM
                                c.ConstantColumn(60);  // Quantità
                                c.ConstantColumn(60);  // Prezzo
                                c.ConstantColumn(60);  // Importo
                            });
                            table.Header(h =>
                            {
                                h.Cell().Background(Colors.Blue.Darken2).Padding(2).Text("N°").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken2).Padding(2).Text("Codice").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken2).Padding(2).Text("Descrizione").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken2).Padding(2).Text("UM").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken2).Padding(2).AlignRight().Text("Quantità").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken2).Padding(2).AlignRight().Text("Prezzo").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken2).Padding(2).AlignRight().Text("Importo").FontColor(Colors.White).Bold();
                            });

                            foreach (var node in data.Chapters)
                                WriteChapterRows(table, node, indent: 0);
                            foreach (var entry in data.UnchaperedEntries)
                                WriteEntryRow(table, entry);

                            // Total
                            table.Cell().ColumnSpan(6).Background(Colors.Blue.Darken2).Padding(4).AlignRight().Text("TOTALE GENERALE").FontColor(Colors.White).Bold();
                            table.Cell().Background(Colors.Blue.Darken2).Padding(4).AlignRight()
                                .Text(data.GrandTotal.ToString("N2", CultureInfo.GetCultureInfo("it-IT")) + " €").FontColor(Colors.White).Bold();
                        });
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("Pag. ");
                        t.CurrentPageNumber();
                        t.Span(" di ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf(outputPath);
        }

        private static void WriteChapterRows(QuestPDF.Fluent.TableDescriptor table, ReportChapterNode node, int indent)
        {
            table.Cell().ColumnSpan(7).Background(Colors.Grey.Lighten3).Padding(4)
                .Text($"{new string(' ', indent * 2)}{node.Chapter.Code} {node.Chapter.Name}").Bold();
            foreach (var child in node.Children)
                WriteChapterRows(table, child, indent + 1);
            foreach (var entry in node.Entries)
                WriteEntryRow(table, entry);
            // Subtotal
            table.Cell().ColumnSpan(6).Background(Colors.Grey.Lighten4).Padding(2).AlignRight()
                .Text($"Subtotale {node.Chapter.Code}").Italic();
            table.Cell().Background(Colors.Grey.Lighten4).Padding(2).AlignRight()
                .Text(node.Subtotal.ToString("N2", CultureInfo.GetCultureInfo("it-IT")) + " €").Italic();
        }

        private static void WriteEntryRow(QuestPDF.Fluent.TableDescriptor table, ReportEntry e)
        {
            var it = CultureInfo.GetCultureInfo("it-IT");
            table.Cell().Padding(2).Text(e.OrderIndex.ToString());
            table.Cell().Padding(2).Text(e.EpCode);
            table.Cell().Padding(2).Text(e.EpDescription);
            table.Cell().Padding(2).Text(e.Unit);
            table.Cell().Padding(2).AlignRight().Text(e.Quantity.ToString("N2", it));
            table.Cell().Padding(2).AlignRight().Text(e.UnitPrice.ToString("N2", it));
            table.Cell().Padding(2).AlignRight().Text(e.Total.ToString("N2", it));
        }
    }
}
