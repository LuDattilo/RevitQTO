using Autodesk.Revit.DB;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Plan C-6: estrae il valore numerico di misura da un Element Revit in base al QuantityMode.
    /// Ritorna valori in UNITÀ DI PROGETTO (es. m, m², m³) — converte da piedi internal via UnitUtils.
    /// </summary>
    public class RevitElementMeasurementReader
    {
        /// <summary>
        /// Ritorna il valore di misura per l'elemento nel modo richiesto.
        /// Count ritorna sempre 1. Null se il parametro non è presente / non valorizzato.
        /// </summary>
        public double? GetValue(Element element, QuantityMode mode)
        {
            if (element == null) return null;

            switch (mode)
            {
                case QuantityMode.Count:
                    return 1.0;

                case QuantityMode.Area:
                    return ReadDoubleParam(element, BuiltInParameter.HOST_AREA_COMPUTED, SpecTypeId.Area);

                case QuantityMode.Volume:
                    return ReadDoubleParam(element, BuiltInParameter.HOST_VOLUME_COMPUTED, SpecTypeId.Volume);

                case QuantityMode.Length:
                    // Prova INSTANCE_LENGTH_PARAM (instance), fallback su CURVE_ELEM_LENGTH (curve-based)
                    var inst = ReadDoubleParam(element, BuiltInParameter.INSTANCE_LENGTH_PARAM, SpecTypeId.Length);
                    if (inst.HasValue && inst.Value > 0) return inst;
                    return ReadDoubleParam(element, BuiltInParameter.CURVE_ELEM_LENGTH, SpecTypeId.Length);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Legge un parametro Double (quantità geometriche) e converte da unità interne Revit
        /// a unità di progetto. Ritorna null se non presente o non valorizzato.
        /// </summary>
        private static double? ReadDoubleParam(Element el, BuiltInParameter bip, ForgeTypeId specType)
        {
            var p = el.get_Parameter(bip);
            if (p == null || !p.HasValue) return null;

            double raw = p.AsDouble();
            try
            {
                var unitId = el.Document.GetUnits().GetFormatOptions(specType).GetUnitTypeId();
                return UnitUtils.ConvertFromInternalUnits(raw, unitId);
            }
            catch
            {
                return raw;
            }
        }
    }
}
