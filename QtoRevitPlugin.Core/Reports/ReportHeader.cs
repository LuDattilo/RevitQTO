using System;

namespace QtoRevitPlugin.Reports
{
    public class ReportHeader
    {
        public string Titolo { get; set; } = "";
        public string Committente { get; set; } = "";
        public string DirettoreLavori { get; set; } = "";
        public DateTime DataCreazione { get; set; }
    }
}
