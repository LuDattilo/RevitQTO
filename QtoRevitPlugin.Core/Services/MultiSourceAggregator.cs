using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Aggrega le quantità per <c>EpCode</c> combinando le tre sorgenti (§I13):
    /// <list type="bullet">
    ///   <item><b>A</b>: <see cref="QtoAssignment"/> da elementi Revit modellati</item>
    ///   <item><b>B</b>: <see cref="QtoAssignment"/> da Rooms/Spaces con NCalc (non gestito qui — arriva già in A)</item>
    ///   <item><b>C</b>: <see cref="ManualQuantityEntry"/> voci manuali svincolate dal modello</item>
    /// </list>
    ///
    /// <para><b>Regola di aggregazione</b>: sommativa senza priorità. Se lo stesso EpCode
    /// appare in entrambe le sorgenti, le quantità vengono sommate. Eventuali discrepanze
    /// sul prezzo unitario sono rilevate e segnalate (flag <see cref="AggregatedEntry.HasPriceConflict"/>)
    /// per permettere all'utente di verificare.</para>
    ///
    /// <para>Usato dal totalizzatore del computo e dal report export.</para>
    /// </summary>
    public static class MultiSourceAggregator
    {
        /// <summary>Tolleranza assoluta per considerare due prezzi "diversi" (€ 0.005).</summary>
        public const double PriceEpsilon = 0.005;

        /// <summary>
        /// Aggrega le due sorgenti per <c>EpCode</c>. Ignora assignment/manual item
        /// marcati come deleted o excluded.
        /// </summary>
        public static IReadOnlyList<AggregatedEntry> Aggregate(
            IReadOnlyList<QtoAssignment> assignments,
            IReadOnlyList<ManualQuantityEntry> manualItems)
        {
            var groups = new Dictionary<string, AggregatedEntry>(StringComparer.OrdinalIgnoreCase);

            // Sorgente A + B (da modello)
            if (assignments != null)
            {
                foreach (var a in assignments)
                {
                    if (string.IsNullOrEmpty(a.EpCode)) continue;
                    if (a.IsDeleted || a.IsExcluded) continue;
                    if (a.AuditStatus != AssignmentStatus.Active) continue;

                    var entry = GetOrCreate(groups, a.EpCode);
                    entry.QuantityFromModel += a.Quantity;
                    entry.TotalFromModel += a.Quantity * a.UnitPrice;

                    // Traccia il prezzo per detection conflitti
                    if (entry.UnitPriceModel == null)
                        entry.UnitPriceModel = a.UnitPrice;
                    else if (Math.Abs(entry.UnitPriceModel.Value - a.UnitPrice) > PriceEpsilon)
                        entry.ModelPriceNonUniform = true;

                    // Description dalla prima assegnazione (se vuota, resta vuota)
                    if (string.IsNullOrEmpty(entry.Description))
                        entry.Description = a.EpDescription;
                }
            }

            // Sorgente C (manual items)
            if (manualItems != null)
            {
                foreach (var m in manualItems)
                {
                    if (string.IsNullOrEmpty(m.EpCode)) continue;
                    if (m.IsDeleted) continue;

                    var entry = GetOrCreate(groups, m.EpCode);
                    entry.QuantityFromManual += m.Quantity;
                    entry.TotalFromManual += m.Total;

                    if (entry.UnitPriceManual == null)
                        entry.UnitPriceManual = m.UnitPrice;
                    else if (Math.Abs(entry.UnitPriceManual.Value - m.UnitPrice) > PriceEpsilon)
                        entry.ManualPriceNonUniform = true;

                    if (string.IsNullOrEmpty(entry.Description))
                        entry.Description = m.EpDescription;
                }
            }

            // Fase finale: compute conflict flags + total
            foreach (var e in groups.Values)
            {
                e.HasPriceConflict =
                    (e.UnitPriceModel != null && e.UnitPriceManual != null &&
                     Math.Abs(e.UnitPriceModel.Value - e.UnitPriceManual.Value) > PriceEpsilon)
                    || e.ModelPriceNonUniform
                    || e.ManualPriceNonUniform;
            }

            return groups.Values
                .OrderBy(e => e.EpCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static AggregatedEntry GetOrCreate(Dictionary<string, AggregatedEntry> map, string epCode)
        {
            if (!map.TryGetValue(epCode, out var e))
            {
                e = new AggregatedEntry { EpCode = epCode };
                map[epCode] = e;
            }
            return e;
        }
    }

    /// <summary>
    /// Entry aggregata di un <c>EpCode</c> sulle due sorgenti (modello + manuale).
    /// Usata dal totalizzatore e dai report export.
    /// </summary>
    public class AggregatedEntry
    {
        public string EpCode { get; set; } = "";
        public string Description { get; set; } = "";

        public double QuantityFromModel { get; set; }
        public double QuantityFromManual { get; set; }
        public double QuantityTotal => QuantityFromModel + QuantityFromManual;

        public double TotalFromModel { get; set; }
        public double TotalFromManual { get; set; }
        public double TotalAmount => TotalFromModel + TotalFromManual;

        /// <summary>Prezzo unitario osservato dalla sorgente modello (null se sorgente vuota).</summary>
        public double? UnitPriceModel { get; set; }

        /// <summary>Prezzo unitario osservato dalla sorgente manuale (null se sorgente vuota).</summary>
        public double? UnitPriceManual { get; set; }

        /// <summary>Nel gruppo modello esistono prezzi diversi per la stessa voce.</summary>
        public bool ModelPriceNonUniform { get; set; }

        /// <summary>Nel gruppo manuale esistono prezzi diversi per la stessa voce.</summary>
        public bool ManualPriceNonUniform { get; set; }

        /// <summary>
        /// True se il prezzo unitario modello differisce da quello manuale OLTRE la soglia
        /// <see cref="MultiSourceAggregator.PriceEpsilon"/>, oppure se uno dei due è non uniforme.
        /// Il report UI evidenzierà la riga in giallo per verifica.
        /// </summary>
        public bool HasPriceConflict { get; set; }

        /// <summary>Messaggio diagnostico human-readable in caso di conflict.</summary>
        public string? PriceConflictMessage
        {
            get
            {
                if (!HasPriceConflict) return null;
                if (UnitPriceModel != null && UnitPriceManual != null)
                {
                    return $"Prezzi non omogenei: {UnitPriceModel:N2} da listino / {UnitPriceManual:N2} manuale — verificare.";
                }
                if (ModelPriceNonUniform)
                    return "Il gruppo ha prezzi diversi per la stessa voce nella sorgente modello.";
                if (ManualPriceNonUniform)
                    return "Il gruppo ha prezzi diversi nella sorgente manuale.";
                return "Conflitto prezzo non caratterizzato.";
            }
        }
    }
}
