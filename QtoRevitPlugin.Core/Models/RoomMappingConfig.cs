namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Sorgente B: configura come una voce EP viene calcolata da un Room/Space Revit.
    /// La formula NCalc usa variabili: PERIMETER, AREA, HEIGHT, DOOR_WIDTH_SUM, DOOR_AREA_SUM, WINDOW_AREA_SUM.
    /// </summary>
    public class RoomMappingConfig
    {
        public int Id { get; set; }
        public int SessionId { get; set; }

        public string EpCode { get; set; } = string.Empty;
        public string EpDescription { get; set; } = string.Empty;

        /// <summary>Formula NCalc — es. "(PERIMETER - DOOR_WIDTH_SUM) * HEIGHT - WINDOW_AREA_SUM"</summary>
        public string Formula { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// Parametro di progetto usato come altezza locale.
        /// Se vuoto, usa il valore di DefaultHeightMeters.
        /// </summary>
        public string HeightParameterName { get; set; } = string.Empty;

        /// <summary>Altezza di fallback in metri se HeightParameterName non è trovato (default 2.70m).</summary>
        public double DefaultHeightMeters { get; set; } = 2.70;

        /// <summary>Filtra Room per nome (opzionale — es. "Solo bagni").</summary>
        public string RoomNameFilter { get; set; } = string.Empty;
    }
}
