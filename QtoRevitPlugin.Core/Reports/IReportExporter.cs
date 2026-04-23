namespace QtoRevitPlugin.Reports
{
    public interface IReportExporter
    {
        string FormatName { get; }
        string FileExtension { get; }
        string FileFilter { get; }
        ReportExportOptions DefaultOptions { get; }
        void Export(ReportDataSet data, string outputPath, ReportExportOptions options);
    }
}
