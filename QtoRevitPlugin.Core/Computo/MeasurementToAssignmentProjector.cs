using System;
using System.Collections.Generic;
using System.Globalization;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    /// <summary>
    /// Proietta il modello Computi canonico (<see cref="MeasurementRow"/> + <see cref="MeasurementSubRow"/>
    /// + <see cref="PriceItem"/>) in <see cref="QtoAssignment"/> in-memory (NON persistiti), così i
    /// consumatori del modello classico — in particolare l'analisi Health (anomaly detection z-score sulle
    /// quantità per codice EP) — leggono lo stesso binario su cui scrive <c>SelectionViewModel.ApplyEp</c>
    /// senza dover riscrivere il gateway.
    ///
    /// Granularità = RGItem (una assegnazione per sotto-riga), così l'anomaly detection sulle quantità
    /// per-elemento resta significativa. Limite dichiarato: il modello Computi non persiste categoria/
    /// famiglia per elemento, quindi il mismatch semantico AI (che le userebbe) degrada con grazia —
    /// le anomalie di quantità restano invece complete.
    /// </summary>
    public static class MeasurementToAssignmentProjector
    {
        public static List<QtoAssignment> Project(
            int sessionId,
            IReadOnlyList<MeasurementRow> rows,
            IReadOnlyDictionary<int, IReadOnlyList<MeasurementSubRow>> subRowsByRowId,
            IReadOnlyDictionary<int, PriceItem> priceItemsById)
        {
            var result = new List<QtoAssignment>();
            if (rows == null) return result;

            foreach (var row in rows)
            {
                priceItemsById.TryGetValue(row.PriceItemId, out var pi);
                var epCode = pi != null ? (string.IsNullOrWhiteSpace(pi.Tariffa) ? pi.Code : pi.Tariffa!) : "";
                var epDesc = pi?.Description ?? "";
                var unit = pi?.Unit ?? "";
                var unitPrice = pi?.UnitPrice ?? 0.0;

                if (subRowsByRowId == null
                    || !subRowsByRowId.TryGetValue(row.Id, out var subs)
                    || subs == null
                    || subs.Count == 0)
                {
                    // Voce senza sotto-righe: una sola assegnazione con la quantità aggregata della voce.
                    result.Add(Make(sessionId, row.Id, 0, epCode, epDesc, unit, unitPrice, row.Quantita));
                    continue;
                }

                foreach (var s in subs)
                    result.Add(Make(sessionId, row.Id, s.IDVV, epCode, epDesc, unit, unitPrice, s.Quantita));
            }

            return result;
        }

        private static QtoAssignment Make(
            int sessionId, int rowId, int idvv, string epCode, string epDesc,
            string unit, double unitPrice, double quantity)
        {
            var now = DateTime.UtcNow;
            var uniqueId = idvv > 0
                ? idvv.ToString(CultureInfo.InvariantCulture)
                : "manual:" + rowId.ToString(CultureInfo.InvariantCulture) + ":" + idvv.ToString(CultureInfo.InvariantCulture);
            return new QtoAssignment
            {
                SessionId = sessionId,
                ElementId = idvv > 0 ? idvv : 0,
                UniqueId = uniqueId,
                EpCode = epCode,
                EpDescription = epDesc,
                Quantity = quantity,
                QuantityGross = quantity,
                Unit = unit,
                UnitPrice = unitPrice,
                // Total è calcolato (Quantity * UnitPrice), non assegnabile.
                Source = QtoSource.RevitElement,
                AssignedAt = now,
                CreatedAt = now,
                Version = 1,
                AuditStatus = AssignmentStatus.Active,
            };
        }
    }
}
