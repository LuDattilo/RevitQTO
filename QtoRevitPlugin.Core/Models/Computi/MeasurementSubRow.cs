namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Riga di misura (RGItem). Formula: PartiUguali × (Lunghezza ?? 1) × (Larghezza ?? 1) × (HPeso ?? 1).
    /// IDVV: Revit ElementId (&gt;0) oppure contatore locale negativo (&lt;0) per voci manuali.
    /// </summary>
    public class MeasurementSubRow
    {
        public int Id { get; set; }
        public int MeasurementRowId { get; set; }
        public int IDVV { get; set; }
        public string? Descrizione { get; set; }
        public double PartiUguali { get; set; } = 1;
        public double? Lunghezza { get; set; }
        public double? Larghezza { get; set; }
        public double? HPeso { get; set; }
        public double Quantita { get; set; }
        public int Flags { get; set; }
        public int SortOrder { get; set; }

        /// <summary>Categoria Revit dell'elemento (IDVV), persistita all'assegnazione. v13. Null per voci manuali/legacy.</summary>
        public string? Category { get; set; }

        /// <summary>Nome famiglia Revit dell'elemento (IDVV), persistito all'assegnazione. v13. Null per voci manuali/legacy.</summary>
        public string? FamilyName { get; set; }
    }
}
