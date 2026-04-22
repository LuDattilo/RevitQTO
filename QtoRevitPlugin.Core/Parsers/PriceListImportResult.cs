using System.Collections.Generic;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Parsers
{
    /// <summary>
    /// Output di un parser di listino: metadata del listino, voci estratte, warning non bloccanti.
    /// Il parser NON tocca il database; la persistenza avviene in QtoRepository.InsertPriceList + InsertPriceItemsBatch.
    /// </summary>
    public class PriceListImportResult
    {
        /// <summary>Metadata listino (Name, Source, Region, Version, ImportedAt) — Id=0 finché non persistito.</summary>
        public PriceList Metadata { get; set; } = new PriceList();

        /// <summary>Voci estratte in ordine di apparizione. PriceListId=0 finché non persistito.</summary>
        public List<PriceItem> Items { get; set; } = new List<PriceItem>();

        /// <summary>
        /// Warning non bloccanti (es. riga con PrezzoUnitario non parsabile, attributo mancante, encoding ambiguo).
        /// Il parser tenta sempre di continuare anziché abortire.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Righe totali individuate nel file (anche quelle scartate per dati invalidi).</summary>
        public int TotalRowsDetected { get; set; }
    }
}
