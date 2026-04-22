using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Application;
using System;

namespace QtoRevitPlugin.Commands
{
    /// <summary>
    /// §I15 Fix 4: apre il DockablePane SOLO tramite questo comando (ribbon).
    /// Nessuna altra chiamata a pane.Show() nel plug-in.
    /// Revit persiste stato/posizione/dimensione → al riavvio l'utente ritrova
    /// il pane come l'ha lasciato.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LaunchQtoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Catturiamo UIApplication per i menu interni del pane (New/Open file .cme)
                QtoApplication.Instance.CurrentUiApp = commandData.Application;

                var pane = commandData.Application.GetDockablePane(QtoApplication.PaneId);
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
