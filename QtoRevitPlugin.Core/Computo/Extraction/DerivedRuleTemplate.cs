using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Computo.Extraction
{
    /// <summary>
    /// Una regola di voce derivata come definita in configurazione (JSON), prima della risoluzione del
    /// coefficiente. Il coefficiente è una costante (<see cref="FixedCoefficient"/>) oppure il nome di un
    /// parametro dell'elemento (<see cref="CoefficientParameter"/>) letto per-elemento dal chiamante.
    /// Speculare al template CSV di Pulse, adattato a CME (categoria per nome Revit).
    /// </summary>
    public sealed class DerivedRuleTemplate
    {
        /// <summary>Categoria Revit a cui la regola si applica, per nome (es. "Muri", "Pilastri strutturali"). "*" = tutte.</summary>
        public string BindCategory { get; set; } = "";

        /// <summary>Filtro opzionale sul nome del tipo (vuoto = qualsiasi tipo della categoria).</summary>
        public string BindType { get; set; } = "";

        public string Code { get; set; } = "";
        public string Um { get; set; } = "";

        /// <summary>Misura base su cui poggia la derivata: "volume" (mc) o "area" (mq).</summary>
        public string BaseMeasure { get; set; } = "volume";

        /// <summary>Nome del parametro sull'elemento che porta il coefficiente (incidenza). Vuoto = usa <see cref="FixedCoefficient"/>.</summary>
        public string CoefficientParameter { get; set; } = "";

        /// <summary>Coefficiente costante, se non letto da parametro. Null e senza parametro ⇒ voce flaggata "da completare a mano".</summary>
        public double? FixedCoefficient { get; set; }

        public string AntiDoubleCategory { get; set; } = "";
        public string AntiDoubleLabel { get; set; } = "";
        public bool OverestimateBias { get; set; }
        public string OverestimateNote { get; set; } = "";
        public string ShortDescription { get; set; } = "";
        public string ExtendedDescription { get; set; } = "";

        public DerivedBase ResolveBase() =>
            string.Equals(BaseMeasure?.Trim(), "area", StringComparison.OrdinalIgnoreCase)
                ? DerivedBase.Area
                : DerivedBase.Volume;
    }

    /// <summary>Contenitore di configurazione delle regole derivate (versionato, come le MappingRules).</summary>
    public sealed class DerivedRulesConfig
    {
        public int Version { get; set; } = 1;
        public List<DerivedRuleTemplate> Rules { get; set; } = new List<DerivedRuleTemplate>();

        /// <summary>I template applicabili a una data categoria/tipo Revit (match case-insensitive; "*" = tutte; BindType vuoto = qualsiasi).</summary>
        public IReadOnlyList<DerivedRuleTemplate> RulesFor(string category, string typeName)
        {
            var result = new List<DerivedRuleTemplate>();
            if (Rules == null) return result;
            foreach (var r in Rules)
            {
                if (r == null) continue;
                var catOk = r.BindCategory == "*"
                    || string.Equals(r.BindCategory?.Trim(), (category ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                if (!catOk) continue;
                var typeOk = string.IsNullOrWhiteSpace(r.BindType)
                    || string.Equals(r.BindType.Trim(), (typeName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                if (!typeOk) continue;
                result.Add(r);
            }
            return result;
        }
    }

    /// <summary>Trasforma un template (con coefficiente già risolto) nella regola pura del deriver.</summary>
    public static class DerivedRuleMapper
    {
        public static DerivedRule ToRule(DerivedRuleTemplate t, double? resolvedCoefficient) => new DerivedRule
        {
            Code = t.Code,
            Um = t.Um,
            Base = t.ResolveBase(),
            Coefficient = resolvedCoefficient,
            ShortDescription = t.ShortDescription,
            ExtendedDescription = t.ExtendedDescription,
            AntiDoubleCategory = t.AntiDoubleCategory,
            AntiDoubleLabel = t.AntiDoubleLabel,
            OverestimateBias = t.OverestimateBias,
            OverestimateNote = t.OverestimateNote,
        };
    }
}
