namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Nodo della gerarchia Categorie sul Computo (VCItem). Distinto da SoaCategory.
    /// Livelli: SpCat (SuperCategoria) → Cat (Categoria) → SbCat (SubCategoria).
    /// Runtime-defined per documento (non c'è uno standard preimpostato).
    /// </summary>
    public class CategoryNode
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Level { get; set; } = "SpCat";
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
