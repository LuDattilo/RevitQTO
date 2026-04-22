using Autodesk.Revit.UI;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI;
using RevitAsync = Revit.Async;
using System;
using System.Reflection;

namespace QtoRevitPlugin.Application
{
    public class QtoApplication : IExternalApplication
    {
        public static QtoApplication Instance { get; private set; } = null!;

        /// <summary>Gestore sessione condiviso tra i comandi. Singleton per sessione Revit.</summary>
        public SessionManager SessionManager { get; private set; } = null!;

        /// <summary>AutoSave condiviso. Avviato al primo CreateSession/ResumeSession.</summary>
        public AutoSaveService AutoSave { get; private set; } = null!;

        public Result OnStartup(UIControlledApplication application)
        {
            Instance = this;

            try
            {
                // Inizializza il wrapper async per ExternalEvent — richiesto una sola volta in OnStartup.
                // Permette a ViewModel di chiamare: await RevitTask.RunAsync(app => { ... })
                RevitAsync.RevitTask.Initialize(application);

                SessionManager = new SessionManager();
                AutoSave = new AutoSaveService(SessionManager);

                // Avvia AutoSave solo quando esiste una sessione attiva
                SessionManager.SessionChanged += (_, args) =>
                {
                    if (args.Kind == SessionChangeKind.Created
                     || args.Kind == SessionChangeKind.Resumed
                     || args.Kind == SessionChangeKind.Forked)
                    {
                        AutoSave.Start();
                    }
                    else if (args.Kind == SessionChangeKind.Closed)
                    {
                        AutoSave.Stop();
                    }
                };

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
            AutoSave?.Dispose();
            SessionManager?.Dispose();
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
                    "tagging, calcolo deterministico ed export.",
                LargeImage = IconFactory.CreateLaunchIcon(32),
                Image = IconFactory.CreateLaunchIcon(16)
            };

            var healthCheckButton = new PushButtonData(
                "HealthCheck",
                "Health Check",
                assemblyPath,
                "QtoRevitPlugin.Commands.HealthCheckCommand")
            {
                ToolTip = "Verifica lo stato di computazione di tutti gli elementi del modello",
                LargeImage = IconFactory.CreateHealthCheckIcon(32),
                Image = IconFactory.CreateHealthCheckIcon(16)
            };

            panel.AddItem(launchButton);
            panel.AddSeparator();
            panel.AddItem(healthCheckButton);
        }
    }
}
