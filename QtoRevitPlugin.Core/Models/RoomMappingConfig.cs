namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Sorgente B: configura come una voce EP viene calcolata da un Room/Space Revit.
    /// La formula NCalc è name-based: usa i nomi dei parametri Revit (built-in nella lingua corrente,
    /// oppure Shared Params). Convenzione GPA: per portabilità IT/EN usare Shared Params inglesi
    /// (es. H_Controsoffitto, LargTotAperture) invece di built-in localizzati.
    ///
    /// Esempi:
    ///   "Area"                               → superficie Room (ROOM_AREA con BoundaryLocation=AtWallFinish)
    ///   "Area * 1.08"                        → area con 8% sfrido
    ///   "Perimeter - LargTotAperture"        → battiscopa (Shared Param per larghezza aperture)
    ///   "Perimeter * H_Controsoffitto"       → tinteggiatura pareti
    ///   "Volume"                             → volume Room per demolizioni
    /// </summary>
    public class RoomMappingConfig
    {
        public int Id { get; set; }
        public int SessionId { get; set; }

        public string EpCode { get; set; } = string.Empty;
        public string EpDescription { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;

        /// <summary>Formula NCalc libera — gli identificatori vengono risolti via LookupParameter sul Room corrente.</summary>
        public string Formula { get; set; } = string.Empty;

        /// <summary>Target: Rooms (OST_Rooms) oppure MEP Spaces (OST_MEPSpaces). Default: Rooms.</summary>
        public RoomTargetCategory TargetCategory { get; set; } = RoomTargetCategory.Rooms;

        /// <summary>Filtra Room per nome (opzionale — es. "Solo bagni"). Vuoto = tutti i Room validi (Area > 0).</summary>
        public string RoomNameFilter { get; set; } = string.Empty;
    }

    public enum RoomTargetCategory
    {
        Rooms,      // OST_Rooms (architettonico)
        MEPSpaces   // OST_MEPSpaces (impianti)
    }
}
