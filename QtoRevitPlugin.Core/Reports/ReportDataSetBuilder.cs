using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Compone un ReportDataSet gerarchico a partire dai dati del IQtoRepository.
    /// Filtra per AuditStatus=Active (a meno che options.IncludeDeletedAndSuperseded sia true),
    /// raggruppa per ComputoChapterId (3 livelli), calcola subtotali e GrandTotal.
    /// </summary>
    public class ReportDataSetBuilder
    {
        private readonly IQtoRepository _repo;

        public ReportDataSetBuilder(IQtoRepository repo) => _repo = repo;

        public ReportDataSet Build(int sessionId, ReportExportOptions options)
        {
            var session = _repo.GetSession(sessionId)
                ?? throw new InvalidOperationException($"Sessione {sessionId} non trovata.");
            var chapters = _repo.GetComputoChapters(sessionId);
            var assignments = _repo.GetAssignments(sessionId)
                .Where(a => options.IncludeDeletedAndSuperseded || a.AuditStatus == AssignmentStatus.Active)
                .ToList();

            var dataset = new ReportDataSet
            {
                Session = session,
                Header = new ReportHeader
                {
                    Titolo = options.Titolo,
                    Committente = options.Committente,
                    DirettoreLavori = options.DirettoreLavori,
                    DataCreazione = DateTime.Now
                }
            };

            var orderCounter = 1;
            var assignmentsByChapter = assignments
                .Where(a => a.ComputoChapterId.HasValue)
                .GroupBy(a => a.ComputoChapterId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var roots = chapters.Where(c => c.Level == 1).OrderBy(c => c.SortOrder).ThenBy(c => c.Code).ToList();
            foreach (var root in roots)
            {
                var node = BuildNode(root, chapters, assignmentsByChapter, ref orderCounter);
                dataset.Chapters.Add(node);
            }

            dataset.UnchaperedEntries = assignments
                .Where(a => !a.ComputoChapterId.HasValue)
                .Select(a => BuildEntry(a, ref orderCounter, options.IncludeAuditFields))
                .ToList();

            dataset.GrandTotal = dataset.Chapters.Sum(c => c.Subtotal)
                               + dataset.UnchaperedEntries.Sum(e => e.Total);
            return dataset;
        }

        private ReportChapterNode BuildNode(
            ComputoChapter chapter,
            IReadOnlyList<ComputoChapter> allChapters,
            Dictionary<int, List<QtoAssignment>> assignmentsByChapter,
            ref int orderCounter)
        {
            var node = new ReportChapterNode { Chapter = chapter };

            // Children
            var children = allChapters
                .Where(c => c.ParentChapterId == chapter.Id)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Code);
            foreach (var child in children)
                node.Children.Add(BuildNode(child, allChapters, assignmentsByChapter, ref orderCounter));

            // Entries
            if (assignmentsByChapter.TryGetValue(chapter.Id, out var items))
            {
                foreach (var a in items.OrderBy(a => a.EpCode))
                    node.Entries.Add(BuildEntry(a, ref orderCounter, includeAudit: false));
            }

            node.Subtotal = node.Entries.Sum(e => e.Total)
                          + node.Children.Sum(c => c.Subtotal);
            return node;
        }

        private static ReportEntry BuildEntry(QtoAssignment a, ref int orderCounter, bool includeAudit)
        {
            return new ReportEntry
            {
                OrderIndex = orderCounter++,
                EpCode = a.EpCode,
                EpDescription = a.EpDescription ?? "",
                Unit = a.Unit ?? "",
                Quantity = a.Quantity,
                UnitPrice = (decimal)a.UnitPrice,
                Total = (decimal)a.Total,
                ElementId = a.ElementId.ToString(),
                Category = a.Category ?? "",
                Version = includeAudit ? a.Version : 0,
                CreatedBy = includeAudit ? (a.CreatedBy ?? "") : "",
                CreatedAt = includeAudit ? a.CreatedAt : default,
                AuditStatus = includeAudit ? a.AuditStatus.ToString() : ""
            };
        }
    }
}
