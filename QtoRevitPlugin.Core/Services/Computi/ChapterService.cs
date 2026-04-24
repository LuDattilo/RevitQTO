using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public class ChapterService : IChapterService
    {
        private readonly IQtoRepository _repo;
        public ChapterService(IQtoRepository repo) => _repo = repo;

        public IReadOnlyList<ChapterNode> GetAll(int documentId) =>
            _repo.GetChapterNodes(documentId);

        public ChapterNode AddSuperChapter(int documentId, string codice, string desSintetica)
            => AddNode(documentId, "SpCap", null, codice, desSintetica);

        public ChapterNode AddChapter(int documentId, int parentSpCapId, string codice, string desSintetica)
        {
            var parent = GetOrThrow(documentId, parentSpCapId);
            if (parent.Level != "SpCap")
                throw new DomainValidationException("ChapterNode", "PARENT_WRONG_LEVEL",
                    $"Parent deve avere Level=SpCap, trovato {parent.Level}.");
            return AddNode(documentId, "Cap", parentSpCapId, codice, desSintetica);
        }

        public ChapterNode AddSubChapter(int documentId, int parentCapId, string codice, string desSintetica)
        {
            var parent = GetOrThrow(documentId, parentCapId);
            if (parent.Level != "Cap")
                throw new DomainValidationException("ChapterNode", "PARENT_WRONG_LEVEL",
                    $"Parent deve avere Level=Cap, trovato {parent.Level}.");
            return AddNode(documentId, "SbCap", parentCapId, codice, desSintetica);
        }

        public void Update(ChapterNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node.Id <= 0)
                throw new DomainValidationException("ChapterNode", "NO_ID", "Id non valido.");
            _repo.UpdateChapterNode(node);
        }

        public void Delete(int nodeId) => _repo.DeleteChapterNode(nodeId);

        private ChapterNode AddNode(int documentId, string level, int? parentId, string codice, string desSintetica)
        {
            if (string.IsNullOrWhiteSpace(codice))
                throw new DomainValidationException("ChapterNode", "EMPTY_CODICE",
                    "Codice non può essere vuoto.");

            var siblings = _repo.GetChapterNodes(documentId)
                                .Where(n => n.Level == level && n.ParentId == parentId)
                                .ToList();
            if (siblings.Any(n => string.Equals(n.Codice, codice, StringComparison.OrdinalIgnoreCase)))
                throw new DomainValidationException("ChapterNode", "DUPLICATE_CODICE",
                    $"Codice '{codice}' già presente tra i {level} con stesso parent.");

            var sortOrder = siblings.Count == 0 ? 1 : siblings.Max(n => n.SortOrder) + 1;
            var node = new ChapterNode
            {
                DocumentId = documentId,
                Level = level,
                ParentId = parentId,
                Codice = codice,
                DesSintetica = desSintetica ?? "",
                SortOrder = sortOrder,
                IsActive = true
            };
            node.Id = _repo.InsertChapterNode(node);
            return node;
        }

        private ChapterNode GetOrThrow(int documentId, int nodeId)
        {
            var node = _repo.GetChapterNodes(documentId).FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
                throw new DomainValidationException("ChapterNode", "NOT_FOUND",
                    $"Nodo {nodeId} non trovato nel documento {documentId}.");
            return node;
        }
    }
}
