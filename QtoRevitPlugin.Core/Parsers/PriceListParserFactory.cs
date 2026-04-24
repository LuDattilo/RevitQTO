using System.Linq;

namespace QtoRevitPlugin.Parsers
{
    /// <summary>
    /// Registro centralizzato dei parser di listino. Aggiungere un nuovo formato
    /// richiede solo inserire la classe in <see cref="All"/> — nessuna modifica ai VM.
    /// </summary>
    public static class PriceListParserFactory
    {
        public static readonly IPriceListParser[] All =
        {
            new DcfParser(),
            new ExcelParser(),
            new CsvParser(),
        };

        public static IPriceListParser? FindFor(string filePath) =>
            All.FirstOrDefault(p => p.CanHandle(filePath));
    }
}
