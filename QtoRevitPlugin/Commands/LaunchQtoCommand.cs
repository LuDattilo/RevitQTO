using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.Panes;
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

                // Se non c'è ancora una sessione attiva, guida l'utente attraverso setup/resume.
                if (!sessionMgr.HasActiveSession)
                {
                    sessionMgr.BindToDocument(doc);

                    var recovery = new RecoveryService();
                    var analysis = recovery.Analyze(doc, sessionMgr.Repository!);
                    if (analysis.RecommendedAction != RecoveryAction.NoActionNeeded
                        && !recovery.CanSyncSilently(analysis))
                    {
                        TaskDialog.Show("QTO – Recovery",
                            $"{analysis.Summary}\n\n" +
                            "La riconciliazione completa è attiva dallo Sprint 3 (scrittura ES).");
                    }

                    var sessions = sessionMgr.GetSessionsForCurrentDocument(doc);
                    var projectLabel = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
                    var dialog = new SessionListWindow(projectLabel, sessions);
                    new WindowInteropHelper(dialog).Owner =
                        System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

                    var dialogResult = dialog.ShowDialog();
                    if (dialogResult != true) return Result.Cancelled;

                    switch (dialog.Result)
                    {
                        case SessionDialogResult.NewSession:
                            sessionMgr.CreateSession(doc, sessionName: string.Empty);
                            break;
                        case SessionDialogResult.Resume:
                            sessionMgr.ResumeSession(dialog.SelectedSessionId);
                            break;
                    }
                }

                // Mostra il DockablePane (sempre presente dopo OnStartup).
                // Se l'utente lo aveva chiuso/minimizzato, torna visibile.
                var pane = commandData.Application.GetDockablePane(QtoDockablePaneProvider.PaneId);
                pane?.Show();

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
