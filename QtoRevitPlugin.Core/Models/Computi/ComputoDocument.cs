using System;

namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Corrisponde a PweDocumento dello schema XPWE. Un documento = una sessione computo
    /// o prezziario custom. Differenziati da TipoDocumento (0=Prezziario, 1=Computo).
    /// </summary>
    public class ComputoDocument
    {
        public int Id { get; set; }
        public int WorkSessionId { get; set; }
        public int TipoDocumento { get; set; }
        public string Versione { get; set; } = "5.04";
        public long Fgs { get; set; } = 2147614720L;
        public double PercPrezzi { get; set; }
        public string? Comune { get; set; }
        public string? Provincia { get; set; }
        public string? Oggetto { get; set; }
        public string? Committente { get; set; }
        public string? Impresa { get; set; }
        public string? ParteOpera { get; set; }
        public string Currency { get; set; } = "EUR";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
