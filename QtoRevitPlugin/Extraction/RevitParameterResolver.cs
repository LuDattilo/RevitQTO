using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using QtoRevitPlugin.Formula;

namespace QtoRevitPlugin.Extraction
{
    /// <summary>
    /// Implementazione <see cref="IParameterResolver"/> ancorata a uno specifico
    /// <see cref="SpatialElement"/> (Room o MEPSpace). Risolve identificatori NCalc leggendo
    /// i parametri Revit e convertendo dalle "internal units" (feet) al SI (metri).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Identificatori speciali (case-insensitive, gestiti direttamente senza <c>LookupParameter</c>):
    /// <list type="bullet">
    ///   <item><c>Area</c> → area del Room in m² (da ft²)</item>
    ///   <item><c>Perimeter</c> → perimetro in m (da ft)</item>
    ///   <item><c>Volume</c> → volume in m³ (da ft³)</item>
    ///   <item><c>Height</c> → altezza dal Shared Param <c>QTO_AltezzaLocale</c> in metri,
    ///       fallback <see cref="RoomExtractor.DefaultRoomHeightMeters"/> se il SP non è presente</item>
    /// </list>
    /// </para>
    /// <para>
    /// Identificatori non speciali → <c>room.LookupParameter(name)</c>. Se trovato e di tipo
    /// <c>StorageType.Double</c>, il valore viene convertito da internal units a display units
    /// (m/m²/m³) a seconda del <c>SpecTypeId</c> ricavato da <c>Definition.GetDataType()</c>. Per parametri Integer ritorna il valore
    /// direttamente (no conversion). String/ElementId → null (non valutabili in formula numerica).
    /// </para>
    /// <para>
    /// Conversione Revit API 2025: usa <c>UnitUtils.ConvertFromInternalUnits(value, specTypeId)</c>
    /// con i <c>UnitTypeId</c> del SI (Meters/SquareMeters/CubicMeters). Per progetti con
    /// unità di progetto diverse, resta comunque valido perché il resolver opera in SI.
    /// </para>
    /// </remarks>
    public class RevitParameterResolver : IParameterResolver
    {
        // Conversioni dirette fallback (Revit internal units → SI). Usate se il spec-based API fallisce.
        private const double FtToM = 0.3048;
        private const double SqFtToSqM = 0.09290304;
        private const double CuFtToCuM = 0.02831685;

        private readonly SpatialElement _room;

        public RevitParameterResolver(SpatialElement room)
        {
            _room = room ?? throw new System.ArgumentNullException(nameof(room));
        }

        public double? TryResolve(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return null;

            // Identificatori speciali — case-insensitive per allinearsi a EvaluateOptions.IgnoreCase.
            if (string.Equals(parameterName, "Area", System.StringComparison.OrdinalIgnoreCase))
                return _room.Area * SqFtToSqM;

            if (string.Equals(parameterName, "Perimeter", System.StringComparison.OrdinalIgnoreCase))
                return GetPerimeterMeters();

            if (string.Equals(parameterName, "Volume", System.StringComparison.OrdinalIgnoreCase))
                return GetVolumeCubicMeters();

            if (string.Equals(parameterName, RoomExtractor.HeightVariableName, System.StringComparison.OrdinalIgnoreCase))
                return GetRoomHeight();

            // Fallback generico: LookupParameter (copre built-in localizzati, shared e project param).
            var param = _room.LookupParameter(parameterName);
            if (param == null || !param.HasValue) return null;

            return ReadAsDouble(param);
        }

        /// <summary>
        /// Perimeter non è esposto come property su <see cref="SpatialElement"/> (solo su <see cref="Room"/>).
        /// Su MEPSpace il parametro è leggibile via BuiltInParameter ROOM_PERIMETER.
        /// </summary>
        private double GetPerimeterMeters()
        {
            if (_room is Room room)
                return room.Perimeter * FtToM;

            // MEPSpace e altri SpatialElement: leggi via BuiltInParameter.
            var p = _room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
            if (p != null && p.StorageType == StorageType.Double)
                return p.AsDouble() * FtToM;

            return 0.0;
        }

