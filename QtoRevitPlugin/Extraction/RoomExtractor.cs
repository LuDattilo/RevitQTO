using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using QtoRevitPlugin.Formula;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Extraction
{
    /// <summary>
    /// Sorgente B (§I12): estrae quantità da Rooms (<see cref="BuiltInCategory.OST_Rooms"/>)
    /// o MEPSpaces (<see cref="BuiltInCategory.OST_MEPSpaces"/>) applicando una formula NCalc
    /// su parametri del singolo Room. La formula è definita in <see cref="RoomMappingConfig.Formula"/>
    /// e valutata tramite <see cref="FormulaEngine"/> + <see cref="RevitParameterResolver"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Workflow: <c>FilteredElementCollector → filtro categoria → filtro Area &gt; 0 (esclude
    /// Not Enclosed / Not Placed / Redundant) → filtro nome (opzionale) → per-Room evaluate → lista risultati.</c>
    /// </para>
    /// <para>
    /// Nota pattern Revit API: <c>OfCategory</c> usa un <c>ElementCategoryFilter</c> nativo (quick filter)
    /// e va applicato prima di <c>WhereElementIsNotElementType()</c> per performance (regola C7).
    /// </para>
    /// <para>
    /// Nota unità: <c>Room.Area</c> è in ft² (internal units Revit). Il filtro "Area &gt; 0" funziona
    /// comunque perché confronta con 0 — nessuna conversione necessaria al filtro. La conversione
    /// in m² avviene nel resolver al momento della valutazione.
    /// </para>
    /// </remarks>
    public class RoomExtractor
    {
        /// <summary>Nome convenzionale dell'identificatore "altezza" nelle formule.</summary>
        public const string HeightVariableName = "Height";

        /// <summary>
        /// Nome del Shared Parameter custom per l'altezza utile del Room.
        /// Alias verso <see cref="QtoConstants.SpQtoAltezzaLocale"/> — single source of truth:
        /// la definizione vive in <c>QtoParameterDefinitions.QtoAltezzaLocale</c> ed è creata
        /// dal <c>SharedParameterManager</c> a setup. Si assume definito come Length con display
        /// unit metri; il SP è bindato su OST_Rooms + OST_MEPSpaces.
        /// </summary>
        public const string AltezzaSharedParamName = QtoConstants.SpQtoAltezzaLocale;

        /// <summary>Altezza di default se il Shared Param non è presente sul Room (2.70 m — standard residenziale).</summary>
        public const double DefaultRoomHeightMeters = 2.70;

        private readonly FormulaEngine _formulaEngine;

        public RoomExtractor(FormulaEngine formulaEngine)
        {
            _formulaEngine = formulaEngine ?? throw new ArgumentNullException(nameof(formulaEngine));
        }

        /// <summary>
        /// Estrae tutti i Rooms/Spaces del documento che matchano la categoria + filtro nome
        /// configurato in <paramref name="config"/> e valuta la formula per ognuno.
        /// Restituisce una lista (anche vuota) di <see cref="RoomExtractionResult"/> — uno per Room.
        /// Non throw: errori per singolo Room sono catturati in <c>IsValid=false + Error</c>.
        /// </summary>
        public IReadOnlyList<RoomExtractionResult> Extract(Document doc, RoomMappingConfig config)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var results = new List<RoomExtractionResult>();

            var targetCategory = config.TargetCategory == RoomTargetCategory.MEPSpaces
                ? BuiltInCategory.OST_MEPSpaces
                : BuiltInCategory.OST_Rooms;

            // Quick filter (category) + slow filter (type exclusion). Ordine C7: quick first.
            var collector = new FilteredElementCollector(doc)
                .OfCategory(targetCategory)
                .WhereElementIsNotElementType();

            // SpatialElement è la base class di Room e Space: casting unificato.
            var rooms = collector
                .OfClass(typeof(SpatialElement))
                .Cast<SpatialElement>()
                .Where(IsValidRoom);

            // Filtro nome opzionale — case-insensitive Contains.
            if (!string.IsNullOrWhiteSpace(config.RoomNameFilter))
            {
                var filter = config.RoomNameFilter.Trim();
                rooms = rooms.Where(r => (r.Name ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (var room in rooms)
            {
                results.Add(EvaluateRoom(room, config));
            }

            return results;
        }

        /// <summary>
        /// Un Room è "valido" se <c>Area &gt; 0</c>: questo singolo predicato esclude
        /// Not Placed, Not Enclosed e Redundant (vedi §I12).
        /// </summary>
        private static bool IsValidRoom(SpatialElement r)
        {
            try
            {
                return r != null && r.Area > 0;
            }
            catch
            {
                // Room.Area può throw su rooms in stato inconsistente — trattali come invalidi.
                return false;
            }
        }

        private RoomExtractionResult EvaluateRoom(SpatialElement room, RoomMappingConfig config)
        {
            var result = new RoomExtractionResult
            {
                RoomId = room.Id,
                RoomUniqueId = room.UniqueId ?? string.Empty,
                RoomName = room.Name ?? string.Empty,
                RoomNumber = room.Number ?? string.Empty
            };

            try
            {
                var resolver = new RevitParameterResolver(room);
                var formulaResult = _formulaEngine.Evaluate(config.Formula, resolver);

                result.IsValid = formulaResult.IsValid;
                result.ComputedQuantity = formulaResult.IsValid ? formulaResult.Value : 0.0;
                result.Error = formulaResult.Error;
                result.UnresolvedParameters = formulaResult.UnresolvedIds ?? new List<string>();
            }
            catch (Exception ex)
            {
                // Safety net: qualsiasi exception imprevista → risultato invalid ma iteriamo sui prossimi Room.
                result.IsValid = false;
                result.Error = ex.Message;
            }

            return result;
        }
    }

    /// <summary>
    /// Esito valutazione Sorgente B per un singolo Room/Space.
    /// <see cref="ComputedQuantity"/> è in SI (m/m²/m³ a seconda di cosa produce la formula).
    /// </summary>
    public class RoomExtractionResult
    {
        public ElementId RoomId { get; set; } = ElementId.InvalidElementId;
        public string RoomUniqueId { get; set; } = string.Empty;
        public string RoomName { get; set; } = string.Empty;
        public string RoomNumber { get; set; } = string.Empty;

        public double ComputedQuantity { get; set; }
        public bool IsValid { get; set; }
        public string? Error { get; set; }
        public List<string> UnresolvedParameters { get; set; } = new List<string>();
    }
}
