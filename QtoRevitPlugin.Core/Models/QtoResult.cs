namespace QtoRevitPlugin.Models
{
    public enum QtoSource
    {
        RevitElement,   // Sorgente A: famiglia Revit
        Room,           // Sorgente B: Room/Space con formula NCalc
        Manual          // Sorgente C: voce inserita manualmente
    }

    /// <summary>
    /// Risultato del calcolo QTO per una singola voce EP aggregata.
    /// Usato dall'ExportEngine e dalla DockablePane preview.
    /// </summary>
    public class QtoResult
    {
        public int ElementId { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;

        public string EpCode { get; set; } = string.Empty;
        public string EpDescription { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;

        public double QuantityGross { get; set; }
        public double QuantityDeducted { get; set; }
        public double Quantity => QuantityGross - QuantityDeducted;

        public double UnitPrice { get; set; }
        public double Total => Quantity * UnitPrice;

        public string RuleApplied { get; set; } = string.Empty;
        public QtoSource Source { get; set; } = QtoSource.RevitElement;

        /// <summary>Nota leggibile per la preview: es. "45,2 m² lordi − 3,1 m² (2 aperture) = 42,1 m² netti"</summary>
        public string QuantityNote { get; set; } = string.Empty;
    }
}
