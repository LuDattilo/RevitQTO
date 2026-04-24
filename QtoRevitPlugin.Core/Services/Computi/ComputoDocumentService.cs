using System;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public class ComputoDocumentService : IComputoDocumentService
    {
        private readonly IQtoRepository _repo;
        public ComputoDocumentService(IQtoRepository repo) => _repo = repo;

        public ComputoDocument GetOrCreate(int workSessionId, int defaultTipo = 1)
        {
            var existing = _repo.GetComputoDocumentBySession(workSessionId);
            if (existing != null) return existing;
            var now = DateTime.UtcNow;
            var doc = new ComputoDocument
            {
                WorkSessionId = workSessionId,
                TipoDocumento = defaultTipo,
                Versione = "5.04",
                Fgs = 2147614720L,
                Currency = "EUR",
                CreatedAt = now,
                UpdatedAt = now
            };
            doc.Id = _repo.InsertComputoDocument(doc);
            return doc;
        }

        public void Update(ComputoDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (doc.Id <= 0)
                throw new DomainValidationException("ComputoDocument", "NO_ID",
                    "Id non valido: il documento va prima inserito.");
            doc.UpdatedAt = DateTime.UtcNow;
            _repo.UpdateComputoDocument(doc);
        }
    }
}
