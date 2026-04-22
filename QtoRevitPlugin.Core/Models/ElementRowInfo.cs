namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Proiezione pure-C# di un elemento Revit per display UI (§I3 SelectionView).
    /// Contiene solo i campi mostrati nel DataGrid, senza dipendenza Revit API nel Core.
    /// </summary>
    public class ElementRowInfo
    {
        public int ElementId { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string PhaseCreatedName { get; set; } = string.Empty;
        public string PhaseDemolishedName { get; set; } = string.Empty;

        /// <summary>Rappresentazione breve per tooltip/log.</summary>
        public override string ToString() =>
            $"[{ElementId}] {FamilyName} · {TypeName} ({Category})";
    }
}
