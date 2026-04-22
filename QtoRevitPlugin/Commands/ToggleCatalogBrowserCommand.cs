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
    public class ToggleCatalogBrowserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = QtoApplication.Instance;

                if (app.CatalogBrowser == null)
                {
                    var window = new CatalogBrowserWindow();
                    window.Show();
                    app.CatalogBrowser = window;
                }
                else
                {
                    if (app.CatalogBrowser.IsVisible)
                        app.CatalogBrowser.Hide();
                    else
                        app.CatalogBrowser.Show();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("ToggleCatalogBrowserCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
