using System;
using System.Collections.Generic;
using QtoRevitPlugin.Data;

namespace QtoRevitPlugin.Services.Computi
{
    /// <summary>
    /// Un contributo di misura da assegnare, prodotto dall'estrazione avanzata (uno strato prezzato o
    /// una voce derivata). Uniforme fra le due sorgenti: entrambe danno codice + UM + quantità già
    /// calcolata + descrizioni + un flag di computabilità.
    /// </summary>
    public sealed class ComputoContribution
    {
        public int ElementId { get; set; }
        public string? Category { get; set; }
        public string? FamilyName { get; set; }
        public string Code { get; set; } = "";
        public string Um { get; set; } = "";
        /// <summary>Quantità finale già calcolata (mq/mc/kg). 0 quando <see cref="Computed"/> è false.</summary>
        public double Quantity { get; set; }
        public string Descrizione { get; set; } = "";
        /// <summary>False = quantità non derivabile (densità mancante, UM ignota, coefficiente assente): voce emessa "da completare a mano".</summary>
        public bool Computed { get; set; } = true;
        public string Note { get; set; } = "";
    }

    public sealed class ContributionApplyResult
    {
        public int VociCreate { get; set; }
        public int SubRowsAggiunte { get; set; }
        public int DaCompletareAMano { get; set; }
        /// <summary>Codici non risolvibili a un PriceItem del .cme: contributi NON assegnati (mai inventati).</summary>
        public List<string> CodiciNonRisolti { get; } = new List<string>();
    }

    /// <summary>
    /// Applica i contributi dell'estrazione avanzata al modello Computi: raggruppa per codice prezzo,
    /// crea una voce (VCItem) per codice e vi aggiunge una riga di misura (RGItem) per elemento. È il
    /// punto in cui l'esplosione strati (1:N) e le voci derivate (ADD) confluiscono nella stessa
    /// struttura del computo, con la stessa identità di riga (IDVV) del percorso di assegnazione diretto.
    ///
    /// La risoluzione codice→PriceItem del .cme è fornita dal chiamante (layer plugin: cerca nel .cme,
    /// poi nella UserLibrary con copy-on-use) tramite <paramref name="resolvePriceItemId"/>. Un codice
    /// non risolvibile NON viene assegnato: è riportato (disciplina H7), mai forzato.
    /// </summary>
    public class ComputoContributionApplier
    {
        private readonly IMeasurementService _measurements;

        public ComputoContributionApplier(IMeasurementService measurements) =>
            _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));

        public ContributionApplyResult Apply(
            int documentId,
            IReadOnlyList<ComputoContribution> contributions,
            Func<string, int?> resolvePriceItemId)
        {
            if (resolvePriceItemId == null) throw new ArgumentNullException(nameof(resolvePriceItemId));
            var result = new ContributionApplyResult();
            if (contributions == null || contributions.Count == 0) return result;

            // Raggruppa per codice preservando l'ordine di prima comparsa.
            var order = new List<string>();
            var byCode = new Dictionary<string, List<ComputoContribution>>(StringComparer.Ordinal);
            foreach (var c in contributions)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.Code)) continue;
                if (!byCode.TryGetValue(c.Code, out var list))
                {
                    byCode[c.Code] = list = new List<ComputoContribution>();
                    order.Add(c.Code);
                }
                list.Add(c);
            }

            foreach (var code in order)
            {
                var pid = resolvePriceItemId(code);
                if (pid == null || pid.Value <= 0)
                {
                    result.CodiciNonRisolti.Add(code);
                    continue;
                }

                // Una voce (VCItem) per codice, con una riga di misura (RGItem) per elemento.
                var row = _measurements.CreateRow(documentId, pid.Value);
                result.VociCreate++;

                foreach (var c in byCode[code])
                {
                    var descr = c.Computed
                        ? (string.IsNullOrWhiteSpace(c.Descrizione) ? c.Note : c.Descrizione)
                        : $"{c.Descrizione} · {c.Note}".Trim(' ', '·');

                    // Quantità già calcolata → PartiUguali (le altre dimensioni restano 1).
                    // Un contributo non computabile ha Quantity 0: la riga resta visibile con la nota.
                    _measurements.AddOrUpdateSubRow(
                        row.Id,
                        idvv: c.ElementId,
                        descrizione: descr,
                        partiUguali: c.Quantity,
                        category: c.Category,
                        familyName: c.FamilyName);

                    result.SubRowsAggiunte++;
                    if (!c.Computed) result.DaCompletareAMano++;
                }
            }

            return result;
        }
    }
}
