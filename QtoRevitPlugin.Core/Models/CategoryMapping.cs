namespace QtoRevitPlugin.Models
{
    public enum QuantityParameterType
    {
        Area,
        Volume,
        Length,
        Count
    }

    /// <summary>
    /// Associa una categoria Revit a un parametro geometrico e a un'eventuale formula NCalc per il prezzo.
    /// Il campo BuiltInCategoryId è il valore intero di BuiltInCategory (evita la dipendenza da Revit API nel Core).
    /// Persistita in Extensible Storage sul ProjectInfo del .rvt.
    /// </summary>
    public class CategoryMapping
    {
        /// <summary>Valore intero di BuiltInCategory — es. -2000011 per OST_Walls.</summary>
        public int BuiltInCategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public QuantityParameterType ParameterType { get; set; }
        public string UnitLabel { get; set; } = string.Empty;

        /// <summary>Formula NCalc opzionale — es. "Prezzo * (1 + PercSicurezza / 100)"</summary>
        public string PriceFormula { get; set; } = string.Empty;
        public double SecurityPercent { get; set; }
    }
}
