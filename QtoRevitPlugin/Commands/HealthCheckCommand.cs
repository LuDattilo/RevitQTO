using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace QtoRevitPlugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HealthCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Implementazione completa in Sprint 6 (HealthCheckView + HealthCheckEngine)
            TaskDialog.Show(
                "QTO – Health Check",
                "Il pannello Health Check sarà disponibile al termine dello Sprint 6.\n\n" +
                "Funzionalità previste:\n" +
                "• 6 stati computazione (Computato / Parziale / Non computato / Multi-EP / Escluso)\n" +
                "• Rilevamento Room non bounded\n" +
                "• AnomalyDetector z-score\n" +
                "• Navigazione diretta all'elemento dal report");

            return Result.Succeeded;
        }
    }
}
