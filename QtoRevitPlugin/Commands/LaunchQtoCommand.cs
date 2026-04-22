using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.UI.Panes;
using System;
using System.Windows;
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

                // Mostra il DockablePane. Se non c'è sessione attiva, il pane mostra
                // empty state con istruzioni "Nuovo / Apri" dal menu Sessione nell'header.
                var pane = commandData.Application.GetDockablePane(QtoDockablePaneProvider.PaneId);
                if (pane != null)
                {
                    pane.Show();
                    // Centra e ridimensiona la finestra flottante alla prima apertura
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

        private static void CenterAndResizeFloatingPane()
        {
            try
            {
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
            catch { }
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
