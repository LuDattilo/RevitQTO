using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Models;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Services
{
    public enum ParamOperator
    {
        Contains,
        Equals,
        NotEquals,
        GreaterThan,
        LessThan,
        GreaterOrEqual,
        LessOrEqual
    }

    /// <summary>
    /// Regola filtro parametrica usata in Selezione v2. I valori sono in unità
    /// DISPLAY del progetto (es. "0.30" per 30 cm). Il service converte in unità
    /// interne Revit prima del confronto per StorageType.Double.
    /// </summary>
    public class ParamFilterRule
    {
        public string ParameterName { get; set; } = "";
        public ParamOperator Operator { get; set; } = ParamOperator.Contains;
        public string Value { get; set; } = "";
    }

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
        /// Selezione v2: supporta anche filtri parametrici e limite massimo risultati.
        /// </summary>
        public IReadOnlyList<ElementRowInfo> FindElements(
            Document doc,
            BuiltInCategory category,
            string? nameQuery,
            int? phaseFilterId,
            IReadOnlyList<ParamFilterRule>? paramRules = null,
            int maxResults = 500)
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

            var activeRules = paramRules?
                .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName) && !string.IsNullOrWhiteSpace(r.Value))
                .ToList();

            var elements = collector.ToElements();
            var result = new List<ElementRowInfo>(elements.Count);
            var nameQueryLower = nameQuery?.Trim().ToLowerInvariant();

            foreach (var el in elements)
            {
                if (result.Count >= maxResults) break;

                var info = ToRowInfo(el, doc);

                // Filtro testuale opzionale su FamilyName/TypeName (case-insensitive contains)
                if (!string.IsNullOrEmpty(nameQueryLower))
                {
                    var haystack = (info.FamilyName + " " + info.TypeName).ToLowerInvariant();
                    if (!haystack.Contains(nameQueryLower)) continue;
                }

                // Filtri parametrici Selezione v2
                if (activeRules != null && activeRules.Count > 0)
                {
                    if (!PassesAllRules(el, activeRules)) continue;
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

        // -------------------------------------------------------------------
        // Selezione v2 — Filtri parametrici
        // -------------------------------------------------------------------

        private static bool PassesAllRules(Element el, List<ParamFilterRule> rules)
        {
            foreach (var rule in rules)
            {
                var param = ResolveParameter(el, rule.ParameterName);
                if (param == null || !param.HasValue) return false;
                if (!EvaluateRule(param, rule)) return false;
            }
            return true;
        }

        /// <summary>
        /// Cerca il parametro per nome: prima sull'istanza (LookupParameter + scan
        /// case-insensitive), poi sul tipo. LookupParameter di Revit API è case-sensitive,
        /// da qui il fallback con ScanParameters.
        /// </summary>
        private static Parameter? ResolveParameter(Element el, string name)
        {
            var p = el.LookupParameter(name);
            if (p != null) return p;

            p = ScanParameters(el.Parameters, name);
            if (p != null) return p;

            var typeEl = el.Document.GetElement(el.GetTypeId());
            if (typeEl != null)
            {
                p = typeEl.LookupParameter(name);
                if (p != null) return p;

                p = ScanParameters(typeEl.Parameters, name);
                if (p != null) return p;
            }

            return null;
        }

        private static Parameter? ScanParameters(ParameterSet pset, string name)
        {
            foreach (Parameter p in pset)
            {
                if (p.Definition?.Name != null &&
                    string.Equals(p.Definition.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        /// <summary>
        /// Valuta una regola su un parametro usando StorageType.
        /// Per StorageType.Double converte da unità interne Revit (piedi, piedi²)
        /// alle unità di progetto tramite UnitUtils.ConvertFromInternalUnits.
        /// </summary>
        private static bool EvaluateRule(Parameter param, ParamFilterRule rule)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    return EvalString(param.AsString() ?? "", rule);

                case StorageType.Double:
                {
                    if (!TryParseDouble(rule.Value, out double target)) return false;

                    double rawVal = param.AsDouble();
                    double displayVal = rawVal;
                    try
                    {
                        var unitId = param.GetUnitTypeId();
                        if (unitId != null && unitId != UnitTypeId.Custom)
                            displayVal = UnitUtils.ConvertFromInternalUnits(rawVal, unitId);
                    }
                    catch
                    {
                        displayVal = rawVal;
                    }
                    return EvalDouble(displayVal, rule.Operator, target);
                }

                case StorageType.Integer:
                {
                    if (!int.TryParse(rule.Value, out int target)) return false;
                    return EvalInt(param.AsInteger(), rule.Operator, target);
                }

                case StorageType.ElementId:
                {
                    var refEl = param.Element.Document.GetElement(param.AsElementId());
                    var name = refEl?.Name ?? param.AsValueString() ?? "";
                    return EvalString(name, rule);
                }

                default:
                    return true;
            }
        }

        private static bool EvalString(string val, ParamFilterRule rule) =>
            rule.Operator switch
            {
                ParamOperator.Contains  => val.IndexOf(rule.Value, System.StringComparison.OrdinalIgnoreCase) >= 0,
                ParamOperator.Equals    => string.Equals(val, rule.Value, System.StringComparison.OrdinalIgnoreCase),
                ParamOperator.NotEquals => !string.Equals(val, rule.Value, System.StringComparison.OrdinalIgnoreCase),
                _ => true
            };

        private static bool EvalDouble(double val, ParamOperator op, double target) =>
            op switch
            {
                ParamOperator.Equals         => System.Math.Abs(val - target) < 1e-6,
                ParamOperator.NotEquals      => System.Math.Abs(val - target) >= 1e-6,
                ParamOperator.GreaterThan    => val > target,
                ParamOperator.LessThan       => val < target,
                ParamOperator.GreaterOrEqual => val >= target,
                ParamOperator.LessOrEqual    => val <= target,
                _ => true
            };

        private static bool EvalInt(int val, ParamOperator op, int target) =>
            op switch
            {
                ParamOperator.Equals         => val == target,
                ParamOperator.NotEquals      => val != target,
                ParamOperator.GreaterThan    => val > target,
                ParamOperator.LessThan       => val < target,
                ParamOperator.GreaterOrEqual => val >= target,
                ParamOperator.LessOrEqual    => val <= target,
                _ => true
            };

        private static bool TryParseDouble(string s, out double val) =>
            double.TryParse(s, System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out val);
    }
}
