using QtoRevitPlugin.Models;
using System.Collections.Generic;

namespace QtoRevitPlugin.Reports
{
    public class ReportChapterNode
    {
        public ComputoChapter Chapter { get; set; } = null!;
        public List<ReportChapterNode> Children { get; set; } = new List<ReportChapterNode>();
        public List<ReportEntry> Entries { get; set; } = new List<ReportEntry>();
        public decimal Subtotal { get; set; }
    }
}
