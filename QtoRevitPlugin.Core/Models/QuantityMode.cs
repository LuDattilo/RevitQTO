namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Parametro geometrico scelto dall'utente per calcolare la quantità di ogni
    /// istanza Revit durante il tagging EP→Element (Sprint UI-5).
    ///
    /// Il nome del parametro stringa passato a <c>QuantityExtractor.Extract</c>
    /// è derivato da <see cref="QuantityModeInfo.ExtractorKey(QuantityMode)"/>.
    /// </summary>
    public enum QuantityMode
    {
        /// <summary>1.0 per istanza. Default sicuro per categorie discrete (infissi, apparecchi).</summary>
        Count = 0,

        /// <summary>Superficie (m²). Default per muri, pavimenti, controsoffitti.</summary>
        Area = 1,

        /// <summary>Volume (m³). Default per strutture massive.</summary>
        Volume = 2,

        /// <summary>Lunghezza (m). Default per travi e impianti lineari.</summary>
        Length = 3
    }
}
