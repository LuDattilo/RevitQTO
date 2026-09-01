using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QtoRevitPlugin.Computo
{
    /// <summary>
    /// Motore di calcolo monetario del computo, portato dal modulo Computo di Pulse.
    /// Quattro livelli: costi diretti → prezzo (maggiorato SG+utile) → totale imponibile
    /// (alias esplicito del prezzo) → totale con IVA.
    ///
    /// Deliberatamente Revit-free e senza dipendenze SQLite (POCO in, POCO out): riceve le righe
    /// prezzate già proiettate dal modello Computi (<see cref="ComputoTotalsRow"/>) e restituisce
    /// un <see cref="ComputoTotalsResult"/>. Tutta l'aritmetica interna resta in double NON
    /// arrotondato; l'arrotondamento a 2 decimali (commerciale, <see cref="MidpointRounding.AwayFromZero"/>)
    /// avviene UNA sola volta, quando ogni campo viene scritto nel risultato — mai tra un livello
    /// e il successivo. Disciplina "valore assente ≠ zero": un prezzo o un'aliquota mancante
    /// non viene mai assunto, ma segnalato (VatComputable / UnitPriceComputable = false).
    /// </summary>
    public static class ComputoTotals
    {
        /// <summary>
        /// Cap di default sulle liste diagnostiche <see cref="ComputoTotalsResult.RowsMissingVatRate"/>
        /// e <see cref="ComputoTotalsResult.RowsMissingUnitPrice"/>. Il chiamante che vuole la lista
        /// intera passa <see cref="int.MaxValue"/> all'overload con cap esplicito.
        /// </summary>
        public const int DefaultMissingRowsCap = 50;

        public static ComputoTotalsResult Compute(ComputoTotalsInput input) =>
            Compute(input, DefaultMissingRowsCap);

        public static ComputoTotalsResult Compute(ComputoTotalsInput input, int missingRowsCap)
        {
            input ??= new ComputoTotalsInput();

            var markup = input.MarkupPercent;
            var showMarkup = markup.HasValue && markup.Value != 0.0;
            var markupFactor = 1.0 + (markup ?? 0.0) / 100.0;

            var directTotal = 0.0;
            var priceTotal = 0.0;
            var vatTotal = 0.0;
            var allMissingVat = new List<string>();
            var allMissingUnitPrice = new List<string>();

            foreach (var row in input.Rows)
            {
                if (!row.UnitPriceResolved)
                    allMissingUnitPrice.Add(RowTag(row));

                var direct = row.Quantity * row.UnitPrice;
                directTotal += direct;
                var price = direct * markupFactor;
                priceTotal += price;

                var rate = row.VatPercentOverride ?? input.DefaultVatPercent;
                if (rate == null)
                {
                    allMissingVat.Add(RowTag(row));
                    continue;
                }
                vatTotal += price * rate.Value / 100.0;
            }

            var vatComputable = allMissingVat.Count == 0;
            var unitPriceComputable = allMissingUnitPrice.Count == 0;
            var priceTotalRounded = Round2(priceTotal);

            var result = new ComputoTotalsResult
            {
                DirectCostTotal = Round2(directTotal),
                MarkupPercentApplied = markup,
                MarkupAmount = Round2(priceTotal - directTotal),
                ShowMarkupLine = showMarkup,
                PriceTotal = priceTotalRounded,
                TaxableTotal = priceTotalRounded,             // alias esplicito del Livello 2
                VatComputable = vatComputable,
                VatAmount = vatComputable ? Round2(vatTotal) : 0.0,
                GrandTotalWithVat = vatComputable ? Round2(priceTotal + vatTotal) : 0.0,
                RowsMissingVatRateTotalCount = allMissingVat.Count,
                RowsMissingVatRateTruncated = allMissingVat.Count > missingRowsCap,
                UnitPriceComputable = unitPriceComputable,
                RowsMissingUnitPriceTotalCount = allMissingUnitPrice.Count,
                RowsMissingUnitPriceTruncated = allMissingUnitPrice.Count > missingRowsCap,
            };
            foreach (var m in allMissingVat.Take(missingRowsCap))
                result.RowsMissingVatRate.Add(m);
            foreach (var m in allMissingUnitPrice.Take(missingRowsCap))
                result.RowsMissingUnitPrice.Add(m);

            return result;
        }

        private static string RowTag(ComputoTotalsRow row) =>
            row.Code + "#" + row.ElementId.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Arrotondamento commerciale a 2 decimali, normalizzando lo zero negativo.
        /// <c>Math.Round(-1e-18, 2, AwayFromZero)</c> restituisce -0.0, che stampa "-0,00" con
        /// <c>ToString("N2")</c> su .NET Core 3.0+ — un segno meno spurio su una riga IVA di un
        /// documento legale. Un clamp <c>if (x &lt; 0.0)</c> NON lo intercetta (-0.0 &lt; 0.0 è
        /// false in IEEE-754). Normalizzando qui, l'unico punto dove avviene l'arrotondamento,
        /// ogni chiamante è protetto senza controlli extra.
        /// </summary>
        private static double Round2(double v)
        {
            var r = Math.Round(v, 2, MidpointRounding.AwayFromZero);
            return r == 0.0 ? 0.0 : r; // -0.0 == 0.0 è true; il literal 0.0 restituito è zero positivo.
        }
    }

    /// <summary>Input del motore totali: percentuali a livello documento + le righe prezzate.</summary>
    public sealed class ComputoTotalsInput
    {
        /// <summary>Maggiorazione (spese generali + utile d'impresa) in percentuale. Null o 0 = nessuna riga di maggiorazione.</summary>
        public double? MarkupPercent { get; set; }

        /// <summary>Aliquota IVA di default a livello documento; una riga può sovrascriverla via <see cref="ComputoTotalsRow.VatPercentOverride"/>.</summary>
        public double? DefaultVatPercent { get; set; }

        public List<ComputoTotalsRow> Rows { get; } = new List<ComputoTotalsRow>();
    }

    /// <summary>Una riga di misura prezzata (granularità RGItem), input aritmetico del motore.</summary>
    public sealed class ComputoTotalsRow
    {
        /// <summary>0 = riga non tracciabile a un elemento Revit (voce inserita a mano).</summary>
        public long ElementId { get; set; }

        public string Code { get; set; } = "";

        public double Quantity { get; set; }

        public double UnitPrice { get; set; }

        /// <summary>
        /// False quando il prezzo unitario manca o non è parsabile: <see cref="UnitPrice"/> resta 0
        /// solo come valore neutro aritmetico, non come prezzo reale del listino.
        /// </summary>
        public bool UnitPriceResolved { get; set; } = true;

        /// <summary>Override IVA per riga; vince sul default documento quando presente.</summary>
        public double? VatPercentOverride { get; set; }
    }

    /// <summary>Risultato del motore totali. Ogni omissione è dichiarata, mai silenziosa.</summary>
    public sealed class ComputoTotalsResult
    {
        public double DirectCostTotal { get; set; }               // Livello 1
        public double? MarkupPercentApplied { get; set; }
        public double MarkupAmount { get; set; }

        /// <summary>False quando la maggiorazione è null o 0: il rendering non stampa la riga "+ Spese generali e utile" (il campo resta comunque leggibile, mai omesso).</summary>
        public bool ShowMarkupLine { get; set; }

        public double PriceTotal { get; set; }                    // Livello 2
        public double TaxableTotal { get; set; }                  // Livello 3 (alias esplicito)

        /// <summary>False se almeno una riga non ha né override né default IVA: l'IVA totale NON si calcola (mai un'aliquota assunta). I livelli 1-3 restano validi.</summary>
        public bool VatComputable { get; set; }
        public double VatAmount { get; set; }                     // 0 se !VatComputable
        public double GrandTotalWithVat { get; set; }             // Livello 4, valido solo se VatComputable

        /// <summary>"Code#ElementId" per ogni riga senza aliquota risolvibile, troncata a cap.</summary>
        public List<string> RowsMissingVatRate { get; } = new List<string>();
        public bool RowsMissingVatRateTruncated { get; set; }
        public int RowsMissingVatRateTotalCount { get; set; }

        /// <summary>False se almeno una riga ha prezzo unitario mancante/non parsabile. I totali aritmetici restano leggibili ma non vanno trattati come importi completi.</summary>
        public bool UnitPriceComputable { get; set; } = true;

        /// <summary>"Code#ElementId" per ogni riga con prezzo unitario non risolto, troncata a cap.</summary>
        public List<string> RowsMissingUnitPrice { get; } = new List<string>();
        public bool RowsMissingUnitPriceTruncated { get; set; }
        public int RowsMissingUnitPriceTotalCount { get; set; }
    }
}
