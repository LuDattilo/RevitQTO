using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public class CategoryService : ICategoryService
    {
        private readonly IQtoRepository _repo;
        public CategoryService(IQtoRepository repo) => _repo = repo;

        public IReadOnlyList<CategoryNode> GetAll(int documentId) =>
            _repo.GetCategoryNodes(documentId);

        public CategoryNode AddSuperCategory(int documentId, string codice, string desSintetica)
            => AddNode(documentId, "SpCat", null, codice, desSintetica);

        public CategoryNode AddCategory(int documentId, int parentSpCatId, string codice, string desSintetica)
        {
            var parent = GetOrThrow(documentId, parentSpCatId);
            if (parent.Level != "SpCat")
                throw new DomainValidationException("CategoryNode", "PARENT_WRONG_LEVEL",
                    $"Parent deve avere Level=SpCat, trovato {parent.Level}.");
            return AddNode(documentId, "Cat", parentSpCatId, codice, desSintetica);
        }

        public CategoryNode AddSubCategory(int documentId, int parentCatId, string codice, string desSintetica)
        {
            var parent = GetOrThrow(documentId, parentCatId);
            if (parent.Level != "Cat")
                throw new DomainValidationException("CategoryNode", "PARENT_WRONG_LEVEL",
                    $"Parent deve avere Level=Cat, trovato {parent.Level}.");
            return AddNode(documentId, "SbCat", parentCatId, codice, desSintetica);
        }

        public void Update(CategoryNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node.Id <= 0)
                throw new DomainValidationException("CategoryNode", "NO_ID", "Id non valido.");
            _repo.UpdateCategoryNode(node);
        }

        public void Delete(int nodeId) => _repo.DeleteCategoryNode(nodeId);

        private CategoryNode AddNode(int documentId, string level, int? parentId, string codice, string desSintetica)
        {
            if (string.IsNullOrWhiteSpace(codice))
                throw new DomainValidationException("CategoryNode", "EMPTY_CODICE",
                    "Codice non può essere vuoto.");

            var siblings = _repo.GetCategoryNodes(documentId)
                                .Where(n => n.Level == level && n.ParentId == parentId)
                                .ToList();
            if (siblings.Any(n => string.Equals(n.Codice, codice, StringComparison.OrdinalIgnoreCase)))
                throw new DomainValidationException("CategoryNode", "DUPLICATE_CODICE",
                    $"Codice '{codice}' già presente tra i {level} con stesso parent.");

            var sortOrder = siblings.Count == 0 ? 1 : siblings.Max(n => n.SortOrder) + 1;
            var node = new CategoryNode
            {
                DocumentId = documentId,
                Level = level,
                ParentId = parentId,
                Codice = codice,
                DesSintetica = desSintetica ?? "",
                SortOrder = sortOrder,
                IsActive = true
            };
            node.Id = _repo.InsertCategoryNode(node);
            return node;
        }

        private CategoryNode GetOrThrow(int documentId, int nodeId)
        {
            var node = _repo.GetCategoryNodes(documentId).FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
                throw new DomainValidationException("CategoryNode", "NOT_FOUND",
                    $"Nodo {nodeId} non trovato nel documento {documentId}.");
            return node;
        }
    }
}
