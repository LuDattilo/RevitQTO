using System.Collections.Generic;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Suggerisce il <see cref="QuantityMode"/> di default per una categoria Revit
    /// identificata dal suo codice <c>OST_*</c> (language-independent).
    ///
    /// La mappa è conservativa: se la categoria non è conosciuta, fallback Count.
    /// L'utente può comunque override nel <c>PickEpDialog</c>.
    /// </summary>
    public static class QuantityModeDefaults
    {
        /// <summary>
        /// Mappa OST_* → QuantityMode. Basata su convenzioni italiane di
        /// computo metrico: muri/pavimenti in m², strutture massive in m³,
        /// travi/impianti lineari in m, infissi/apparecchi a corpo.
        /// </summary>
        private static readonly Dictionary<string, QuantityMode> Map =
            new Dictionary<string, QuantityMode>
        {
            // Architettonico
            { "OST_Walls",                QuantityMode.Area },
            { "OST_Floors",               QuantityMode.Area },
            { "OST_Ceilings",             QuantityMode.Area },
            { "OST_Roofs",                QuantityMode.Area },
            { "OST_CurtainWallPanels",    QuantityMode.Area },
            { "OST_CurtainWallMullions",  QuantityMode.Length },
            { "OST_Doors",                QuantityMode.Count },
            { "OST_Windows",              QuantityMode.Count },
            { "OST_Rooms",                QuantityMode.Area },
            { "OST_Areas",                QuantityMode.Area },
            { "OST_Stairs",               QuantityMode.Count },
            { "OST_Railings",             QuantityMode.Length },

            // Strutturale
            { "OST_StructuralFraming",    QuantityMode.Volume },
            { "OST_StructuralColumns",    QuantityMode.Volume },
            { "OST_StructuralFoundation", QuantityMode.Volume },
            { "OST_Rebar",                QuantityMode.Length },

            // MEP
            { "OST_DuctCurves",           QuantityMode.Length },
            { "OST_PipeCurves",           QuantityMode.Length },
            { "OST_Conduit",              QuantityMode.Length },
            { "OST_CableTray",            QuantityMode.Length },
            { "OST_MechanicalEquipment",  QuantityMode.Count },
            { "OST_PlumbingFixtures",     QuantityMode.Count },
            { "OST_LightingFixtures",     QuantityMode.Count },
            { "OST_ElectricalFixtures",   QuantityMode.Count },
            { "OST_ElectricalEquipment",  QuantityMode.Count },
            { "OST_DuctFitting",          QuantityMode.Count },
            { "OST_PipeFitting",          QuantityMode.Count },

            // Generici
            { "OST_GenericModel",         QuantityMode.Count },
            { "OST_FurnitureSystems",     QuantityMode.Count },
            { "OST_Furniture",            QuantityMode.Count },
            { "OST_CaseworkCategory",     QuantityMode.Count },
        };

        /// <summary>
        /// Restituisce il mode suggerito per <paramref name="builtInCategoryCode"/>
        /// (formato "OST_Walls" etc.). Fallback <see cref="QuantityMode.Count"/>
        /// per categorie non mappate — sempre sicuro (1.0 per istanza).
        /// </summary>
        public static QuantityMode GetDefault(string? builtInCategoryCode)
        {
            if (string.IsNullOrWhiteSpace(builtInCategoryCode))
                return QuantityMode.Count;
            return Map.TryGetValue(builtInCategoryCode!, out var mode) ? mode : QuantityMode.Count;
        }

        /// <summary>
        /// Nome parametro canonico per <see cref="Extraction.QuantityExtractor"/>
        /// (accetta Area / Volume / Length / Count). Mantiene l'API extractor
        /// disaccoppiata dall'enum UI.
        /// </summary>
        public static string ExtractorKey(QuantityMode mode) => mode switch
        {
            QuantityMode.Area => "Area",
            QuantityMode.Volume => "Volume",
            QuantityMode.Length => "Length",
            _ => "Count",
        };

        /// <summary>Etichetta user-facing in italiano per radio/label.</summary>
        public static string DisplayLabel(QuantityMode mode) => mode switch
        {
            QuantityMode.Area => "Area (m²)",
            QuantityMode.Volume => "Volume (m³)",
            QuantityMode.Length => "Lunghezza (m)",
            _ => "Conteggio (cad.)",
        };

        /// <summary>Abbreviazione user-facing (usata in anteprima "totale preventivo").</summary>
        public static string UnitAbbrev(QuantityMode mode) => mode switch
        {
            QuantityMode.Area => "m²",
            QuantityMode.Volume => "m³",
            QuantityMode.Length => "m",
            _ => "cad.",
        };
    }
}
