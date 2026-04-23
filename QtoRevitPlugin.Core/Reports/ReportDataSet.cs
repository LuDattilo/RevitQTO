using QtoRevitPlugin.Models;
using System.Collections.Generic;

namespace QtoRevitPlugin.Reports
{
    public class ReportDataSet
    {
        public WorkSession Session { get; set; } = null!;
        public ReportHeader Header { get; set; } = new ReportHeader();
        public List<ReportChapterNode> Chapters { get; set; } = new List<ReportChapterNode>();
        public List<ReportEntry> UnchaperedEntries { get; set; } = new List<ReportEntry>();
        public decimal GrandTotal { get; set; }
    }
}
