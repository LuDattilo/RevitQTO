using Autodesk.Revit.DB;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Services
{
    public class ModelDiffService
    {
        private readonly MappingRulesService _mappingRules;

        public ModelDiffService(MappingRulesService mappingRules)
        {
            _mappingRules = mappingRules;
        }

        public ModelDiffResult ComputeDiff(
            Document doc,
            IReadOnlyList<ElementSnapshot> snapshots,
            MappingRulesService rules)
        {
            var result = new ModelDiffResult();

            foreach (var snap in snapshots)
            {
                var elem = doc.GetElement(snap.UniqueId);

                if (elem == null)
                {
                    result.Deleted.Add(new DiffEntry
                    {
                        Snapshot = snap,
                        CurrentElement = null,
                        OldQty = snap.SnapshotQty,
                        NewQty = 0
                    });
                    continue;
                }

                var catOst = TryGetCategoryOst(elem);
                var rule = rules.GetRule(catOst);
                var paramValues = ExtractHashParams(elem, rule);
                var currentHash = ComputeHashStatic(snap.UniqueId, paramValues);

                if (!string.Equals(currentHash, snap.SnapshotHash, StringComparison.OrdinalIgnoreCase))
                {
                    var currentQty = ExtractPrimaryQty(elem, rule);
                    result.Modified.Add(new DiffEntry
                    {
                        Snapshot = snap,
                        CurrentElement = elem,
                        OldQty = snap.SnapshotQty,
                        NewQty = currentQty
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// SHA256 of uniqueId + paramValues, truncated to 12 uppercase hex chars.
        /// Delegates to <see cref="ElementHasher.ComputeHash"/> which lives in Core
        /// and is testable without Revit API.
        /// </summary>
        public static string ComputeHashStatic(string uniqueId, List<(string paramName, double value)> paramValues)
        {
            return ElementHasher.ComputeHash(uniqueId, paramValues);
        }

        private List<(string, double)> ExtractHashParams(Element elem, MappingRule rule)
        {
            var result = new List<(string, double)>();
            foreach (var paramName in rule.HashParams)
            {
                double value = 0;
                var p = elem.LookupParameter(paramName)
                    ?? elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (p != null && p.HasValue)
                    value = p.AsDouble();
                result.Add((paramName, value));
            }
            return result;
        }

        private static double ExtractPrimaryQty(Element elem, MappingRule rule)
        {
            var p = elem.LookupParameter(rule.DefaultParam)
                ?? elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            return p != null && p.HasValue ? p.AsDouble() : 0;
        }

        private static string TryGetCategoryOst(Element elem)
        {
            try
            {
                var cat = elem.Category;
                if (cat == null) return "";
#if REVIT2024_OR_EARLIER
                var bic = (BuiltInCategory)cat.Id.IntegerValue;
#else
                var bic = (BuiltInCategory)(int)cat.Id.Value;
#endif
                return bic.ToString(); // e.g. "OST_Walls"
            }
            catch
            {
                return "";
            }
        }
    }

    public class ModelDiffResult
    {
        public List<DiffEntry> Deleted { get; } = new List<DiffEntry>();
        public List<DiffEntry> Modified { get; } = new List<DiffEntry>();
        public List<Element> Added { get; } = new List<Element>();
    }

    public class DiffEntry
    {
        public ElementSnapshot Snapshot { get; set; } = null!;
        public Element? CurrentElement { get; set; }
        public double OldQty { get; set; }
        public double NewQty { get; set; }
        public string Delta => $"{NewQty - OldQty:+0.##;-0.##;0}";
    }
}
