using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.Views;
using System;

namespace QtoRevitPlugin.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (QtoApplication.Instance?.SessionManager?.ActiveSession == null)
                {
                    TaskDialog.Show("Export", "Apri o crea un file CME prima di esportare.");
                    return Result.Cancelled;
                }

                var window = new ExportWizardWindow();
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("ExportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
