using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Sorgente C: voce di computo inserita manualmente per lavorazioni non modellabili
    /// (oneri sicurezza, trasporti, noli, mano d'opera oraria, pulizie a corpo).
    /// Persistita in tabella dedicata `ManualItems` con audit trail completo.
    /// Nessun UniqueId/ElementId: queste voci sono orfane dal modello per design.
    /// </summary>
    public class ManualQuantityEntry
    {
        public int Id { get; set; }
        public int SessionId { get; set; }

        public string EpCode { get; set; } = string.Empty;
        public string EpDescription { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double Total => Quantity * UnitPrice;

        public string Notes { get; set; } = string.Empty;

        /// <summary>Path al documento giustificativo allegato (PDF/DOC/immagine). Per audit trail contrattuale.</summary>
        public string AttachmentPath { get; set; } = string.Empty;

        // Audit trail — obbligatorio per voci "fuori modello" in fase di verifica gara
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedAt { get; set; }

        /// <summary>Soft delete — la riga resta nel DB per ricostruzione cronologia.</summary>
        public bool IsDeleted { get; set; }
    }
}
