using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Snapshot DENORMALIZZATO del listino salvato nel DataStorage del .rvt.
    /// Contiene solo le voci effettivamente usate nel progetto (tipicamente 30-50 su 20k).
    ///
    /// Scopo: garantire la **portabilità del .rvt** cross-PC anche senza la UserLibrary:
    /// - PC con UserLibrary: il listino completo è disponibile per ricerca + tagging;
    /// - PC senza UserLibrary: il progetto resta leggibile (voci assegnate presenti qui),
    ///   l'utente riceve un banner "Listino completo non trovato — importa per aggiungere".
    ///
    /// Serializzato come JSON e scritto come entity su un <c>DataStorage</c> "QtoProject"
    /// (nome ben noto). Dimensione tipica: 50 voci × 500 byte ≈ 25 KB, sotto il limite
    /// per-Entity di Extensible Storage (~64 KB).
    ///
    /// Implementation concreta: Sprint 5 Tagging — ad ogni assegnazione EP → elemento,
    /// upsert della voce in questo snapshot.
    /// </summary>
    public class ProjectPriceListSnapshot
    {
        /// <summary>GUID del listino sorgente (<see cref="PriceList.PublicId"/> nella UserLibrary).</summary>
        public string ListPublicId { get; set; } = string.Empty;

        /// <summary>Nome umano-leggibile del listino al momento dello snapshot.</summary>
        public string ListName { get; set; } = string.Empty;

        /// <summary>Regione/versione del listino (es. "Toscana LLPP 2025").</summary>
        public string ListVersion { get; set; } = string.Empty;

        /// <summary>Data dell'ultimo aggiornamento dello snapshot.</summary>
        public DateTime SnapshotUpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Voci EP effettivamente usate nel progetto (solo quelle, non tutto il listino).</summary>
        public List<PriceItemSnapshot> UsedItems { get; set; } = new List<PriceItemSnapshot>();
    }

    /// <summary>
    /// Versione ridotta di <see cref="PriceItem"/> ottimizzata per lo snapshot:
    /// solo i campi necessari a render/export, no FTS, no join.
    /// </summary>
    public class PriceItemSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public string ShortDesc { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double UnitPrice { get; set; }
        public string SuperChapter { get; set; } = string.Empty;
        public string Chapter { get; set; } = string.Empty;
        public string SubChapter { get; set; } = string.Empty;

        /// <summary>True per NP (Nuovi Prezzi) — si esportano sempre con flag distinto.</summary>
        public bool IsCustom { get; set; }
    }
}
