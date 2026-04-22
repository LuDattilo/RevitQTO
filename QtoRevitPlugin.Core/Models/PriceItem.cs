namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Voce di elenco prezzi (da DCF, Excel o CSV). Può essere una voce standard o un Nuovo Prezzo (IsNP).
    /// </summary>
    public class PriceItem
    {
        public int Id { get; set; }
        public int PriceListId { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Chapter { get; set; } = string.Empty;
        public string SubChapter { get; set; } = string.Empty;
        public string SuperChapter { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ShortDesc { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double UnitPrice { get; set; }
        public string Notes { get; set; } = string.Empty;

        /// <summary>True se la voce è un Nuovo Prezzo (non nel contratto originale).</summary>
        public bool IsNP { get; set; }

        /// <summary>Nome del listino di provenienza — popolato in join con PriceLists.</summary>
        public string ListName { get; set; } = string.Empty;

        public override string ToString() =>
            $"{Code} – {(string.IsNullOrEmpty(ShortDesc) ? Description : ShortDesc)}";
    }
}
