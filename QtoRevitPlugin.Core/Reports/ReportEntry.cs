using System;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Riga del computo nel report: una singola voce EP con quantità, prezzo, importo.
    /// Campi audit popolati solo per CSV modalità analitica.
    /// </summary>
    public class ReportEntry
    {
        public int OrderIndex { get; set; }
        public string EpCode { get; set; } = "";
        public string EpDescription { get; set; } = "";
        public string Unit { get; set; } = "";
        public double Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total { get; set; }
        public string ElementId { get; set; } = "";
        public string Category { get; set; } = "";
        // Audit (opzionali)
        public int Version { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string AuditStatus { get; set; } = "";
    }
}
