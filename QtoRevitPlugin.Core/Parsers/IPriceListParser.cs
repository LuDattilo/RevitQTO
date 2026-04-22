using System.IO;

namespace QtoRevitPlugin.Parsers
{
    /// <summary>
    /// Contratto per i parser di listino (DCF XML ACCA, CSV, Excel).
    /// Un parser emette un <see cref="PriceListImportResult"/> con items + metadata + warning
    /// senza accedere al database: l'import/persistenza avviene nel repository layer.
    /// </summary>
    public interface IPriceListParser
    {
        /// <summary>
        /// True se il parser gestisce questa estensione/formato (case-insensitive).
        /// Usato dal factory per auto-selezione del parser giusto.
        /// </summary>
        bool CanHandle(string filePath);

        /// <summary>
        /// Parse da path filesystem. Shortcut che apre il FileStream e delega a <see cref="Parse(Stream,string)"/>.
        /// </summary>
        PriceListImportResult Parse(string filePath);

        /// <summary>
        /// Parse da Stream (principale, testabile senza IO). <paramref name="sourceName"/>
        /// viene usato per <see cref="Models.PriceList.Name"/> di default (es. nome file sans extension).
        /// </summary>
        PriceListImportResult Parse(Stream stream, string sourceName);
    }
}
