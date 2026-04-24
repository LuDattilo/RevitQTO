namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Nodo WBS a profondità libera. Kind=WbsCap → referenziato da EPItem (Prezziario),
    /// Kind=WbsComputo → referenziato da VCItem (Computo).
    /// </summary>
    public class WbsNode
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Kind { get; set; } = "WbsCap";
        public string Codice { get; set; } = "";
        public string DesSintetica { get; set; } = "";
        public int? ParentId { get; set; }
        public int Level { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
