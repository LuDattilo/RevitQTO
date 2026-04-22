namespace QtoRevitPlugin.Theme
{
    /// <summary>
    /// Palette stati QTO — fonte di verità unica per colori cross-layer:
    /// - UI WPF (override grafici, legende HealthCheck)
    /// - FilterManager (§I11) per ParameterFilterElement + OverrideGraphicSettings
    /// - Export Excel (colori celle NP / Mancanti)
    /// - DockablePane Preview (badge stato elementi)
    ///
    /// Stati documentati in QTO-Implementazioni-v3.md §I5.
    /// Hex strings: netstandard2.0-compatible, convertiti in Color/Brush dall'addin.
    /// </summary>
    public static class StateColors
    {
        /// <summary>Verde — elemento computato (QtoAssignments.Count >= 1).</summary>
        public const string Computato = "#16A34A";

        /// <summary>Rosso — elemento senza assegnazioni EP.</summary>
        public const string Mancante = "#DC2626";

        /// <summary>Giallo/ocra — EP assegnato ma quantità calcolata = 0.</summary>
        public const string Parziale = "#CA8A04";

        /// <summary>Blu — elemento con 2+ assegnazioni EP (multi-EP).</summary>
        public const string MultiEP = "#2563EB";

        /// <summary>Arancione — elemento aggiunto dopo la prima computazione (ModelDiffLog).</summary>
        public const string Added = "#EA580C";

        /// <summary>Grigio — escluso manualmente o per regola filtro globale.</summary>
        public const string Escluso = "#78716C";

        /// <summary>Mappa il valore del Shared Param `QTO_Stato` al colore esadecimale.</summary>
        public static string ForQtoStato(string qtoStato)
        {
            return qtoStato switch
            {
                "COMPUTATO" => Computato,
                "PARZIALE" => Parziale,
                "NP" => Escluso,
                "ESCLUSO" => Escluso,
                _ => Mancante   // vuoto o sconosciuto → mancante
            };
        }

        /// <summary>RGB triplet (0–255) utile per OverrideGraphicSettings.SurfaceForegroundPatternColor.</summary>
        public static (byte R, byte G, byte B) ToRgb(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return (0, 0, 0);
            var s = hex.StartsWith("#") ? hex.Substring(1) : hex;
            if (s.Length != 6) return (0, 0, 0);

            return (
                System.Convert.ToByte(s.Substring(0, 2), 16),
                System.Convert.ToByte(s.Substring(2, 2), 16),
                System.Convert.ToByte(s.Substring(4, 2), 16)
            );
        }
    }
}
