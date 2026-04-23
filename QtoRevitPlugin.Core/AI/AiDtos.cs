using QtoRevitPlugin.Models;
using System.Collections.Generic;

namespace QtoRevitPlugin.AI
{
    /// <summary>
    /// Suggerimento di una voce EP per un elemento Revit, con punteggio di similarità.
    /// Ritornato da <see cref="IQtoAiProvider.SuggestEpAsync"/>.
    /// </summary>
    public class MappingSuggestion
    {
        /// <summary>Voce di listino suggerita (può essere null se l'Id cache è orfano).</summary>
        public PriceItem? PriceItem { get; set; }

        /// <summary>Cosine similarity in [0, 1]. Maggiore = match migliore.</summary>
        public float Score { get; set; }

        /// <summary>Label human-readable (es. "87% match") per UI badge.</summary>
        public string Label { get; set; } = "";

        /// <summary>Categoria di confidenza derivata dal score (per icone/colori UI).</summary>
        public MatchConfidence Confidence => Score switch
        {
            >= 0.75f => MatchConfidence.VeryLikely,
            >= 0.60f => MatchConfidence.Likely,
            >= 0.45f => MatchConfidence.Uncertain,
            _        => MatchConfidence.Unlikely
        };
    }

    /// <summary>Soglie indicative da §7.3 del doc AI.</summary>
    public enum MatchConfidence
    {
        /// <summary>score &gt;= 0.75 — abbinamento molto probabile.</summary>
        VeryLikely,
        /// <summary>0.60 &le; score &lt; 0.75 — abbinamento ragionevole.</summary>
        Likely,
        /// <summary>0.45 &le; score &lt; 0.60 — zona grigia, da verificare manualmente.</summary>
        Uncertain,
        /// <summary>score &lt; 0.45 — abbinamento improbabile / mismatch.</summary>
        Unlikely
    }

    /// <summary>
    /// Incoerenza semantica rilevata tra la categoria/famiglia di un elemento Revit
    /// e l'EP assegnato (cosine similarity &lt; 0.45). Ritornato dal Health Check AI.
    /// </summary>
    public class SemanticMismatch
    {
        public string UniqueId { get; set; } = "";
        public string Category { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string EpCode { get; set; } = "";
        public string EpDescription { get; set; } = "";

        /// <summary>Score cosine tra embedding(categoria+famiglia) e embedding(EP).</summary>
        public float Similarity { get; set; }

        /// <summary>EP alternativi con score più alto, per proposta di correzione.</summary>
        public IReadOnlyList<MappingSuggestion> Suggestions { get; set; } = new List<MappingSuggestion>();
    }

    /// <summary>
    /// Anomalia quantitativa rilevata via z-score statistico (no AI). Individua
    /// elementi con Quantity molto fuori dalla media del loro gruppo EP.
    /// Complementare ai mismatch semantici.
    /// </summary>
    public class QuantityAnomaly
    {
        public string UniqueId { get; set; } = "";
        public string EpCode { get; set; } = "";
        public double Quantity { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double ZScore { get; set; }

        /// <summary>Alta se z-score &gt; 3.5, Media se 2.5 &lt; z &le; 3.5.</summary>
        public AnomalySeverity Severity { get; set; }

        /// <summary>Messaggio human-readable per UI health check.</summary>
        public string Message { get; set; } = "";
    }

    public enum AnomalySeverity
    {
        Media,
        Alta
    }
}
