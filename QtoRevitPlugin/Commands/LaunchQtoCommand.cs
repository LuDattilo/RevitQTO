using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.Views;
using System;
using System.Windows.Interop;

namespace QtoRevitPlugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LaunchQtoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("QTO", "Apri un progetto Revit prima di avviare il plug-in.");
                    return Result.Cancelled;
                }

                var sessionMgr = QtoApplication.Instance.SessionManager;
                sessionMgr.BindToDocument(doc);

                // Analisi recovery (silenziosa se non serve intervento utente)
                var recovery = new RecoveryService();
                var analysis = recovery.Analyze(doc, sessionMgr.Repository!);
                if (analysis.RecommendedAction != RecoveryAction.NoActionNeeded
                    && !recovery.CanSyncSilently(analysis))
                {
                    // Sprint 3+: mostrare dialog avanzato con 3 opzioni. Per ora: log in TaskDialog semplice.
                    TaskDialog.Show("QTO – Recovery",
                        $"{analysis.Summary}\n\n" +
                        "La logica di riconciliazione completa sarà attiva a partire dallo Sprint 3 " +
                        "(quando le assegnazioni saranno scritte nel modello).");
                }

                // Dialog sessioni
                var sessions = sessionMgr.GetSessionsForCurrentDocument(doc);
                var projectName = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
                var dialog = new SessionListWindow(projectName, sessions);

                var helper = new WindowInteropHelper(dialog);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

                var result = dialog.ShowDialog();
                if (result != true) return Result.Cancelled;

                switch (dialog.Result)
                {
                    case SessionDialogResult.NewSession:
                        sessionMgr.CreateSession(doc, sessionName: string.Empty);
                        break;
                    case SessionDialogResult.Resume:
                        sessionMgr.ResumeSession(dialog.SelectedSessionId);
                        break;
                }

                // Apre la finestra principale
                var window = new QtoMainWindow(commandData.Application, sessionMgr);
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
