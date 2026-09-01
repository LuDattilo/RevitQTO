using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Computo.Extraction
{
    /// <summary>Quale quantità base misurata moltiplica una voce derivata: il volume (mc) o l'area (mq).
    /// Armatura e casseforme viaggiano sul volume; intonaco/tinteggio parità sull'area.</summary>
    public enum DerivedBase
    {
        Volume,
        Area,
    }

    /// <summary>
    /// Una regola di quantità derivata: una voce extra non modellata ma sempre fatturata con una misura
    /// base. Il coefficiente (incidenza kg/mc, rapporto mq/mc casseforme, 1 per la parità) è letto da un
    /// parametro dell'elemento e passato qui — null quando l'elemento non lo porta (allora la voce è
    /// flaggata, non bloccata). Revit-free: lo scanner la riempie dal modello e dalla sorgente regole.
    /// </summary>
    public sealed class DerivedRule
    {
        public string Code { get; set; } = "";
        public string Um { get; set; } = "";
        public DerivedBase Base { get; set; } = DerivedBase.Volume;
        /// <summary>Incidenza letta dall'elemento (kg/mc, mq/mc, o 1). Null ⇒ non trovata ⇒ voce "da completare a mano", mai fatturata con un numero inventato.</summary>
        public double? Coefficient { get; set; }
        public string MaterialName { get; set; } = "";
        public string ShortDescription { get; set; } = "";
        public string ExtendedDescription { get; set; } = "";
        /// <summary>La categoria modellata la cui presenza nello scope significa che questa lavorazione è già contata dal modello (es. OST_Rebar): il gate anti-doppio. Vuoto ⇒ nessun gate.</summary>
        public string AntiDoubleCategory { get; set; } = "";
        /// <summary>Etichetta umana per la nota del gate (es. "armatura").</summary>
        public string AntiDoubleLabel { get; set; } = "";
        /// <summary>True quando la quantità è un'approssimazione parametrica notoriamente in eccesso (casseforme ai giunti fra getti): il bias è dichiarato nella nota.</summary>
        public bool OverestimateBias { get; set; }
        public string OverestimateNote { get; set; } = "";
    }

    /// <summary>Un contributo di misura derivato. SI SOMMA alla riga base (UM/opera diversa), non la sostituisce mai.</summary>
    public sealed class DerivedContribution
    {
        public string Code { get; set; } = "";
        public string Um { get; set; } = "";
        public double Quantity { get; set; }
        public string MaterialName { get; set; } = "";
        public string ShortDescription { get; set; } = "";
        public string ExtendedDescription { get; set; } = "";
        /// <summary>False quando la quantità non è derivabile (coefficiente/base mancante, o soppressa dal gate anti-doppio). La voce è comunque restituita per essere visibile, marcata "da completare a mano" o "già modellata".</summary>
        public bool Computed { get; set; } = true;
        /// <summary>True quando il gate anti-doppio l'ha rimossa (lavorazione già modellata). Distinta da coefficiente mancante: questa riga NON va compilata a mano, deve restare fuori dal computo.</summary>
        public bool Suppressed { get; set; }
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// Deriva le voci extra che viaggiano su una misura base (armatura kg, casseforme mq, parità mq).
    /// Portato dal modulo Computo di Pulse, con le invarianti contabili:
    ///
    ///  - ADD, non REPLACE: una voce derivata SI SOMMA alla riga diretta/strato — misura una lavorazione
    ///    ortogonale a UM diversa (speculare a <see cref="LayerComputoExploder"/>, dove righe sullo stesso
    ///    asse si sostituiscono).
    ///  - Il coefficiente NON è nella geometria; mancante ⇒ FLAG "da completare a mano", mai inventato.
    ///  - Gate ANTI-DOPPIO: armatura già modellata (OST_Rebar in scope) ⇒ SOPPRIME la riga derivata,
    ///    visibilmente, così l'acciaio non è contato due volte.
    ///  - Un'approssimazione parametrica in eccesso DICHIARA il bias nella nota.
    ///
    /// Puro e unit-agnostic: quantità base in m³ / m², coefficienti nell'UM della voce.
    /// </summary>
    public static class DerivedComputoDeriver
    {
        public static List<DerivedContribution> Derive(double? baseVolumeM3, double? baseAreaM2,
            IReadOnlyList<DerivedRule> rules, ISet<string> modelledCategoriesInScope)
        {
            var result = new List<DerivedContribution>();
            if (rules == null) return result;

            foreach (var r in rules)
            {
                if (r == null) continue;

                var shortDesc = string.IsNullOrWhiteSpace(r.ShortDescription) ? r.MaterialName : r.ShortDescription.Trim();
                var extDesc = string.IsNullOrWhiteSpace(r.ExtendedDescription) ? shortDesc : r.ExtendedDescription.Trim();
                var c = new DerivedContribution
                {
                    Code = (r.Code ?? "").Trim(),
                    Um = (r.Um ?? "").Trim(),
                    MaterialName = r.MaterialName ?? "",
                    ShortDescription = shortDesc,
                    ExtendedDescription = extDesc,
                };

                // Gate ANTI-DOPPIO per primo: se la lavorazione è già modellata, il coefficiente è
                // irrilevante — la riga derivata resta fuori dal computo comunque, e NON deve chiedere di
                // essere completata a mano.
                if (!string.IsNullOrWhiteSpace(r.AntiDoubleCategory) && ScopeContains(modelledCategoriesInScope, r.AntiDoubleCategory))
                {
                    c.Computed = false;
                    c.Suppressed = true;
                    c.Quantity = 0;
                    var label = string.IsNullOrWhiteSpace(r.AntiDoubleLabel) ? "questa lavorazione" : r.AntiDoubleLabel.Trim();
                    c.Note = label + " già modellata (" + r.AntiDoubleCategory.Trim()
                        + "): non derivata per non contarla due volte";
                    result.Add(c);
                    continue;
                }

                // Il coefficiente non è nella geometria: mancante ⇒ flag, mai inventato.
                if (!r.Coefficient.HasValue)
                {
                    c.Computed = false;
                    c.Quantity = 0;
                    c.Note = "incidenza mancante: da completare a mano";
                    result.Add(c);
                    continue;
                }

                var baseQty = r.Base == DerivedBase.Volume ? baseVolumeM3 : baseAreaM2;
                if (!baseQty.HasValue)
                {
                    // Una regola la cui base non è stata misurata: non emettere uno 0 silenzioso che si legge come "misurato zero".
                    c.Computed = false;
                    c.Quantity = 0;
                    c.Note = "quantità base (" + (r.Base == DerivedBase.Volume ? "mc" : "mq") + ") assente: da completare a mano";
                    result.Add(c);
                    continue;
                }

                c.Quantity = baseQty.Value * r.Coefficient.Value;
                c.Computed = true;
                if (r.OverestimateBias)
                    c.Note = string.IsNullOrWhiteSpace(r.OverestimateNote)
                        ? "stima parametrica in eccesso ai giunti fra getti: verificare"
                        : r.OverestimateNote.Trim();
                result.Add(c);
            }

            return result;
        }

        private static bool ScopeContains(ISet<string> scope, string category)
        {
            if (scope == null || string.IsNullOrWhiteSpace(category)) return false;
            var needle = category.Trim();
            foreach (var s in scope)
                if (!string.IsNullOrWhiteSpace(s) && string.Equals(s.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
