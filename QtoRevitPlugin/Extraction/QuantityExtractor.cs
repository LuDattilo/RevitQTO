using Autodesk.Revit.DB;
using System;

namespace QtoRevitPlugin.Extraction
{
    /// <summary>
    /// Sorgente A: estrazione deterministica della quantità di un elemento Revit
    /// per un parametro geometrico specifico. Converte sempre dalle unità interne
    /// Revit (feet-based) a unità SI: m, m², m³. Il conteggio è sempre 1.0.
    ///
    /// Usato dal TagAssignmentHandler al click "CONFERMA E INSERISCI" della TaggingView
    /// per calcolare <see cref="Models.QtoAssignmentEntry.Quantity"/>.
    /// </summary>
    public class QuantityExtractor
    {
        /// <summary>Nomi canonici dei parametri geometrici supportati (per UI dropdown).</summary>
        public static readonly string[] SupportedParams = { "Area", "Volume", "Length", "Count" };

        /// <summary>
        /// Estrae la quantità di <paramref name="element"/> secondo il parametro geometrico scelto.
        /// Ritorna 0 e <paramref name="error"/> valorizzato se il parametro non è disponibile
        /// sulla categoria dell'elemento.
        /// </summary>
        public double Extract(Element element, string geometricParam, out string? error)
        {
            error = null;
            if (element == null) { error = "Elemento null."; return 0; }

            switch (geometricParam?.ToLowerInvariant())
            {
                case "area":
                    return GetArea(element, out error);
                case "volume":
                    return GetVolume(element, out error);
                case "length":
                case "lunghezza":
                    return GetLength(element, out error);
                case "count":
                case "conteggio":
                    return 1.0;
                default:
                    // Fallback: prova il LookupParameter come parametro custom
                    return GetCustomParameterAsDouble(element, geometricParam, out error);
            }
        }

        // ---------------------------------------------------------------------
        // Area (HOST_AREA_COMPUTED in feet² → m²)
        // ---------------------------------------------------------------------

        private double GetArea(Element el, out string? error)
        {
            error = null;
            var param = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (param == null || !param.HasValue)
            {
                error = $"Parametro 'Area' non disponibile per {el.Category?.Name ?? "(categoria?)"}.";
                return 0;
            }
            var valueInFt2 = param.AsDouble();
#if REVIT2025_OR_LATER
            return UnitUtils.ConvertFromInternalUnits(valueInFt2, UnitTypeId.SquareMeters);
#else
            return UnitUtils.ConvertFromInternalUnits(valueInFt2, DisplayUnitType.DUT_SQUARE_METERS);
#endif
        }

        // ---------------------------------------------------------------------
        // Volume (HOST_VOLUME_COMPUTED in feet³ → m³)
        // ---------------------------------------------------------------------

        private double GetVolume(Element el, out string? error)
        {
            error = null;
            var param = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (param == null || !param.HasValue)
            {
                error = $"Parametro 'Volume' non disponibile per {el.Category?.Name ?? "(categoria?)"}.";
                return 0;
            }
            var valueInFt3 = param.AsDouble();
#if REVIT2025_OR_LATER
            return UnitUtils.ConvertFromInternalUnits(valueInFt3, UnitTypeId.CubicMeters);
#else
            return UnitUtils.ConvertFromInternalUnits(valueInFt3, DisplayUnitType.DUT_CUBIC_METERS);
#endif
        }

        // ---------------------------------------------------------------------
        // Length (CURVE_ELEM_LENGTH in feet → m) — per elementi curve-based (muri, travi, linee)
        // ---------------------------------------------------------------------

