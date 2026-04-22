namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Sorgente C: voce di computo inserita manualmente per lavorazioni non modellabili
    /// (ponteggi, smaltimento, opere provvisionali, ecc.).
    /// Identificata come "sorgente: manuale" nel DB e nell'export.
    /// </summary>
    public class ManualQuantityEntry
    {
        public int Id { get; set; }
        public int SessionId { get; set; }

        public string EpCode { get; set; } = string.Empty;
        public string EpDescription { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double Total => Quantity * UnitPrice;

        public string Notes { get; set; } = string.Empty;
    }
}
