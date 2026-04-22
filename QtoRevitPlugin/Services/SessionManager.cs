using Autodesk.Revit.DB;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Orchestra il ciclo di vita di una sessione di lavoro QTO per un documento Revit.
    /// Tiene aperto il QtoRepository (una connessione per sessione) e aggiorna lo snapshot
    /// in DB ad ogni operazione. Non chiama mai la Revit API dal proprio codice interno:
    /// il Document viene passato come parametro solo per leggere project path/name.
    /// </summary>
    public class SessionManager : IDisposable
    {
        private QtoRepository? _repository;
        private WorkSession? _activeSession;
        private bool _disposed;

        public WorkSession? ActiveSession => _activeSession;
        public QtoRepository? Repository => _repository;
        public bool HasActiveSession => _activeSession != null;

        /// <summary>Evento sollevato quando una sessione viene creata, riaperta o chiusa.</summary>
        public event EventHandler<SessionChangedEventArgs>? SessionChanged;

        /// <summary>
        /// Apre (o crea) il database del progetto. Non seleziona ancora una sessione specifica.
        /// Da chiamare all'apertura del plug-in sul documento corrente.
        /// </summary>
        public void BindToDocument(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var projectName = GetProjectName(doc);
            var dbPath = DatabaseInitializer.GetDefaultDbPath(projectName);

            _repository?.Dispose();
            _repository = new QtoRepository(dbPath);
        }

        /// <summary>Lista delle sessioni salvate per il documento attualmente bindato.</summary>
        public List<WorkSession> GetSessionsForCurrentDocument(Document doc)
        {
            EnsureRepository();
            return _repository!.GetSessionsForProject(GetProjectPath(doc));
        }

        /// <summary>Crea una nuova sessione vuota e la rende attiva.</summary>
        public WorkSession CreateSession(Document doc, string sessionName)
        {
            EnsureRepository();

            var session = new WorkSession
            {
                ProjectPath = GetProjectPath(doc),
                ProjectName = GetProjectName(doc),
                SessionName = string.IsNullOrWhiteSpace(sessionName)
                    ? $"Computo {DateTime.Now:yyyy-MM-dd HH:mm}"
                    : sessionName,
                Status = SessionStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                LastSavedAt = DateTime.UtcNow,
                ModelSnapshotDate = DateTime.UtcNow
            };

            _repository!.InsertSession(session);
            SetActiveSession(session, SessionChangeKind.Created);
            return session;
        }

        /// <summary>Ricarica una sessione esistente (resume).</summary>
        public WorkSession ResumeSession(int sessionId)
        {
            EnsureRepository();
            var session = _repository!.GetSession(sessionId)
                ?? throw new InvalidOperationException($"Sessione {sessionId} non trovata.");

            SetActiveSession(session, SessionChangeKind.Resumed);
            return session;
        }

        /// <summary>Fork: duplica la sessione attiva con un nuovo nome (Save As).</summary>
        public WorkSession ForkSession(string newName)
        {
            EnsureActiveSession();
            var src = _activeSession!;

            var fork = new WorkSession
            {
                ProjectPath = src.ProjectPath,
                ProjectName = src.ProjectName,
                SessionName = newName,
                Status = SessionStatus.InProgress,
                ActivePhaseId = src.ActivePhaseId,
                ActivePhaseName = src.ActivePhaseName,
                TotalElements = src.TotalElements,
                TaggedElements = src.TaggedElements,
                TotalAmount = src.TotalAmount,
                Notes = src.Notes,
                CreatedAt = DateTime.UtcNow,
                LastSavedAt = DateTime.UtcNow,
                ModelSnapshotDate = src.ModelSnapshotDate
            };

            _repository!.InsertSession(fork);
            // TODO Sprint 3+: copia QtoAssignments, RoomMappings, ecc. dalla sessione sorgente

            SetActiveSession(fork, SessionChangeKind.Forked);
            return fork;
        }

        /// <summary>Chiude la sessione: aggiorna timestamp, mantiene stato InProgress per resume futuro.</summary>
        public void CloseSession()
        {
            if (_activeSession == null) return;
            _activeSession.LastSavedAt = DateTime.UtcNow;
            _repository!.UpdateSession(_activeSession);

            var closed = _activeSession;
            _activeSession = null;
            SessionChanged?.Invoke(this, new SessionChangedEventArgs(closed, SessionChangeKind.Closed));
        }

        /// <summary>Salvataggio incrementale — chiamato da AutoSaveService + dopo ogni INSERISCI.</summary>
        public void Flush()
        {
            if (_activeSession == null) return;
            _activeSession.LastSavedAt = DateTime.UtcNow;
            _repository!.UpdateSession(_activeSession);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static string GetProjectPath(Document doc)
        {
            if (!string.IsNullOrEmpty(doc.PathName)) return doc.PathName;
            // Modello non salvato: usa titolo come fallback
            return $"unsaved://{doc.Title}";
        }

        private static string GetProjectName(Document doc)
        {
            var path = doc.PathName;
            if (string.IsNullOrEmpty(path)) return doc.Title;
            return Path.GetFileNameWithoutExtension(path);
        }

        private void SetActiveSession(WorkSession session, SessionChangeKind kind)
        {
            _activeSession = session;
            SessionChanged?.Invoke(this, new SessionChangedEventArgs(session, kind));
        }

        private void EnsureRepository()
        {
            if (_repository == null)
                throw new InvalidOperationException("SessionManager: chiamare BindToDocument prima di operare sulle sessioni.");
        }

        private void EnsureActiveSession()
        {
            if (_activeSession == null)
                throw new InvalidOperationException("SessionManager: nessuna sessione attiva.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _repository?.Dispose();
            _disposed = true;
        }
    }

    public enum SessionChangeKind
    {
        Created,
        Resumed,
        Forked,
        Closed
    }

    public class SessionChangedEventArgs : EventArgs
    {
        public WorkSession Session { get; }
        public SessionChangeKind Kind { get; }

        public SessionChangedEventArgs(WorkSession session, SessionChangeKind kind)
        {
            Session = session;
            Kind = kind;
        }
    }
}
