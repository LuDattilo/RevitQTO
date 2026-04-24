using System;

namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>Audit dei job di export XPWE (traccia file, checksum, versione).</summary>
    public class XpweExportJob
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public DateTime ExportedAt { get; set; }
        public int TipoDocumento { get; set; }
        public string XpweVersion { get; set; } = "5.04";
        public string? FilePath { get; set; }
        public string? FileChecksum { get; set; }
        public string? ValidationReport { get; set; }
    }
}
