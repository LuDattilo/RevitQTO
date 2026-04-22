using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Dato QTO per singolo elemento Revit, persistito in Extensible Storage v1.
    /// Multi-EP: un elemento può avere N voci EP assegnate (es. un muro = intonaco esterno +
    /// intonaco interno + tinteggiatura). Questo modello è pure C# (netstandard2.0) per
    /// consentire unit test senza Revit API — il mapping su Entity Revit avviene nel plugin
    /// main via <c>ExtensibleStorageRepo</c>.
    /// </summary>
    public class QtoElementData
    {
        /// <summary>Codici EP assegnati (multi-EP). Vuoto = elemento non ancora taggato.</summary>
        public IList<string> AssignedEpCodes { get; set; } = new List<string>();

        /// <summary>
        /// Sorgente della quantità: "RevitElement" (A — famiglie), "Room" (B — locali+NCalc),
        /// "Manual" (C — voci manuali). Serializzato come stringa per estensibilità schema.
        /// </summary>
        public string Source { get; set; } = "RevitElement";

        /// <summary>Timestamp ISO-8601 UTC dell'ultimo tagging.</summary>
        public DateTime LastTagged { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Motivo dell'esclusione manuale dall'export (es. "escluso per volere del DL").
        /// Null/empty = non escluso.
        /// </summary>
        public string? ExclusionReason { get; set; }

        /// <summary>True se l'elemento è stato escluso manualmente dal computo.</summary>
        public bool IsExcluded => !string.IsNullOrEmpty(ExclusionReason);

        /// <summary>True se non ci sono EP assegnati (né esclusione): stato "mancante" in HealthCheck.</summary>
        public bool IsUntagged => AssignedEpCodes.Count == 0 && !IsExcluded;
    }
}
