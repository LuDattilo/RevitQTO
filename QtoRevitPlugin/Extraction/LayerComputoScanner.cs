using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using QtoRevitPlugin.Computo.Extraction;

namespace QtoRevitPlugin.Extraction
{
    /// <summary>
    /// Adattatore Revit → motori puri del Port #4 (estrazione avanzata). Legge dal modello Revit ciò che
    /// i motori Revit-free non possono conoscere — la struttura stratificata di un elemento, l'area
    /// faccia, lo stato di fase — e delega il calcolo a <see cref="LayerComputoExploder"/>,
    /// <see cref="DerivedComputoDeriver"/> e <see cref="ComputoPhaseFilter"/>.
    ///
    /// Read-only: nessuna transazione. Il codice/UM/densità di ogni strato sono letti da parametri sul
    /// <see cref="Material"/> il cui nome è configurabile (coerente con l'approccio "materialCodeParameter"
    /// di Pulse e con le Regole di Mappatura JSON di CME).
    /// </summary>
    public class LayerComputoScanner
    {
        private readonly Document _doc;

        public LayerComputoScanner(Document doc) =>
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        /// <summary>
        /// Legge gli strati della struttura composita del tipo di <paramref name="element"/> come
        /// <see cref="LayerInput"/> per l'exploder. Ritorna lista vuota se l'elemento non ha struttura
        /// composita (es. famiglia caricabile): in tal caso l'exploder userà il percorso whole-element.
        /// </summary>
        /// <param name="materialCodeParameter">Nome del parametro sul Material che porta il codice prezzo (obbligatorio).</param>
        /// <param name="materialUnitParameter">Nome del parametro sul Material che porta l'UM della voce (mq|mc|kg). Null ⇒ nessuna.</param>
        /// <param name="materialDensityParameter">Nome del parametro sul Material che porta la densità (kg/mc) per le voci a peso. Null ⇒ nessuna.</param>
        public List<LayerInput> ReadLayers(
            Element element,
            string materialCodeParameter,
            string? materialUnitParameter = null,
            string? materialDensityParameter = null)
        {
            var layers = new List<LayerInput>();
            if (element == null) return layers;

            // La struttura composita vive sul TIPO (WallType/FloorType/RoofType/CeilingType), non sull'istanza.
            if (!(_doc.GetElement(element.GetTypeId()) is HostObjAttributes typeElem)) return layers;
            var cs = typeElem.GetCompoundStructure();
            if (cs == null) return layers;

            foreach (var layer in cs.GetLayers())
            {
                var material = _doc.GetElement(layer.MaterialId) as Material;
                var materialName = material?.Name ?? "";

                var widthMm = UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Millimeters);
                layers.Add(new LayerInput
                {
                    Code = ReadMaterialString(material, materialCodeParameter),
                    Um = ReadMaterialString(material, materialUnitParameter) ?? "",
                    Density = ReadMaterialDouble(material, materialDensityParameter),
                    WidthMm = widthMm,
                    MaterialName = materialName,
                    // Descrizioni: se il Material non porta parametri descrittivi dedicati, l'exploder
                    // ripiega sul nome materiale (mai sul codice).
                    Description = null,
                    ExtendedDescription = null,
                });
            }

            return layers;
        }

        /// <summary>
        /// Esplode <paramref name="element"/> negli strati prezzati. Combina la lettura strati con l'area
        /// faccia (HOST_AREA_COMPUTED → m²) e delega al motore puro.
        /// </summary>
        public LayerExplosionResult Explode(
            Element element,
            string materialCodeParameter,
            string? materialUnitParameter = null,
            string? materialDensityParameter = null)
        {
            var faceAreaM2 = GetFaceAreaM2(element);
            var layers = ReadLayers(element, materialCodeParameter, materialUnitParameter, materialDensityParameter);
            return LayerComputoExploder.Explode(faceAreaM2, layers);
        }

        /// <summary>
        /// Stato di fase canonico dell'elemento nella fase <paramref name="phase"/>, nella forma attesa da
        /// <see cref="ComputoPhaseFilter"/> ("new"/"demolished"/"existing"/"temporary"/"past"/"future"/"none").
        /// </summary>
        public static string GetPhaseStatus(Element element, Phase phase)
        {
            if (element == null || phase == null) return "none";
            ElementOnPhaseStatus status;
            try { status = element.GetPhaseStatus(phase.Id); }
            catch { return "none"; }

            switch (status)
            {
                case ElementOnPhaseStatus.New: return "new";
                case ElementOnPhaseStatus.Demolished: return "demolished";
                case ElementOnPhaseStatus.Existing: return "existing";
                case ElementOnPhaseStatus.Temporary: return "temporary";
                case ElementOnPhaseStatus.Past: return "past";
                case ElementOnPhaseStatus.Future: return "future";
                default: return "none";
            }
        }

        // ---------------------------------------------------------------------

        private double GetFaceAreaM2(Element el)
        {
            var param = el?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (param == null || !param.HasValue) return 0.0;
            return UnitUtils.ConvertFromInternalUnits(param.AsDouble(), UnitTypeId.SquareMeters);
        }

        /// <summary>Volume base dell'elemento in m³ (HOST_VOLUME_COMPUTED), o null se non disponibile — per le voci derivate su volume.</summary>
        public static double? GetBaseVolumeM3(Element el)
        {
            var param = el?.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (param == null || !param.HasValue) return null;
            return UnitUtils.ConvertFromInternalUnits(param.AsDouble(), UnitTypeId.CubicMeters);
        }

        /// <summary>Area base dell'elemento in m² (HOST_AREA_COMPUTED), o null se non disponibile — per le voci derivate su area.</summary>
        public static double? GetBaseAreaM2(Element el)
        {
            var param = el?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (param == null || !param.HasValue) return null;
            return UnitUtils.ConvertFromInternalUnits(param.AsDouble(), UnitTypeId.SquareMeters);
        }

        /// <summary>Legge un parametro numerico (double) dall'elemento per nome, o null se assente/non numerico — per il coefficiente delle derivate.</summary>
        public static double? ReadElementDouble(Element el, string? paramName)
        {
            if (el == null || string.IsNullOrWhiteSpace(paramName)) return null;
            var p = el.LookupParameter(paramName);
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
            return p.AsDouble();
        }

        private static string? ReadMaterialString(Material? material, string? paramName)
        {
            if (material == null || string.IsNullOrWhiteSpace(paramName)) return null;
            var p = material.LookupParameter(paramName);
            if (p == null || !p.HasValue) return null;
            var s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private static double? ReadMaterialDouble(Material? material, string? paramName)
        {
            if (material == null || string.IsNullOrWhiteSpace(paramName)) return null;
            var p = material.LookupParameter(paramName);
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
            return p.AsDouble();
        }
    }
}