        /// <summary>
        /// Volume non è esposto come property su <see cref="SpatialElement"/> base:
        /// <see cref="Room"/> ha <c>Volume</c>, <see cref="Mechanical.Space"/> ha <c>Volume</c> (proprio),
        /// ma per uniformità leggiamo via <see cref="BuiltInParameter.ROOM_VOLUME"/>.
        /// </summary>
        private double GetVolumeCubicMeters()
        {
            var p = _room.get_Parameter(BuiltInParameter.ROOM_VOLUME);
            if (p != null && p.StorageType == StorageType.Double)
                return p.AsDouble() * CuFtToCuM;

            // Fallback: se per qualche motivo il parametro non è presente ma il tipo specifico lo espone.
            if (_room is Room room)
                return room.Volume * CuFtToCuM;

            return 0.0;
        }

        /// <summary>
        /// Altezza del Room: legge shared param <see cref="RoomExtractor.AltezzaSharedParamName"/>
        /// (in metri — il SP viene definito come Length con display unit metri in SetupView).
        /// Fallback <see cref="RoomExtractor.DefaultRoomHeightMeters"/> (2.70m) se il SP non esiste o non ha valore.
        /// </summary>
        /// <remarks>
        /// Nota §I12: <c>Room.Height</c> NON è un BuiltInParameter, ed "Unbounded Height" è il
        /// delta level-top/bottom che non riflette l'altezza utile sotto controsoffitto. Per questo
        /// il plug-in usa un Shared Param dedicato (creato in SetupView Sprint 3).
        /// </remarks>
        private double GetRoomHeight()
        {
            var sp = _room.LookupParameter(RoomExtractor.AltezzaSharedParamName);
            if (sp != null && sp.HasValue && sp.StorageType == StorageType.Double)
            {
                // Il SP è di tipo Length: API ritorna feet internally → converti in m.
                return sp.AsDouble() * FtToM;
            }
            return RoomExtractor.DefaultRoomHeightMeters;
        }

        /// <summary>
        /// Legge un <see cref="Parameter"/> generico come double, convertendo da internal units
        /// a SI quando il parametro è di tipo Length/Area/Volume.
        /// </summary>
        private static double? ReadAsDouble(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.Double:
                    return ConvertDoubleToSi(p);
                case StorageType.Integer:
                    return p.AsInteger();
                default:
                    // String/ElementId non utilizzabili in formula numerica.
                    return null;
            }
        }

        /// <summary>
        /// Converte il valore double del parametro da internal units (feet-family) al SI.
        /// Usa <see cref="Definition.GetDataType()"/> per ottenere il <see cref="ForgeTypeId"/>
        /// del tipo di dato (SpecTypeId), confrontato con <see cref="SpecTypeId.Length"/>,
        /// <see cref="SpecTypeId.Area"/> e <see cref="SpecTypeId.Volume"/> (disponibile R2022+).
        /// In caso di errore ricade sui fattori di conversione fissi per Length/Area/Volume.
        /// </summary>
        private static double ConvertDoubleToSi(Parameter p)
        {
            try
            {
                var def = p.Definition;
                if (def != null)
                {
                    // GetDataType() ritorna un ForgeTypeId di categoria SpecTypeId (tipo di dato),
                    // NON un UnitTypeId. Confrontare con SpecTypeId.* per riconoscere lunghezze/aree/volumi.
                    var specType = def.GetDataType();
                    if (specType != null && !specType.Empty())
                    {
                        if (specType.Equals(SpecTypeId.Length))
                            return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Meters);

                        if (specType.Equals(SpecTypeId.Area))
                            return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.SquareMeters);

                        if (specType.Equals(SpecTypeId.Volume))
                            return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.CubicMeters);
                    }
                }
            }
            catch
            {
                // Fallback sotto.
            }

            // Fallback: nessuna conversione; meglio ritornare il valore grezzo che throw.
            return p.AsDouble();
        }
    }
}
