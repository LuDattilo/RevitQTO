namespace QtoRevitPlugin.Reports
{
    public class ReportExportOptions
    {
        public bool IncludeAuditFields { get; set; }
        public bool IncludeDeletedAndSuperseded { get; set; } = false;
        public bool GroupByChapter { get; set; } = true;
        public string? CompanyLogoPath { get; set; }
        public string Titolo { get; set; } = "";
        public string Committente { get; set; } = "";
        public string DirettoreLavori { get; set; } = "";
    }
}
