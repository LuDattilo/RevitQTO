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
            IReadOnlyList<ElementSnapshot> snapshots)
        {
            var result = new ModelDiffResult();

            // Passo 1: confronta snapshot noti → rileva Deleted + Modified.
            var knownUniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var categoriesSeen = new HashSet<BuiltInCategory>();

            foreach (var snap in snapshots)
            {
                knownUniqueIds.Add(snap.UniqueId);

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

                // Raccolgo le categorie OST "interessanti" (quelle dove l'utente ha
                // già assegnato voci) per limitare lo scan Added al passo 2.
                var bic = GetBuiltInCategory(elem);
                if (bic.HasValue) categoriesSeen.Add(bic.Value);

                var catOst = TryGetCategoryOst(elem);
                var rule = _mappingRules.GetRule(catOst);
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

            // Passo 2: scansione categorie già usate → rileva elementi Added (non in snapshots).
            // Si limita alle categorie realmente mappate per evitare di proporre ogni elemento
            // del modello come "aggiunto" (sarebbero migliaia di cross-reference indesiderati).
            foreach (var bic in categoriesSeen)
            {
                FilteredElementCollector collector;
                try
                {
                    collector = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType();
                }
                catch
                {
                    continue;  // categoria non valida/filtrabile → skip
                }

                foreach (var elem in collector)
                {
                    if (elem == null) continue;
                    if (string.IsNullOrEmpty(elem.UniqueId)) continue;
                    if (knownUniqueIds.Contains(elem.UniqueId)) continue;

                    result.Added.Add(elem);
                }
            }

            return result;
        }

        private static BuiltInCategory? GetBuiltInCategory(Element elem)
        {
            try
            {
                var cat = elem.Category;
                if (cat == null) return null;
#if REVIT2024_OR_EARLIER
                return (BuiltInCategory)cat.Id.IntegerValue;
#else
                return (BuiltInCategory)(int)cat.Id.Value;
#endif
            }
            catch { return null; }
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
            // Nessun fallback silenzioso su HOST_AREA_COMPUTED: se il parametro mappato
            // non esiste o non ha valore, il contributo è 0 (mantiene coerenza dell'hash
            // storico, ma evita collisioni semantiche tra elementi con Area uguale ma
            // parametri di mapping diversi). Il fallback precedente causava false-negative
            // nel diff (modifiche non rilevate su porte/finestre con stessa area).
            var result = new List<(string, double)>();
            foreach (var paramName in rule.HashParams)
            {
                double value = 0;
                var p = elem.LookupParameter(paramName);
                if (p == null)
                {
                    QtoRevitPlugin.Services.CrashLogger.Warn(
                        $"ExtractHashParams: parametro '{paramName}' non trovato su elemento {elem.UniqueId} (cat={elem.Category?.Name}). Hash contribution = 0.");
                }
                else if (p.HasValue)
                {
                    value = p.AsDouble();
                }
                result.Add((paramName, value));
            }
            return result;
        }

        private static double ExtractPrimaryQty(Element elem, MappingRule rule)
        {
            // Nessun fallback su HOST_AREA_COMPUTED — se il parametro canonico della
            // MappingRule non è disponibile, ritorna 0 e lascia all'utente riconciliare.
            var p = elem.LookupParameter(rule.DefaultParam);
            return p != null && p.HasValue ? p.AsDouble() : 0;
        }

        internal static string TryGetCategoryOst(Element elem)
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
