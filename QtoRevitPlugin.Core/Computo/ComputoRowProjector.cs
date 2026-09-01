using System.Collections.Generic;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Computo
{
    /// <summary>
    /// Proietta il modello Computi di CME (<see cref="MeasurementRow"/> = VCItem, con il
    /// <see cref="PriceItem"/> collegato via <see cref="MeasurementRow.PriceItemId"/>) sulle righe
    /// aritmetiche del motore <see cref="ComputoTotals"/>. È il ponte fra la persistenza SQLite del
    /// <c>.cme</c> e il calcolo monetario puro: resta Revit-free e privo di dipendenze da repository
    /// (riceve una lookup dei prezzi già materializzata) così da essere interamente unit-testabile.
    ///
    /// Granularità di proiezione = VCItem (una riga di totali per voce di computo): la
    /// <see cref="MeasurementRow.Quantita"/> è già la cache di SUM dei RGItem, quindi non serve
    /// rileggere i sotto-record per il calcolo del totale.
    /// </summary>
    public static class ComputoRowProjector
    {
        /// <summary>
        /// Proietta le voci di computo in righe per il motore totali.
        /// </summary>
        /// <param name="rows">Le voci di computo (VCItem) del documento.</param>
        /// <param name="priceItemsById">
        /// Lookup dei <see cref="PriceItem"/> per Id (chiave = <see cref="PriceItem.Id"/>). Una voce
        /// il cui <see cref="MeasurementRow.PriceItemId"/> non è presente nella lookup viene marcata
        /// <see cref="ComputoTotalsRow.UnitPriceResolved"/> = false (prezzo non risolto, mai assunto 0).
        /// </param>
        /// <param name="vatOverridesByRowId">
        /// Facoltativo: override IVA per Id di <see cref="MeasurementRow"/> (aliquota in percentuale).
        /// Assente = si usa il default documento nel motore.
        /// </param>
        public static List<ComputoTotalsRow> Project(
            IEnumerable<MeasurementRow> rows,
            IReadOnlyDictionary<int, PriceItem> priceItemsById,
            IReadOnlyDictionary<int, double>? vatOverridesByRowId = null)
        {
            var result = new List<ComputoTotalsRow>();
            if (rows == null) return result;

            foreach (var row in rows)
            {
                PriceItem? pi = null;
                var resolved = priceItemsById != null
                    && priceItemsById.TryGetValue(row.PriceItemId, out pi)
                    && pi != null;

                double? vat = null;
                if (vatOverridesByRowId != null && vatOverridesByRowId.TryGetValue(row.Id, out var v))
                    vat = v;

                result.Add(new ComputoTotalsRow
                {
                    // A livello VCItem non esiste un singolo ElementId Revit (la voce aggrega N
                    // elementi): si usa l'Id della MeasurementRow come disambiguatore stabile per le
                    // liste diagnostiche del motore ("Code#Id"), non come id di elemento Revit.
                    ElementId = row.Id,
                    Code = resolved ? ResolveCode(pi!) : string.Empty,
                    Quantity = row.Quantita,
                    UnitPrice = resolved ? pi!.UnitPrice : 0.0,
                    UnitPriceResolved = resolved,
                    VatPercentOverride = vat,
                });
            }

            return result;
        }

        /// <summary>Codice PriMus della voce: <see cref="PriceItem.Tariffa"/> se valorizzata, altrimenti <see cref="PriceItem.Code"/>.</summary>
        private static string ResolveCode(PriceItem pi) =>
            string.IsNullOrWhiteSpace(pi.Tariffa) ? pi.Code : pi.Tariffa!;
    }
}
