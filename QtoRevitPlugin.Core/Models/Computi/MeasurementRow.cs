namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Voce del Computo (VCItem). Aggrega 1-N MeasurementSubRow (RGItem).
    /// Quantita è cache di SUM(SubRows.Quantita).
    /// </summary>
    public class MeasurementRow
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int PriceItemId { get; set; }
        public double Quantita { get; set; }
        public string? DataMis { get; set; }
        public int Flags { get; set; }
        public int? SpCatId { get; set; }
        public int? CatId { get; set; }
        public int? SbCatId { get; set; }
        public int? WbsComputoNodeId { get; set; }
        public int SortOrder { get; set; }
    }
}
