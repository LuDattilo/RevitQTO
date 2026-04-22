using Autodesk.Revit.UI;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI;
using QtoRevitPlugin.UI.Panes;
using QtoRevitPlugin.UI.ViewModels;
using RevitAsync = Revit.Async;
using System;
using System.Reflection;

namespace QtoRevitPlugin.Application
{
    public class QtoApplication : IExternalApplication
    {
        public static QtoApplication Instance { get; private set; } = null!;

        /// <summary>Gestore sessione condiviso. Singleton per sessione Revit.</summary>
        public SessionManager SessionManager { get; private set; } = null!;

        /// <summary>AutoSave condiviso. Avviato al primo CreateSession/ResumeSession.</summary>
        public AutoSaveService AutoSave { get; private set; } = null!;

        /// <summary>ViewModel radice del DockablePane, persistente per tutta la sessione Revit.</summary>
        public DockablePaneViewModel PaneViewModel { get; private set; } = null!;

        public Result OnStartup(UIControlledApplication application)
        {
            Instance = this;

            try
            {
                // Revit.Async: initializzato una sola volta in OnStartup (richiesto dal wrapper)
                RevitAsync.RevitTask.Initialize(application);

                SessionManager = new SessionManager();
                AutoSave = new AutoSaveService(SessionManager);
                PaneViewModel = new DockablePaneViewModel(SessionManager);

                SessionManager.SessionChanged += (_, args) =>
                {
                    switch (args.Kind)
                    {
                        case SessionChangeKind.Created:
                        case SessionChangeKind.Resumed:
                        case SessionChangeKind.Forked:
                            AutoSave.Start();
                            break;
                        case SessionChangeKind.Closed:
                            AutoSave.Stop();
                            break;
                    }
                };

                // DockablePane va registrato in OnStartup, prima dell'apertura del primo documento.
                // Una sola istanza per lifecycle Revit.
                var pane = new QtoDockablePane(PaneViewModel);
                var provider = new QtoDockablePaneProvider(pane);
                application.RegisterDockablePane(
                    QtoDockablePaneProvider.PaneId,
                    "QTO – GPA Ingegneria",
                    provider);

                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("QTO Plugin – Errore avvio",
                    $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}");
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

            // Ribbon minimale: solo il punto di ingresso.
            // Tutto il resto della UI vive nel DockablePane per separazione netta con l'UI Revit.
            RibbonPanel panel = application.CreateRibbonPanel(tabName, "Computo Metrico");

            var launchButton = new PushButtonData(
                "LaunchQto",
                "Avvia QTO",
                assemblyPath,
                "QtoRevitPlugin.Commands.LaunchQtoCommand")
            {
                ToolTip = "Apre il pannello QTO – contiene tutti i comandi (Setup, Selezione, Tagging, Export...)",
                LongDescription =
                    "Il pannello QTO è flottante per default: può essere trascinato su un secondo monitor " +
                    "affiancandolo alla vista di progettazione. Le scritture sul modello Revit " +
                    "passano sempre da ExternalEvent con conferma anteprima.",
                LargeImage = IconFactory.CreateLaunchIcon(32),
                Image = IconFactory.CreateLaunchIcon(16)
            };

            panel.AddItem(launchButton);
        }
    }
}
