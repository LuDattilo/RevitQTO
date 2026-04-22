using Autodesk.Revit.UI;
using System;
using System.Reflection;

namespace QtoRevitPlugin.Application
{
    public class QtoApplication : IExternalApplication
    {
        public static QtoApplication Instance { get; private set; } = null!;

        public Result OnStartup(UIControlledApplication application)
        {
            Instance = this;

            try
            {
                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("QTO Plugin – Errore avvio", ex.Message);
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void CreateRibbon(UIControlledApplication application)
        {
            const string tabName = "QTO";

            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab già esistente — ignorato
            }

            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            // Pannello principale
            RibbonPanel panel = application.CreateRibbonPanel(tabName, "Computo Metrico");

            var launchButton = new PushButtonData(
                "LaunchQto",
                "Avvia QTO",
                assemblyPath,
                "QtoRevitPlugin.Commands.LaunchQtoCommand")
            {
                ToolTip = "Apre il pannello di Quantity Take-Off per la sessione corrente",
                LongDescription =
                    "Avvia il flusso completo QTO: caricamento listino, selezione elementi, " +
                    "tagging, calcolo deterministico ed export."
            };

            var healthCheckButton = new PushButtonData(
                "HealthCheck",
                "Health Check",
                assemblyPath,
                "QtoRevitPlugin.Commands.HealthCheckCommand")
            {
                ToolTip = "Verifica lo stato di computazione di tutti gli elementi del modello"
            };

            panel.AddItem(launchButton);
            panel.AddSeparator();
            panel.AddItem(healthCheckButton);
        }
    }
}
