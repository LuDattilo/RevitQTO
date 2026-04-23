using Autodesk.Revit.DB;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.UI.ViewModels;
using QtoRevitPlugin.UI.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Orchestra il ciclo di vita di un computo CME.
    /// Modello file-based: ogni computo è un file .cme (SQLite database) scelto dall'utente
    /// con OpenFileDialog/SaveFileDialog. Un file = un computo.
    ///
    /// Threading: il SessionManager è UI-thread-only (SQLite connection non è thread-safe).
    /// Non chiama mai la Revit API: il Document è passato come parametro solo per leggere
    /// project path/name al momento della creazione.
    /// </summary>
    public class SessionManager : IDisposable
    {
        public const string FileExtension = ".cme";
        public const string FileFilter = "Computo CME (*.cme)|*.cme|Tutti i file (*.*)|*.*";

        private QtoRepository? _repository;
        private WorkSession? _activeSession;
        private string? _activeFilePath;
        private bool _disposed;

        public WorkSession? ActiveSession => _activeSession;
        public QtoRepository? Repository => _repository;
        public bool HasActiveSession => _activeSession != null;

        /// <summary>Path del file .cme attualmente aperto, o null se nessuna sessione attiva.</summary>
        public string? ActiveFilePath => _activeFilePath;

        public event EventHandler<SessionChangedEventArgs>? SessionChanged;

        // =====================================================================
        // Operazioni file
        // =====================================================================

        /// <summary>
        /// Crea un nuovo file .cme al path indicato e vi scrive una sessione vuota.
        /// Se il file esiste già, viene sovrascritto (il chiamante deve aver confermato).
        /// </summary>
        public WorkSession CreateSession(string filePath, Document doc, string sessionName)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Path file obbligatorio.", nameof(filePath));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Se il file esiste, lo rimuoviamo (nuovo file = schema fresco)
            CloseCurrent();
            if (File.Exists(filePath)) File.Delete(filePath);

            _repository = new QtoRepository(filePath);
            _activeFilePath = filePath;

            var session = new WorkSession
            {
                ProjectPath = GetProjectPath(doc),
                ProjectName = GetProjectName(doc),
                SessionName = string.IsNullOrWhiteSpace(sessionName)
                    ? Path.GetFileNameWithoutExtension(filePath)
                    : sessionName,
                Status = SessionStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                LastSavedAt = DateTime.UtcNow,
                ModelSnapshotDate = DateTime.UtcNow
            };

            _repository.InsertSession(session);
            SetActiveSession(session, SessionChangeKind.Created);
            return session;
        }

        /// <summary>
        /// Apre un file .cme esistente. Carica la prima sessione del DB (convention: 1 file = 1 computo).
        /// </summary>
        public WorkSession OpenSession(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Path file obbligatorio.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File .cme non trovato.", filePath);

            CloseCurrent();

            _repository = new QtoRepository(filePath);
            _activeFilePath = filePath;

            // Carichiamo la prima sessione (convention: 1 file = 1 computo).
            // Se il DB contiene più sessioni, prendiamo la più recente LastSavedAt.
            var allSessions = _repository.GetAllSessions();
            if (allSessions.Count == 0)
            {
                _repository.Dispose();
                _repository = null;
                _activeFilePath = null;
                throw new InvalidDataException(
                    $"Il file {Path.GetFileName(filePath)} non contiene alcun computo. File corrotto?");
            }

            var session = allSessions[0];  // già ORDER BY LastSavedAt DESC

            // Verifica snapshot per Model Diff Check
            TryLaunchModelDiff(session);

            SetActiveSession(session, SessionChangeKind.Resumed);
            return session;
        }

        /// <summary>Salva con nome: duplica il file .cme corrente a un nuovo path e vi si sposta.</summary>
        public void SaveAs(string newFilePath)
        {
            if (_activeFilePath == null || _repository == null || _activeSession == null)
                throw new InvalidOperationException("Nessun computo attivo da salvare con nome.");
            if (string.IsNullOrWhiteSpace(newFilePath))
                throw new ArgumentException("Path file obbligatorio.", nameof(newFilePath));
            if (string.Equals(newFilePath, _activeFilePath, StringComparison.OrdinalIgnoreCase))
            {
                // Same file: degrada a Flush
                Flush();
                return;
            }

            // Flush e chiudi la connessione per liberare il file sorgente
            _activeSession.LastSavedAt = DateTime.UtcNow;
            _repository.UpdateSession(_activeSession);
            _repository.Dispose();
            _repository = null;

            // Copia fisica del file
            if (File.Exists(newFilePath)) File.Delete(newFilePath);
            File.Copy(_activeFilePath, newFilePath);

            // Riapri puntando al nuovo path
            _repository = new QtoRepository(newFilePath);
            _activeFilePath = newFilePath;

            // Aggiorna nome sessione = nome file (convenzione semplice)
            _activeSession.SessionName = Path.GetFileNameWithoutExtension(newFilePath);
            _activeSession.LastSavedAt = DateTime.UtcNow;
            _repository.UpdateSession(_activeSession);

            SessionChanged?.Invoke(this, new SessionChangedEventArgs(_activeSession, SessionChangeKind.Forked));
        }

        /// <summary>Chiude il computo corrente senza cancellare il file.</summary>
        public void CloseSession()
        {
            if (_activeSession == null) return;

            // Flush finale
            _activeSession.LastSavedAt = DateTime.UtcNow;
            _repository?.UpdateSession(_activeSession);

            var closed = _activeSession;
            CloseCurrent();
            SessionChanged?.Invoke(this, new SessionChangedEventArgs(closed, SessionChangeKind.Closed));
        }

        /// <summary>Elimina il file .cme corrente dal disco e chiude la sessione.</summary>
        public void DeleteActiveFile()
        {
            if (_activeSession == null || _activeFilePath == null) return;

            var closed = _activeSession;
            var pathToDelete = _activeFilePath;

            // Chiudi prima di cancellare
            CloseCurrent();

            try
            {
                if (File.Exists(pathToDelete))
                    File.Delete(pathToDelete);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    $"Impossibile eliminare il file '{pathToDelete}': {ex.Message}", ex);
            }

            SessionChanged?.Invoke(this, new SessionChangedEventArgs(closed, SessionChangeKind.Deleted));
        }

        // =====================================================================
        // Operazioni sul contenuto della sessione attiva
        // =====================================================================

        /// <summary>Flush immediato: scrive stato sessione nel file corrente.</summary>
        public void Flush()
        {
            if (_activeSession == null || _repository == null) return;
            _activeSession.LastSavedAt = DateTime.UtcNow;
            _repository.UpdateSession(_activeSession);
        }

        /// <summary>
        /// Notifica alle view che la Fase Revit attiva della sessione è cambiata (soft-switch).
        /// Persiste immediatamente il cambio (ActivePhaseId/Name già impostati dal chiamante)
        /// e solleva <see cref="SessionChanged"/> con <see cref="SessionChangeKind.PhaseChanged"/>,
        /// così che tutte le view phase-bound (ComputoStructure, Verifica, Tagging) si aggiornino.
        /// </summary>
        public void NotifyActivePhaseChanged()
        {
            if (_activeSession == null || _repository == null) return;
            _activeSession.LastSavedAt = DateTime.UtcNow;
            _repository.UpdateSession(_activeSession);
            SessionChanged?.Invoke(this,
                new SessionChangedEventArgs(_activeSession, SessionChangeKind.PhaseChanged));
        }

        /// <summary>Rinomina la sessione (non il file). Cambia solo il SessionName nel DB.</summary>
        public void RenameActiveSession(string newName)
        {
            if (_activeSession == null || _repository == null)
                throw new InvalidOperationException("Nessun computo attivo da rinominare.");
            _activeSession.SessionName = newName;
            _activeSession.LastSavedAt = DateTime.UtcNow;
            _repository.UpdateSession(_activeSession);
            SessionChanged?.Invoke(this,
                new SessionChangedEventArgs(_activeSession, SessionChangeKind.Renamed));
        }

        // =====================================================================
        // Model Diff on open
        // =====================================================================

        private void TryLaunchModelDiff(WorkSession session)
        {
            if (_repository == null) return;

            var snapshots = _repository.GetSnapshots(session.Id);
            if (snapshots.Count == 0) return;

            var answer = MessageBox.Show(
                $"Il file contiene {snapshots.Count} elementi con snapshot salvati.\n" +
                "Vuoi verificare le modifiche al modello Revit rispetto all'ultima sessione?",
                "Apri CME esistente",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            LaunchModelDiff(session.Id, snapshots);
        }

        private void LaunchModelDiff(int sessionId, IReadOnlyList<ElementSnapshot> snapshots)
        {
            // Capture repository reference before async work — _repository can be nulled by CloseCurrent()
            var capturedRepo = _repository;
            if (capturedRepo == null) return;

            _ = Revit.Async.RevitTask.RunAsync(app =>
            {
                try
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return;

                    var diffSvc = new ModelDiffService(new MappingRulesService());
                    var result = diffSvc.ComputeDiff(doc, snapshots);

                    if (result.Deleted.Count == 0 && result.Modified.Count == 0 && result.Added.Count == 0)
                        return;

                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        // Re-check that the session is still the same before showing UI
                        if (capturedRepo != _repository) return;

                        var userContext = QtoRevitPlugin.Application.QtoApplication.Instance?.UserContext
                            ?? new WindowsUserContext();
                        var vm = new ReconciliationViewModel(result, capturedRepo, userContext);
                        var window = new ReconciliationWindow(vm);
                        window.Show();
                    }));
                }
                catch (Exception ex)
                {
                    CrashLogger.WriteException("LaunchModelDiff", ex);
                }
            });
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private void CloseCurrent()
        {
            _repository?.Dispose();
            _repository = null;
            _activeSession = null;
            _activeFilePath = null;
        }

        private static string GetProjectPath(Document doc)
        {
            if (!string.IsNullOrEmpty(doc.PathName)) return doc.PathName;
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

        public void Dispose()
        {
            if (_disposed) return;
            CloseCurrent();
            _disposed = true;
        }
    }

    public enum SessionChangeKind
    {
        Created,
        Resumed,
        Forked,
        Renamed,
        Closed,
        Deleted,
        /// <summary>Fase Revit attiva cambiata (contesto soft-switch phase-bound).</summary>
        PhaseChanged
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