        private double GetLength(Element el, out string? error)
        {
            error = null;
            var param = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (param == null || !param.HasValue)
            {
                // Fallback: WALL_ATTR_LENGTH_PARAM per muri
                param = el.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM);
            }
            if (param == null || !param.HasValue)
            {
                error = $"Parametro 'Length' non disponibile per {el.Category?.Name ?? "(categoria?)"}.";
                return 0;
            }
            var valueInFt = param.AsDouble();
#if REVIT2025_OR_LATER
            return UnitUtils.ConvertFromInternalUnits(valueInFt, UnitTypeId.Meters);
#else
            return UnitUtils.ConvertFromInternalUnits(valueInFt, DisplayUnitType.DUT_METERS);
#endif
        }

        // ---------------------------------------------------------------------
        // Custom param — tenta LookupParameter come double (utente abilita Shared Param specifico)
        // ---------------------------------------------------------------------

        private double GetCustomParameterAsDouble(Element el, string? paramName, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(paramName))
            {
                error = "Nome parametro geometrico mancante.";
                return 0;
            }
            var param = el.LookupParameter(paramName);
            if (param == null || !param.HasValue)
            {
                error = $"Parametro '{paramName}' non trovato o vuoto su {el.Category?.Name ?? "(categoria?)"}.";
                return 0;
            }
            try
            {
                var raw = param.AsDouble();
                // Non sappiamo l'unità: tentiamo conversione SI standard (Length/Area/Volume)
                // — se il parametro è di tipo diverso, restituisce il valore raw.
#if REVIT2025_OR_LATER
                var typeId = param.Definition?.GetDataType();
                if (typeId != null && typeId.Equals(SpecTypeId.Length))
                    return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Meters);
                if (typeId != null && typeId.Equals(SpecTypeId.Area))
                    return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.SquareMeters);
                if (typeId != null && typeId.Equals(SpecTypeId.Volume))
                    return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.CubicMeters);
#else
                // Revit 2024-: ParameterType enum
                var pt = param.Definition?.ParameterType ?? ParameterType.Invalid;
                if (pt == ParameterType.Length)
                    return UnitUtils.ConvertFromInternalUnits(raw, DisplayUnitType.DUT_METERS);
                if (pt == ParameterType.Area)
                    return UnitUtils.ConvertFromInternalUnits(raw, DisplayUnitType.DUT_SQUARE_METERS);
                if (pt == ParameterType.Volume)
                    return UnitUtils.ConvertFromInternalUnits(raw, DisplayUnitType.DUT_CUBIC_METERS);
#endif
                return raw;
            }
            catch (Exception ex)
            {
                error = $"Errore lettura parametro '{paramName}': {ex.Message}";
                return 0;
            }
        }

        // ---------------------------------------------------------------------
        // Suggerimenti per-categoria (dropdown UI)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Suggerisce il parametro geometrico più appropriato per una BuiltInCategory specifica.
        /// Usato dalla TaggingView per pre-selezionare il dropdown al cambio di categoria.
        /// </summary>
        public static string SuggestedParam(BuiltInCategory cat)
        {
            switch (cat)
            {
                case BuiltInCategory.OST_Floors:
                case BuiltInCategory.OST_Ceilings:
                case BuiltInCategory.OST_Roofs:
                case BuiltInCategory.OST_Walls:
                    return "Area";
                case BuiltInCategory.OST_StructuralFoundation:
                case BuiltInCategory.OST_StructuralColumns:
                case BuiltInCategory.OST_Columns:
                case BuiltInCategory.OST_Rooms:
                    return "Volume";
                case BuiltInCategory.OST_StructuralFraming:
                case BuiltInCategory.OST_Railings:
                    return "Length";
                case BuiltInCategory.OST_Doors:
                case BuiltInCategory.OST_Windows:
                case BuiltInCategory.OST_Furniture:
                case BuiltInCategory.OST_Casework:
                case BuiltInCategory.OST_PlumbingFixtures:
                case BuiltInCategory.OST_ElectricalFixtures:
                case BuiltInCategory.OST_MechanicalEquipment:
                    return "Count";
                default:
                    return "Count";
            }
        }
    }
}
