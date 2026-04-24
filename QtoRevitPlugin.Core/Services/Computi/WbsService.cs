using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public class WbsService : IWbsService
    {
        private readonly IQtoRepository _repo;
        public WbsService(IQtoRepository repo) => _repo = repo;

        public IReadOnlyList<WbsNode> GetAll(int documentId, string? kind = null) =>
            _repo.GetWbsNodes(documentId, kind);

        public WbsNode Add(int documentId, string kind, int? parentId, string desSintetica)
        {
            if (kind != "WbsCap" && kind != "WbsComputo")
                throw new DomainValidationException("WbsNode", "INVALID_KIND",
                    "Kind deve essere WbsCap o WbsComputo.");

            var all = _repo.GetWbsNodes(documentId, kind);
            WbsNode? parent = null;
            if (parentId.HasValue)
            {
                parent = all.FirstOrDefault(n => n.Id == parentId.Value);
                if (parent == null)
                    throw new DomainValidationException("WbsNode", "PARENT_NOT_FOUND",
                        $"Parent {parentId.Value} non trovato.");
            }

            var siblings = all.Where(n => n.ParentId == parentId).ToList();
            var nextOrder = siblings.Count == 0 ? 1 : siblings.Max(n => n.SortOrder) + 1;
            var level = parent == null ? 1 : parent.Level + 1;
            var codice = parent == null
                ? nextOrder.ToString()
                : $"{parent.Codice}.{nextOrder}";

            var node = new WbsNode
            {
                DocumentId = documentId,
                Kind = kind,
                Codice = codice,
                DesSintetica = desSintetica ?? "",
                ParentId = parentId,
                Level = level,
                SortOrder = nextOrder,
                IsActive = true
            };
            node.Id = _repo.InsertWbsNode(node);
            return node;
        }

        public void Update(WbsNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node.Id <= 0)
                throw new DomainValidationException("WbsNode", "NO_ID", "Id non valido.");
            _repo.UpdateWbsNode(node);
        }

        public void Delete(int nodeId) => _repo.DeleteWbsNode(nodeId);
    }
}
