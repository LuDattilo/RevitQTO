using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Preset di regole di selezione salvato (§I6 QTO-Implementazioni-v3.md).
    ///
    /// <para>Doppio layer di persistenza (§I6):</para>
    /// <list type="bullet">
    ///   <item>File JSON locale (%AppData%\QtoPlugin\rules\) — portabile tra macchine</item>
    ///   <item>DataStorage ES nel .rvt — condiviso col team su modello workshared</item>
    /// </list>
    ///
    /// <para>La SetupView espone un dropdown "Regole salvate" da cui caricare il preset
    /// e applicarlo immediatamente alla SelectionView (§I3 filtri parametrici).</para>
    /// </summary>
    public class SelectionRulePreset
    {
        /// <summary>Nome human-readable del preset (es. "Muri Esterni — Contrassegno A").</summary>
        public string RuleName { get; set; } = "";

        /// <summary>Categoria Revit (BIC string come "OST_Walls") o vuota = tutte.</summary>
        public string Category { get; set; } = "";

        /// <summary>ElementId della Phase Revit filtrata, 0 = nessun filtro fase.</summary>
        public int PhaseId { get; set; }

        /// <summary>"New", "Existing", "Demolished" o vuota.</summary>
        public string PhaseStatus { get; set; } = "";

        /// <summary>Regole parametriche composte (AND tra le regole).</summary>
        public List<SelectionRuleEntry> Rules { get; set; } = new List<SelectionRuleEntry>();

        /// <summary>Parametro Revit usato per la ricerca testuale inline (es. "ALL_MODEL_MARK").</summary>
        public string InlineSearchParam { get; set; } = "";

        /// <summary>Timestamp di ultimo salvataggio. Non serializzato in JSON per ora.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Voce di regola filtro dentro un <see cref="SelectionRulePreset"/> persistito.
    /// Versione serializzabile/portabile (JSON + DB) — distinto da
    /// <c>QtoRevitPlugin.Services.ParamFilterRule</c> (runtime in memory per SelectionView).
    /// Operatori supportati: Equals, Contains, StartsWith, EndsWith, GreaterThan, LessThan, NotEquals.
    /// </summary>
    public class SelectionRuleEntry
    {
        /// <summary>Nome del parametro Revit o BIP (es. "FUNCTION_PARAM", "QTO_Contrassegno").</summary>
        public string Parameter { get; set; } = "";

        /// <summary>Nome dell'evaluator (usato come string per JSON portability).</summary>
        public string Evaluator { get; set; } = "Equals";

        /// <summary>Valore di confronto (stringa — il parser interpreta come numero se serve).</summary>
        public string Value { get; set; } = "";
    }
}
