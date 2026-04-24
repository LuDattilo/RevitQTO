namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Nodo della gerarchia Capitoli sul Prezziario (EPItem).
    /// Livelli: SpCap (SuperCapitolo) → Cap (Capitolo) → SbCap (SubCapitolo).
    /// Corrisponde a DGSuperCapitoliItem/DGCapitoliItem/DGSubCapitoliItem XPWE.
    /// </summary>
    public class ChapterNode
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Level { get; set; } = "SpCap";
        public string Codice { get; set; } = "";
        public string DesSintetica { get; set; } = "";
        public string? DesEstesa { get; set; }
        public string? DataInit { get; set; }
        public int Durata { get; set; }
        public string? CodFase { get; set; }
        public double Percentuale { get; set; }
        public int? ParentId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
