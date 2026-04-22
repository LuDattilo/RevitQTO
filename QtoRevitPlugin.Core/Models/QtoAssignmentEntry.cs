using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Entry ricca di assegnazione EP→elemento (Sprint 5 Tagging §Scheda 3).
    /// Serializzata come JSON in un campo array di <see cref="QtoElementData"/>.
    ///
    /// Ogni elemento Revit può avere N entries (multi-EP) — es. un muro esterno:
    ///   entry 1 → A.02.001 (muratura) · Area 12.5 m² · 562.50 €
    ///   entry 2 → C.05.003 (intonaco esterno) · Area 12.5 m² · 187.50 €
    ///   entry 3 → D.01.001 (tinteggiatura) · Area 12.5 m² · 87.50 €
    /// </summary>
    public class QtoAssignmentEntry
    {
        /// <summary>Codice EP (chiave nel listino UserLibrary).</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Descrizione breve snapshotted al momento dell'assegnazione.</summary>
        public string ShortDesc { get; set; } = string.Empty;

        /// <summary>Unità di misura (es. "m²", "m³", "ml", "cad").</summary>
        public string UnitOfMeasure { get; set; } = string.Empty;

        /// <summary>Prezzo unitario snapshotted al momento dell'assegnazione (€ senza IVA).</summary>
        public double UnitPrice { get; set; }

        /// <summary>
        /// Parametro geometrico usato per il calcolo: "Area", "Volume", "Length", "Count",
        /// oppure il nome di un parametro custom (Shared Param) se usato.
        /// </summary>
        public string Param { get; set; } = string.Empty;

        /// <summary>Quantità calcolata dal parametro (già convertita in unità SI: m/m²/m³/count).</summary>
        public double Quantity { get; set; }

        /// <summary>Totale voce = Quantity × UnitPrice. Precalcolato per audit/export senza ricalcolo.</summary>
        public double Total { get; set; }

        /// <summary>Timestamp ISO-8601 UTC di assegnazione.</summary>
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Sorgente: "RevitElement" (A — famiglie), "Room" (B — locali+NCalc), "Manual" (C).
        /// Replica di <see cref="QtoElementData.Source"/> per entry — utile quando l'elemento ha
        /// entries miste (multi-EP con quantità da sorgenti diverse).
        /// </summary>
        public string Source { get; set; } = "RevitElement";

        /// <summary>True se la voce è un NP (Nuovo Prezzo). Si esporta con flag distinto.</summary>
        public bool IsCustom { get; set; }
    }
}
