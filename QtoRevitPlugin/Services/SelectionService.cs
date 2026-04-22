using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Models;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Servizio di selezione elementi Revit (§I3 SelectionView).
    /// Query con FilteredElementCollector e comandi di isola/nascondi sulla vista corrente.
    ///
    /// Regola C7 (performance): filtri rapidi PRIMA dei lenti →
    /// OfCategory + WhereElementIsNotElementType (rapidi) + WherePasses(ElementPhaseStatusFilter) (lento).
    /// </summary>
    public class SelectionService
    {
        /// <summary>
        /// Elenco "popolare" di categorie modellabili per il dropdown UI.
        /// Ordine by importanza tipica in un CME architettonico/strutturale.
        /// </summary>
        public static readonly (BuiltInCategory Bic, string Label)[] PopularCategories =
        {
            (BuiltInCategory.OST_Walls,               "Muri"),
            (BuiltInCategory.OST_Floors,              "Pavimenti"),
            (BuiltInCategory.OST_Ceilings,            "Controsoffitti"),
            (BuiltInCategory.OST_Roofs,               "Tetti"),
            (BuiltInCategory.OST_Doors,               "Porte"),
            (BuiltInCategory.OST_Windows,             "Finestre"),
            (BuiltInCategory.OST_Columns,             "Pilastri architettonici"),
            (BuiltInCategory.OST_StructuralColumns,   "Pilastri strutturali"),
            (BuiltInCategory.OST_StructuralFraming,   "Travi strutturali"),
            (BuiltInCategory.OST_StructuralFoundation,"Fondazioni"),
            (BuiltInCategory.OST_Stairs,              "Scale"),
            (BuiltInCategory.OST_Railings,            "Ringhiere"),
            (BuiltInCategory.OST_Rooms,               "Locali (Rooms)"),
            (BuiltInCategory.OST_GenericModel,        "Modelli generici"),
            (BuiltInCategory.OST_Furniture,           "Arredi"),
            (BuiltInCategory.OST_Casework,            "Arredi fissi"),
            (BuiltInCategory.OST_PlumbingFixtures,    "Idraulica"),
            (BuiltInCategory.OST_ElectricalFixtures,  "Elettrico"),
            (BuiltInCategory.OST_MechanicalEquipment, "Impianti meccanici"),
        };

        /// <summary>
        /// Trova elementi per categoria + (opzionale) filtro nome famiglia/tipo + fase.
        /// </summary>
        public IReadOnlyList<ElementRowInfo> FindElements(
            Document doc,
            BuiltInCategory category,
            string? nameQuery,
            int? phaseFilterId)
        {
            var collector = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType();

            if (phaseFilterId.HasValue && phaseFilterId.Value > 0)
            {
#if REVIT2025_OR_LATER
                var phaseId = new ElementId((long)phaseFilterId.Value);
#else
                var phaseId = new ElementId(phaseFilterId.Value);
#endif
                var phaseFilter = new ElementPhaseStatusFilter(
                    phaseId,
                    new List<ElementOnPhaseStatus>
                    {
                        ElementOnPhaseStatus.New,
                        ElementOnPhaseStatus.Existing
                    });
                collector = collector.WherePasses(phaseFilter);
            }

            var elements = collector.ToElements();
            var result = new List<ElementRowInfo>(elements.Count);
            var nameQueryLower = nameQuery?.Trim().ToLowerInvariant();

            foreach (var el in elements)
            {
                var info = ToRowInfo(el, doc);

                // Filtro testuale opzionale su FamilyName/TypeName (case-insensitive contains)
                if (!string.IsNullOrEmpty(nameQueryLower))
                {
                    var haystack = (info.FamilyName + " " + info.TypeName).ToLowerInvariant();
                    if (!haystack.Contains(nameQueryLower)) continue;
                }

                result.Add(info);
            }

            return result;
        }

        /// <summary>Isola temporaneamente gli elementi sulla vista attiva (toggle per-view).</summary>
        public void IsolateElements(UIDocument uidoc, IEnumerable<int> elementIds)
        {
            var view = uidoc.ActiveView;
            var ids = ToElementIdCollection(elementIds);
            if (ids.Count == 0) return;

            using var tx = new Transaction(uidoc.Document, "CME — Isola selezione");
            tx.Start();
            view.IsolateElementsTemporary(ids);
            tx.Commit();
        }

        /// <summary>Nasconde temporaneamente gli elementi sulla vista attiva.</summary>
        public void HideElements(UIDocument uidoc, IEnumerable<int> elementIds)
        {
            var view = uidoc.ActiveView;
            var ids = ToElementIdCollection(elementIds);
            if (ids.Count == 0) return;

            using var tx = new Transaction(uidoc.Document, "CME — Nascondi selezione");
            tx.Start();
            view.HideElementsTemporary(ids);
            tx.Commit();
        }

        /// <summary>Ripristina vista (rimuove isola/nascondi temporanei).</summary>
        public void ResetTemporaryView(UIDocument uidoc)
        {
            var view = uidoc.ActiveView;
            using var tx = new Transaction(uidoc.Document, "CME — Reset vista");
            tx.Start();
            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            tx.Commit();
        }

        /// <summary>
        /// Seleziona gli elementi dato un set di Id (toggle in Revit, utile per vedere
        /// in proprietà i parametri dell'elemento corrente).
        /// </summary>
        public void SelectInRevit(UIDocument uidoc, IEnumerable<int> elementIds)
        {
            var ids = ToElementIdCollection(elementIds);
            uidoc.Selection.SetElementIds(ids);
            if (ids.Count == 1) uidoc.ShowElements(ids);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static ElementRowInfo ToRowInfo(Element el, Document doc)
        {
            var familyName = string.Empty;
            var typeName = string.Empty;
            if (el is FamilyInstance fi)
            {
                familyName = fi.Symbol?.FamilyName ?? "";
                typeName = fi.Symbol?.Name ?? "";
            }
            else
            {
                var typeId = el.GetTypeId();
#if REVIT2025_OR_LATER
                if (typeId != null && typeId.Value != ElementId.InvalidElementId.Value)
#else
                if (typeId != null && typeId.IntegerValue != ElementId.InvalidElementId.IntegerValue)
#endif
                {
                    var type = doc.GetElement(typeId) as ElementType;
                    familyName = type?.FamilyName ?? "";
                    typeName = type?.Name ?? "";
                }
                if (string.IsNullOrEmpty(typeName)) typeName = el.Name;
            }

            string levelName = "";
            var levelId = el.LevelId;
#if REVIT2025_OR_LATER
            if (levelId != null && levelId.Value != ElementId.InvalidElementId.Value)
#else
            if (levelId != null && levelId.IntegerValue != ElementId.InvalidElementId.IntegerValue)
#endif
            {
                levelName = (doc.GetElement(levelId) as Level)?.Name ?? "";
            }

            string phaseCreated = "";
            string phaseDemolished = "";
            var pcParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
            var pdParam = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
            if (pcParam != null && pcParam.HasValue)
            {
                var phaseEl = doc.GetElement(pcParam.AsElementId());
                phaseCreated = phaseEl?.Name ?? "";
            }
            if (pdParam != null && pdParam.HasValue)
            {
                var phaseEl = doc.GetElement(pdParam.AsElementId());
                phaseDemolished = phaseEl?.Name ?? "";
            }

            return new ElementRowInfo
            {
#if REVIT2025_OR_LATER
                ElementId = (int)el.Id.Value,
#else
                ElementId = el.Id.IntegerValue,
#endif
                UniqueId = el.UniqueId,
                Category = el.Category?.Name ?? "",
                FamilyName = familyName,
                TypeName = typeName,
                LevelName = levelName,
                PhaseCreatedName = phaseCreated,
                PhaseDemolishedName = phaseDemolished
            };
        }

        private static ICollection<ElementId> ToElementIdCollection(IEnumerable<int> ids)
        {
#if REVIT2025_OR_LATER
            return ids.Select(i => new ElementId((long)i)).ToList();
#else
            return ids.Select(i => new ElementId(i)).ToList();
#endif
        }
    }
}
