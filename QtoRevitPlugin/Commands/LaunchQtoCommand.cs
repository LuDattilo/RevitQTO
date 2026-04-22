using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.Panes;
using QtoRevitPlugin.UI.Views;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace QtoRevitPlugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LaunchQtoCommand : IExternalCommand
    {
        private const double PaneInitialWidth = 960;
        private const double PaneInitialHeight = 920;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("CME", "Apri un progetto Revit prima di avviare il plug-in.");
                    return Result.Cancelled;
                }

                // Memorizza UIApplication per i handler del pane (menu Sessione)
                QtoApplication.Instance.CurrentUiApp = commandData.Application;

                var sessionMgr = QtoApplication.Instance.SessionManager;

                // Se non c'è ancora una sessione attiva, guida l'utente
                if (!sessionMgr.HasActiveSession)
                {
                    sessionMgr.BindToDocument(doc);

                    var recovery = new RecoveryService();
                    var analysis = recovery.Analyze(doc, sessionMgr.Repository!);
                    if (analysis.RecommendedAction != RecoveryAction.NoActionNeeded
                        && !recovery.CanSyncSilently(analysis))
                    {
                        TaskDialog.Show("CME – Recovery",
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

                // Mostra il DockablePane + resize+center alla prima apertura della sessione
                var pane = commandData.Application.GetDockablePane(QtoDockablePaneProvider.PaneId);
                if (pane != null)
                {
                    pane.Show();
                    // Il resize/center richiede che la Window contenitore esista.
                    // BeginInvoke con priorità Loaded ci dà il tempo a Revit di crearla.
                    Dispatcher.CurrentDispatcher.BeginInvoke(
                        new Action(CenterAndResizeFloatingPane),
                        DispatcherPriority.Loaded);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Trova la Window WPF che ospita il nostro UserControl del pane, la centra
        /// sullo schermo attivo e la ridimensiona a una dimensione utilizzabile.
        /// </summary>
        private static void CenterAndResizeFloatingPane()
        {
            try
            {
                // Cerca tra le WPF Window aperte quella che contiene il nostro QtoDockablePane
                foreach (Window w in System.Windows.Application.Current.Windows)
                {
                    if (ContainsOurPane(w))
                    {
                        var work = SystemParameters.WorkArea;
                        w.Width = PaneInitialWidth;
                        w.Height = PaneInitialHeight;
                        w.Left = work.Left + (work.Width - PaneInitialWidth) / 2;
                        w.Top = work.Top + (work.Height - PaneInitialHeight) / 2;
                        return;
                    }
                }
            }
            catch
            {
                // Non critico: se non riusciamo a centrare il pane, Revit usa la sua dimensione default.
            }
        }

        private static bool ContainsOurPane(DependencyObject root)
        {
            if (root is QtoDockablePane) return true;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (ContainsOurPane(child)) return true;
            }
            return false;
        }
    }
}
