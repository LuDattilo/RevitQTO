using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Computo
{
    public enum ManodoperaFindingSeverity { Error, Warning }

    public sealed class ManodoperaFinding
    {
        public string Code { get; set; } = "";
        public ManodoperaFindingSeverity Severity { get; set; }
        public string Voce { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>Una riga di output (un VCItem in modalità "riga", o una Tariffa aggregata in modalità "codice").</summary>
    public sealed class ManodoperaVoceIncidenza
    {
        public string Code { get; set; } = "";
        public string Unit { get; set; } = "";
        public double Quantity { get; set; }
        public double? UnitPrice { get; set; }
        public double? Importo { get; set; }
        public double? IncMdoPercent { get; set; }
        public double? IncidenzaManodopera { get; set; }
        public string DesRidotta { get; set; } = "";
        public string DesEstesa { get; set; } = "";
        public List<int> VociComputoIds { get; } = new List<int>();
    }

    public sealed class ManodoperaTotals
    {
        public double ImportoTotaleComputo { get; set; }
        public double IncidenzaManodoperaTotale { get; set; }
        /// <summary>Null quando ImportoTotaleComputo == 0 — mai NaN/Infinity da una divisione per zero.</summary>
        public double? IncidenzaManodoperaPercentSulTotale { get; set; }
    }

    public sealed class ManodoperaCoverage
    {
        public int VociTotali { get; set; }
        public int VociConMdoNota { get; set; }
        public int VociSenzaMdo { get; set; }
        public double ImportoEscluso { get; set; }
        public List<string> CodesSenzaMdo { get; } = new List<string>();
    }

    public sealed class ManodoperaResult
    {
        public List<ManodoperaVoceIncidenza> Righe { get; } = new List<ManodoperaVoceIncidenza>();
        public ManodoperaTotals Totals { get; set; } = new ManodoperaTotals();
        public ManodoperaCoverage Coverage { get; set; } = new ManodoperaCoverage();
        public List<ManodoperaFinding> Findings { get; } = new List<ManodoperaFinding>();
    }

    /// <summary>
    /// Calcolatore puro (Revit-free, deterministico): aggrega l'incidenza della manodopera
    /// (art. 41 cc.13-14 D.Lgs 36/2023) da un computo del modello Computi CME. Portato dal modulo
    /// Computo di Pulse. Non stampa nulla e non decide se la quota manodopera di un'offerta sia
    /// ribassabile — riporta solo l'incidenza come dichiarata dal prezzario (IncMDO).
    ///
    /// Adattamento CME: prezzo e IncMDO sono già numerici sul <see cref="PriceItem"/>. Una voce con
    /// FK prezzo orfano dà finding <c>idep_unresolved</c>. IncMDO ≤ 0 è trattata come "non dichiarata"
    /// (voce esclusa dal totale manodopera, non dal totale lavori) coerentemente con la disciplina H7.
    /// </summary>
    public static class ManodoperaAggregator
    {
        private sealed class GroupAcc
        {
            public string Code = "";
            public string Unit = "";
            public double Quantity;
            public double? UnitPriceDisplay;
            public double? IncMdoPercentDisplay;
            public string DesRidottaDisplay = "";
            public string DesEstesaDisplay = "";
            public double ImportoSum;
            public int ImportoKnownCount;
            public double IncidenzaSum;
            public int IncidenzaKnownCount;
            public readonly List<int> VociComputoIds = new List<int>();
        }

        public static ManodoperaResult Aggregate(
            IReadOnlyList<MeasurementRow> rows,
            IReadOnlyDictionary<int, PriceItem> priceItemsById,
            string groupBy)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (groupBy != "riga" && groupBy != "codice")
                throw new ArgumentException("groupBy deve essere 'riga' o 'codice'.", nameof(groupBy));
            var byRiga = groupBy == "riga";

            var result = new ManodoperaResult();
            var order = new List<string>();
            var groups = new Dictionary<string, GroupAcc>(StringComparer.Ordinal);

            var vociTotali = 0;
            var vociConMdoNota = 0;
            var importoEscluso = 0.0;
            var codesSenzaMdo = new List<string>();

            foreach (var vc in rows)
            {
                vociTotali++;

                string code;
                string unit = "";
                double? unitPrice = null;
                double? incMdoPercent = null;
                string desRidotta = "";
                string desEstesa = "";

                var resolved = priceItemsById != null
                    && priceItemsById.TryGetValue(vc.PriceItemId, out var p)
                    && p != null;

                if (!resolved)
                {
                    code = "?VC" + vc.Id;
                    result.Findings.Add(new ManodoperaFinding
                    {
                        Code = "idep_unresolved",
                        Severity = ManodoperaFindingSeverity.Error,
                        Voce = code,
                        Message = "La voce #" + vc.Id + " referenzia PriceItemId=" + vc.PriceItemId
                                  + ", che non corrisponde ad alcun PriceItem.",
                    });
                }
                else
                {
                    var pi = priceItemsById![vc.PriceItemId];
                    code = string.IsNullOrWhiteSpace(pi.Tariffa)
                        ? (string.IsNullOrWhiteSpace(pi.Code) ? "?VC" + vc.Id : pi.Code)
                        : pi.Tariffa!;
                    unit = pi.Unit ?? "";
                    desRidotta = pi.ShortDesc ?? "";
                    desEstesa = pi.Description ?? "";
                    unitPrice = pi.UnitPrice;
                    incMdoPercent = pi.IncMDO > 0 ? pi.IncMDO : (double?)null;

                    if (incMdoPercent == null)
                    {
                        result.Findings.Add(new ManodoperaFinding
                        {
                            Code = "missing_incmdo",
                            Severity = ManodoperaFindingSeverity.Warning,
                            Voce = code,
                            Message = "Il PriceItem '" + code + "' non dichiara IncMDO; la voce è esclusa "
                                      + "dal totale manodopera, non dal totale lavori.",
                        });
                        codesSenzaMdo.Add(code);
                    }
                    else
                    {
                        vociConMdoNota++;
                    }
                }

                if (vc.Quantita == 0.0)
                    result.Findings.Add(new ManodoperaFinding
                    {
                        Code = "not_measured",
                        Severity = ManodoperaFindingSeverity.Warning,
                        Voce = code,
                        Message = "La voce #" + vc.Id + " (" + code + ") ha quantità 0.",
                    });

                var quantity = vc.Quantita;
                double? importo = unitPrice.HasValue ? quantity * unitPrice.Value : (double?)null;
                double? incidenza = (importo.HasValue && incMdoPercent.HasValue)
                    ? importo.Value * (incMdoPercent.Value / 100.0)
                    : (double?)null;

                if (importo.HasValue && incMdoPercent == null)
                    importoEscluso += importo.Value;

                var key = byRiga ? "row:" + vc.Id : code;
                if (!groups.TryGetValue(key, out var acc))
                {
                    acc = new GroupAcc { Code = code, Unit = unit };
                    groups[key] = acc;
                    order.Add(key);
                }
                acc.Quantity += quantity;
                if (importo.HasValue) { acc.ImportoSum += importo.Value; acc.ImportoKnownCount++; }
                if (incidenza.HasValue) { acc.IncidenzaSum += incidenza.Value; acc.IncidenzaKnownCount++; }
                if (acc.UnitPriceDisplay == null) acc.UnitPriceDisplay = unitPrice;
                if (acc.IncMdoPercentDisplay == null) acc.IncMdoPercentDisplay = incMdoPercent;
                if (acc.DesRidottaDisplay.Length == 0) acc.DesRidottaDisplay = desRidotta;
                if (acc.DesEstesaDisplay.Length == 0) acc.DesEstesaDisplay = desEstesa;
                acc.VociComputoIds.Add(vc.Id);
            }

            foreach (var key in order)
            {
                var acc = groups[key];
                var row = new ManodoperaVoceIncidenza
                {
                    Code = acc.Code,
                    Unit = acc.Unit,
                    Quantity = acc.Quantity,
                    UnitPrice = acc.UnitPriceDisplay,
                    Importo = acc.ImportoKnownCount > 0 ? acc.ImportoSum : (double?)null,
                    IncMdoPercent = acc.IncMdoPercentDisplay,
                    IncidenzaManodopera = acc.IncidenzaKnownCount > 0 ? acc.IncidenzaSum : (double?)null,
                    DesRidotta = acc.DesRidottaDisplay,
                    DesEstesa = acc.DesEstesaDisplay,
                };
                row.VociComputoIds.AddRange(acc.VociComputoIds);
                result.Righe.Add(row);
            }

            var importoTotale = result.Righe.Where(r => r.Importo.HasValue).Sum(r => r.Importo!.Value);
            var incidenzaTotale = result.Righe.Where(r => r.IncidenzaManodopera.HasValue).Sum(r => r.IncidenzaManodopera!.Value);

            result.Totals.ImportoTotaleComputo = importoTotale;
            result.Totals.IncidenzaManodoperaTotale = incidenzaTotale;
            result.Totals.IncidenzaManodoperaPercentSulTotale =
                importoTotale == 0.0 ? (double?)null : incidenzaTotale / importoTotale * 100.0;

            result.Coverage.VociTotali = vociTotali;
            result.Coverage.VociConMdoNota = vociConMdoNota;
            result.Coverage.VociSenzaMdo = vociTotali - vociConMdoNota;
            result.Coverage.ImportoEscluso = importoEscluso;
            result.Coverage.CodesSenzaMdo.AddRange(codesSenzaMdo);

            return result;
        }
    }
}
