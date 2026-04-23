using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Implementazione di <see cref="IAssignmentService"/>. Orchestra la creazione
    /// di <see cref="QtoAssignment"/> a partire da <see cref="AssignmentRequest"/>
    /// e mantiene sincronizzati i KPI della <see cref="WorkSession"/>.
    ///
    /// Non conosce Revit API: riceve già target POCO dalla UI (che li raccoglie
    /// via <c>FilteredElementCollector</c>). Questo consente test completi con
    /// SQLite temporanea.
    /// </summary>
    public class AssignmentService : IAssignmentService
    {
        private readonly IQtoRepository _repo;

        public AssignmentService(IQtoRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        public AssignmentOutcome AssignEp(AssignmentRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.SessionId <= 0)
                throw new ArgumentException("SessionId invalido.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.EpCode))
                throw new ArgumentException("EpCode obbligatorio.", nameof(request));

            // Primo uso? (snapshot PRIMA di inserire)
            var usedBefore = _repo.GetUsedEpCodes(request.SessionId);
            bool isFirstUse = !usedBefore.Contains(request.EpCode);

            // Deduplica target su UniqueId (capita se la UI passa duplicati).
            // Salta target invalidi con motivazione diagnostica.
            var skipReasons = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var validTargets = new List<AssignmentTarget>();
            foreach (var t in request.Targets)
            {
                if (string.IsNullOrWhiteSpace(t.UniqueId))
                {
                    skipReasons.Add($"ElementId={t.ElementId}: UniqueId mancante");
                    continue;
                }
                if (t.Quantity <= 0.0)
                {
                    skipReasons.Add($"UniqueId={t.UniqueId}: quantità non positiva ({t.Quantity})");
                    continue;
                }
                if (!seen.Add(t.UniqueId))
                {
                    skipReasons.Add($"UniqueId={t.UniqueId}: duplicato nel batch");
                    continue;
                }
                validTargets.Add(t);
            }

            // Inserisce gli assignment (uno alla volta — l'API repo gestisce il commit).
            int inserted = 0;
            double batchAmount = 0.0;
            var now = DateTime.UtcNow;
            foreach (var t in validTargets)
            {
                var a = new QtoAssignment
                {
                    SessionId = request.SessionId,
                    ElementId = checked((int)t.ElementId), // int per schema esistente; overflow-safe
                    UniqueId = t.UniqueId,
                    Category = t.Category,
                    FamilyName = t.FamilyName,
                    PhaseCreated = t.PhaseCreated,
                    PhaseDemolished = t.PhaseDemolished,
                    EpCode = request.EpCode,
                    EpDescription = request.EpDescription,
                    Quantity = t.Quantity,
                    QuantityGross = t.Quantity,
                    Unit = request.Unit,
                    UnitPrice = request.UnitPrice,
                    RuleApplied = string.IsNullOrWhiteSpace(request.RuleApplied) ? "Manuale" : request.RuleApplied,
                    Source = QtoSource.RevitElement,
                    AssignedAt = now,
                    CreatedAt = now,
                    CreatedBy = request.CreatedBy,
                    Version = 1,
                    AuditStatus = AssignmentStatus.Active,
                };
                _repo.InsertAssignment(a);
                inserted++;
                batchAmount += t.Quantity * request.UnitPrice;
            }

            // Aggiorna KPI della sessione ricalcolando dai dati attivi nel DB.
            // Perché ricalcolare invece di incrementare: l'utente potrebbe aver
            // aggiunto/rimosso assegnazioni fuori dal servizio; ricalcolo = fonte di verità.
            var session = _repo.GetSession(request.SessionId);
            if (session != null)
            {
                var activeAssignments = _repo.GetAssignments(request.SessionId)
                    .Where(a => a.AuditStatus == AssignmentStatus.Active)
                    .ToList();

                // TotalElements = count di UniqueId distinti con almeno un'assegnazione attiva
                session.TotalElements = activeAssignments
                    .Select(a => a.UniqueId)
                    .Where(u => !string.IsNullOrEmpty(u))
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                // TaggedElements = alias di TotalElements oggi (ogni elemento assegnato è "taggato").
                // Questa equivalenza è vera finché non reintroduciamo il concetto di
                // "selezionato ma non ancora taggato" (Sprint futuri).
                session.TaggedElements = session.TotalElements;
                session.TotalAmount = activeAssignments.Sum(a => a.Quantity * a.UnitPrice);
                session.LastEpCode = request.EpCode;
                session.LastSavedAt = now;
                _repo.UpdateSession(session);
            }

            return new AssignmentOutcome(
                insertedCount: inserted,
                skippedCount: request.Targets.Count - inserted,
                isFirstUseOfEp: isFirstUse,
                totalAmount: batchAmount,
                skipReasons: skipReasons);
        }
    }
}
