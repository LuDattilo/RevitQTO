using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Costruisce un <see cref="ReportDataSet"/> — la struttura consumata SENZA modifiche da tutti
    /// gli esportatori (XPWE/Excel/PDF/CSV) — a partire dal modello Computi canonico
    /// (<see cref="ComputoDocument"/> + <see cref="MeasurementRow"/> + <see cref="PriceItem"/> +
    /// <see cref="CategoryNode"/>), invece che dal modello classico QtoAssignments/ComputoChapters.
    ///
    /// È la fetta di riconciliazione (Fase 0) che allinea l'Export al binario su cui scrive
    /// <c>SelectionViewModel.ApplyEp</c>. Riusa <see cref="ReportChapterNode"/>/<see cref="ReportEntry"/>
    /// e sintetizza i <see cref="ComputoChapter"/> dai nodi Categoria del Computo (SpCat/Cat/SbCat),
    /// così l'albero PriMus (SuperCategorie/Categorie/SubCategorie) prodotto da <c>XpweExporter</c>
    /// resta identico. La logica di mapping è una funzione pura (testabile in-memory); il wrapper su
    /// <see cref="IQtoRepository"/> si limita a materializzare i dati.
    /// </summary>
    public class MeasurementReportDataSetBuilder
    {
        private readonly IQtoRepository _repo;

        public MeasurementReportDataSetBuilder(IQtoRepository repo) =>
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));

        public ReportDataSet Build(int sessionId, ReportExportOptions options)
        {
            var session = _repo.GetSession(sessionId)
                ?? throw new InvalidOperationException($"Sessione {sessionId} non trovata.");

            var header = BuildHeader(options);
            var doc = _repo.GetComputoDocumentBySession(sessionId);
            if (doc == null)
            {
                // Nessun documento Computi ancora creato: dataset vuoto ma valido.
                return new ReportDataSet { Session = session, Header = header };
            }

            var rows = _repo.GetMeasurementRows(doc.Id);
            var categories = _repo.GetCategoryNodes(doc.Id);

            var priceItemIds = rows.Select(r => r.PriceItemId).Where(id => id > 0).Distinct().ToList();
            var priceItemsById = priceItemIds.Count == 0
                ? new Dictionary<int, PriceItem>()
                : _repo.GetPriceItems(priceItemIds).ToDictionary(p => p.Id, p => p);

            return BuildDataSet(session, header, rows, priceItemsById, categories);
        }

        /// <summary>
        /// Mapping puro (nessun accesso a repository/Revit): materializza il <see cref="ReportDataSet"/>
        /// dai dati Computi già letti. Esposta per essere unit-testata in-memory.
        /// </summary>
        public static ReportDataSet BuildDataSet(
            WorkSession session,
            ReportHeader header,
            IReadOnlyList<MeasurementRow> rows,
            IReadOnlyDictionary<int, PriceItem> priceItemsById,
            IReadOnlyList<CategoryNode> categories)
        {
            var dataset = new ReportDataSet
            {
                Session = session,
                Header = header ?? new ReportHeader(),
            };

            // Sintetizza i ComputoChapter (uno per CategoryNode attivo) e la gerarchia.
            var activeCats = (categories ?? Array.Empty<CategoryNode>())
                .Where(c => c.IsActive)
                .ToList();
            var chapterById = activeCats.ToDictionary(c => c.Id, ToChapter);

            // Raggruppa le voci per il nodo categoria più profondo assegnato (SbCat > Cat > SpCat).
            var orderCounter = 1;
            var entriesByCat = new Dictionary<int, List<ReportEntry>>();
            var unchapered = new List<ReportEntry>();

            foreach (var row in rows.OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
            {
                priceItemsById.TryGetValue(row.PriceItemId, out var pi);
                var catId = DeepestCategory(row, chapterById);
                var categoryName = catId.HasValue && chapterById.TryGetValue(catId.Value, out var ch)
                    ? ch.Name
                    : "";
                var entry = BuildEntry(row, pi, categoryName, ref orderCounter);

                if (catId.HasValue)
                {
                    if (!entriesByCat.TryGetValue(catId.Value, out var list))
                        entriesByCat[catId.Value] = list = new List<ReportEntry>();
                    list.Add(entry);
                }
                else
                {
                    unchapered.Add(entry);
                }
            }

            // Costruisce l'albero (livello 1 = SpCat come radici).
            var roots = activeCats
                .Where(c => MapLevel(c.Level) == 1)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Codice)
                .ToList();
            foreach (var root in roots)
                dataset.Chapters.Add(BuildNode(root.Id, chapterById, activeCats, entriesByCat));

            dataset.UnchaperedEntries = unchapered;

            dataset.GrandTotal = dataset.Chapters.Sum(c => c.Subtotal)
                               + unchapered.Sum(e => e.Total);
            return dataset;
        }

        private static ReportChapterNode BuildNode(
            int catId,
            IReadOnlyDictionary<int, ComputoChapter> chapterById,
            IReadOnlyList<CategoryNode> allCats,
            IReadOnlyDictionary<int, List<ReportEntry>> entriesByCat)
        {
            var node = new ReportChapterNode { Chapter = chapterById[catId] };

            var children = allCats
                .Where(c => c.ParentId == catId)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Codice);
            foreach (var child in children)
                node.Children.Add(BuildNode(child.Id, chapterById, allCats, entriesByCat));

            if (entriesByCat.TryGetValue(catId, out var items))
                foreach (var e in items)
                    node.Entries.Add(e);

            node.Subtotal = node.Entries.Sum(e => e.Total) + node.Children.Sum(c => c.Subtotal);
            return node;
        }

        private static ReportEntry BuildEntry(MeasurementRow row, PriceItem? pi, string categoryName, ref int orderCounter)
        {
            var quantity = row.Quantita;
            var unitPrice = pi != null ? (decimal)pi.UnitPrice : 0m;
            return new ReportEntry
            {
                OrderIndex = orderCounter++,
                EpCode = pi != null ? (string.IsNullOrWhiteSpace(pi.Tariffa) ? pi.Code : pi.Tariffa!) : "",
                EpDescription = pi?.Description ?? "",
                Unit = pi?.Unit ?? "",
                Quantity = quantity,
                UnitPrice = unitPrice,
                Total = (decimal)quantity * unitPrice,
                ElementId = "",   // VCItem aggrega N elementi: nessun singolo ElementId a questo livello.
                Category = categoryName,
            };
        }

        /// <summary>Nodo categoria più profondo referenziato dalla voce, fra quelli realmente esistenti.</summary>
        private static int? DeepestCategory(MeasurementRow row, IReadOnlyDictionary<int, ComputoChapter> chapterById)
        {
            if (row.SbCatId.HasValue && chapterById.ContainsKey(row.SbCatId.Value)) return row.SbCatId;
            if (row.CatId.HasValue && chapterById.ContainsKey(row.CatId.Value)) return row.CatId;
            if (row.SpCatId.HasValue && chapterById.ContainsKey(row.SpCatId.Value)) return row.SpCatId;
            return null;
        }

        private static ComputoChapter ToChapter(CategoryNode c) => new ComputoChapter
        {
            Id = c.Id,
            ParentChapterId = c.ParentId,
            Code = c.Codice,
            Name = string.IsNullOrWhiteSpace(c.DesSintetica) ? c.Codice : c.DesSintetica,
            Level = MapLevel(c.Level),
            SortOrder = c.SortOrder,
        };

        private static int MapLevel(string level)
        {
            switch (level)
            {
                case "SpCat": return 1;
                case "Cat": return 2;
                case "SbCat": return 3;
                default: return 1;
            }
        }

        private static ReportHeader BuildHeader(ReportExportOptions o) => new ReportHeader
        {
            Titolo = o.Titolo,
            Committente = o.Committente,
            DirettoreLavori = o.DirettoreLavori,
            DataCreazione = DateTime.Now,
            Impresa = o.Impresa,
            RUP = o.RUP,
            DataComputo = o.DataComputo,
            DataPrezzi = o.DataPrezzi,
            RiferimentoPrezzario = o.RiferimentoPrezzario,
            CIG = o.CIG,
            CUP = o.CUP,
            RibassoPercentuale = o.RibassoPercentuale,
            Luogo = o.Luogo,
            Comune = o.Comune,
            Provincia = o.Provincia,
        };
    }
}
