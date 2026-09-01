using System.Collections.Generic;
using System.Globalization;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Computo
{
    /// <summary>
    /// Calcola la quota CAM (Criteri Ambientali Minimi) di un computo: per ogni voce
    /// (<see cref="MeasurementRow"/>) risolve il <see cref="PriceItem"/> collegato, calcola l'importo
    /// (quantità × prezzo unitario) e lo aggrega in bucket CAM / non-CAM / non-classificabile,
    /// classificando il codice con <see cref="PricelistCodeClassifier"/>. Portato dal modulo Computo
    /// di Pulse. Puro: nessun I/O, nessun Revit.
    ///
    /// Disciplina H7: codice non riconosciuto ≠ non-CAM (finisce fra i "non classificabili");
    /// una voce con FK prezzo orfano è esclusa dai totali (mai importo inventato).
    /// </summary>
    public static class CamQuotaCalculator
    {
        private const int MaxDiagnosticCodes = 20;

        public static CamQuotaResult Compute(
            IReadOnlyList<MeasurementRow> rows,
            IReadOnlyDictionary<int, PriceItem> priceItemsById)
        {
            var result = new CamQuotaResult();
            if (rows == null) return result;

            foreach (var vc in rows)
            {
                if (priceItemsById == null
                    || !priceItemsById.TryGetValue(vc.PriceItemId, out var price)
                    || price == null)
                {
                    if (result.OrphanMeasureCodes.Count < MaxDiagnosticCodes)
                        result.OrphanMeasureCodes.Add(
                            "PriceItemId=" + vc.PriceItemId.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                var importo = vc.Quantita * price.UnitPrice;
                result.TotalImporto += importo;
                result.ItemCount++;

                var code = string.IsNullOrWhiteSpace(price.Tariffa) ? price.Code : price.Tariffa!;
                PricelistCodeClassifier.Classify(code, out _, out var cam);
                if (cam == true)
                {
                    result.CamImporto += importo;
                    result.CamItemCount++;
                }
                else if (cam == false)
                {
                    result.NonCamImporto += importo;
                    result.NonCamItemCount++;
                }
                else // cam == null: non riconosciuto — "non so", MAI non-CAM
                {
                    result.UnclassifiedImporto += importo;
                    result.UnclassifiedItemCount++;
                    if (result.UnclassifiedCodes.Count < MaxDiagnosticCodes)
                        result.UnclassifiedCodes.Add(code ?? "");
                }
            }

            result.QuotaCamSuTotale = result.TotalImporto > 0
                ? (double?)(result.CamImporto / result.TotalImporto)
                : null;
            return result;
        }
    }

    /// <summary>Esito del calcolo CAM. <see cref="QuotaCamSuTotale"/> è null (non 0) quando non calcolabile.</summary>
    public sealed class CamQuotaResult
    {
        public int ItemCount { get; set; }
        public int CamItemCount { get; set; }
        public int NonCamItemCount { get; set; }
        public int UnclassifiedItemCount { get; set; }

        public double TotalImporto { get; set; }
        public double CamImporto { get; set; }
        public double NonCamImporto { get; set; }
        public double UnclassifiedImporto { get; set; }

        /// <summary>Quota CAM sul totale (0..1). Null se <see cref="TotalImporto"/> == 0, mai 0 per "non calcolabile".</summary>
        public double? QuotaCamSuTotale { get; set; }

        public List<string> UnclassifiedCodes { get; } = new List<string>();
        public List<string> MalformedPriceCodes { get; } = new List<string>();
        public List<string> OrphanMeasureCodes { get; } = new List<string>();
    }
}
