using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Computo.Preflight
{
    /// <summary>
    /// Verifica pre-consegna del computo, portata dal <c>verify_computo_preflight</c> di Pulse e
    /// adattata al modello Computi CME. Read-only, non decide "consegna sì/no": aggrega verificatori
    /// puri in un report unico.
    ///
    /// Classi eseguibili sul modello CME attuale: coerenza interna, percentuali, coerenza UM interna.
    /// Le classi che richiedono concetti non ancora presenti in CME (scope Revit per la completezza,
    /// abaco per la riconciliazione UM esterna, voci derivate per il doppio-conteggio) sono riportate
    /// esplicitamente come "skipped" con motivo — mai omesse in silenzio (disciplina H7).
    /// </summary>
    public static class ComputoPreflightVerifier
    {
        private static readonly double[] StandardVatRates = { 4.0, 10.0, 22.0 };
        private const double MarkupTypicalMin = 13.0;
        private const double MarkupTypicalMax = 27.0;

        public static PreflightReport Verify(
            IReadOnlyList<MeasurementRow> rows,
            IReadOnlyDictionary<int, PriceItem> priceItemsById,
            double? markupPercent = null,
            double? defaultVatPercent = null)
        {
            rows ??= Array.Empty<MeasurementRow>();
            priceItemsById ??= new Dictionary<int, PriceItem>();

            var report = new PreflightReport();
            report.Classes.Add(VerifyInternalConsistency(rows, priceItemsById));
            report.Classes.Add(VerifyPercentages(rows, markupPercent, defaultVatPercent));
            report.Classes.Add(VerifyUnitConsistency(rows, priceItemsById));

            // Classi non applicabili al modello CME attuale — dichiarate, non nascoste.
            report.Classes.Add(PreflightClassResult.Skipped("completeness",
                "richiede lo scope del modello Revit (elementi in-scope senza voce): non disponibile a livello Core"));
            report.Classes.Add(PreflightClassResult.Skipped("double_count",
                "richiede le voci derivate (armatura/casseforme) — Port #4 estrazione avanzata"));
            report.Classes.Add(PreflightClassResult.Skipped("unit_reconciliation",
                "richiede un abaco Revit validato come testimone esterno: non ancora integrato in CME"));

            return report;
        }

        /// <summary>«Il computo si contraddice?» Voce senza codice, quantità ≤ 0.</summary>
        private static PreflightClassResult VerifyInternalConsistency(
            IReadOnlyList<MeasurementRow> rows, IReadOnlyDictionary<int, PriceItem> priceById)
        {
            var res = new PreflightClassResult { ClassName = "internal_consistency" };

            foreach (var vc in rows)
            {
                var hasPrice = priceById.TryGetValue(vc.PriceItemId, out var price) && price != null;
                var code = hasPrice
                    ? (string.IsNullOrWhiteSpace(price!.Tariffa) ? price.Code : price.Tariffa!)
                    : "";

                if (!hasPrice || string.IsNullOrWhiteSpace(code))
                    res.Findings.Add(new PreflightFinding
                    {
                        Code = "voce_without_code",
                        Severity = PreflightSeverity.Warning,
                        Message = "Voce di computo senza codice di prezziario (PriceItemId "
                                  + vc.PriceItemId.ToString(CultureInfo.InvariantCulture) + ").",
                    });

                if (vc.Quantita <= 0)
                    res.Findings.Add(new PreflightFinding
                    {
                        Code = "non_positive_quantity",
                        Severity = PreflightSeverity.Warning,
                        Voce = code,
                        Message = "Quantità della voce '" + code + "' ≤ 0 ("
                                  + vc.Quantita.ToString("0.##", CultureInfo.InvariantCulture) + ").",
                    });
            }

            return res;
        }

        /// <summary>Percentuali fuori standard sono legittime per legge → SOLO Warning, mai Error bloccante.</summary>
        private static PreflightClassResult VerifyPercentages(
            IReadOnlyList<MeasurementRow> rows, double? markupPercent, double? defaultVatPercent)
        {
            if (markupPercent == null && defaultVatPercent == null)
                return PreflightClassResult.Skipped("percentages",
                    "nessuna percentuale impostata (maggiorazione/IVA non ancora definite)");

            var res = new PreflightClassResult { ClassName = "percentages" };

            if (markupPercent == null)
                res.Findings.Add(new PreflightFinding
                {
                    Code = "markup_not_set",
                    Severity = PreflightSeverity.Warning,
                    Message = "Maggiorazione non impostata: il prezzo coincide col costo diretto (può essere intenzionale).",
                });
            else if (markupPercent.Value < MarkupTypicalMin || markupPercent.Value > MarkupTypicalMax)
                res.Findings.Add(new PreflightFinding
                {
                    Code = "markup_out_of_typical_band",
                    Severity = PreflightSeverity.Warning,
                    Message = "Maggiorazione " + Fmt(markupPercent.Value)
                              + "% fuori dalla fascia orientativa 13-27% (13-17% SG + 10% utile) — solo un promemoria.",
                });

            if (defaultVatPercent == null && rows.Count > 0)
                res.Findings.Add(new PreflightFinding
                {
                    Code = "vat_missing_default",
                    Severity = PreflightSeverity.Warning,
                    Message = "Nessuna IVA di default: il documento non è pronto per il Livello 4 (restano pronti i Livelli 1-3).",
                });

            if (defaultVatPercent != null && !StandardVatRates.Contains(defaultVatPercent.Value))
                res.Findings.Add(new PreflightFinding
                {
                    Code = "vat_out_of_standard_set",
                    Severity = PreflightSeverity.Warning,
                    Message = "IVA di default " + Fmt(defaultVatPercent.Value)
                              + "% non è tra le aliquote ordinarie 4/10/22 — la legge ammette altri casi.",
                });

            return res;
        }

        /// <summary>
        /// Coerenza UM interna: lo stesso codice risolto non deve comparire con unità di misura diverse
        /// fra le voci del computo (variante interna dell'UnitConsistencyVerifier di Pulse, che invece
        /// confronta con un abaco esterno).
        /// </summary>
        private static PreflightClassResult VerifyUnitConsistency(
            IReadOnlyList<MeasurementRow> rows, IReadOnlyDictionary<int, PriceItem> priceById)
        {
            var res = new PreflightClassResult { ClassName = "unit_consistency" };

            var unitsByCode = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var vc in rows)
            {
                if (!priceById.TryGetValue(vc.PriceItemId, out var price) || price == null) continue;
                var code = string.IsNullOrWhiteSpace(price.Tariffa) ? price.Code : price.Tariffa!;
                if (string.IsNullOrWhiteSpace(code)) continue;
                var unit = (price.Unit ?? "").Trim();
                if (!unitsByCode.TryGetValue(code, out var set))
                    unitsByCode[code] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(unit);
            }

            foreach (var kv in unitsByCode)
            {
                if (kv.Value.Count > 1)
                    res.Findings.Add(new PreflightFinding
                    {
                        Code = "unit_inconsistent",
                        Severity = PreflightSeverity.Error,
                        Voce = kv.Key,
                        Message = "Il codice '" + kv.Key + "' compare con unità di misura diverse ("
                                  + string.Join(", ", kv.Value.OrderBy(u => u)) + ").",
                    });
            }

            return res;
        }

        private static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
