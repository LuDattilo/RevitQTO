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

        /// <summary>
        /// Flag diagnostico: se true, salta la registrazione del DockablePane.
        /// Controlla via env var QTO_DISABLE_PANE=1. Usato per isolare crash.
        /// </summary>
        private static bool IsPaneDisabled =>
            string.Equals(
                Environment.GetEnvironmentVariable("QTO_DISABLE_PANE"),
                "1", StringComparison.Ordinal);

        public Result OnStartup(UIControlledApplication application)
        {
            Instance = this;
            CrashLogger.Reset();
            CrashLogger.InstallGlobalHandlers();
            CrashLogger.Info($"OnStartup begin · Revit {application.ControlledApplication.VersionName}");

            try
            {
                CrashLogger.Info("→ RevitTask.Initialize");
                RevitAsync.RevitTask.Initialize(application);

                CrashLogger.Info("→ Loading QtoTheme into Application.Current.Resources");
                LoadThemeIntoApplicationResources();

                CrashLogger.Info("→ SessionManager + AutoSave + PaneVM");
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

                if (IsPaneDisabled)
                {
                    CrashLogger.Warn("→ DockablePane SKIPPED (QTO_DISABLE_PANE=1)");
                }
                else
                {
                    CrashLogger.Info("→ Constructing QtoDockablePane");
                    var pane = new QtoDockablePane(PaneViewModel);

                    CrashLogger.Info("→ RegisterDockablePane");
                    var provider = new QtoDockablePaneProvider(pane);
                    application.RegisterDockablePane(
                        QtoDockablePaneProvider.PaneId,
                        "QTO Plugin",
                        provider);
                }

                CrashLogger.Info("→ CreateRibbon");
                CreateRibbon(application);

                CrashLogger.Info("OnStartup completed OK");
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("OnStartup", ex);
                TaskDialog.Show("QTO Plugin – Errore avvio",
                    $"{ex.GetType().Name}: {ex.Message}\n\nLog: %AppData%\\QtoPlugin\\startup.log");
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            CrashLogger.Info("OnShutdown");
            AutoSave?.Dispose();
            SessionManager?.Dispose();
            return Result.Succeeded;
        }

        /// <summary>
        /// Carica QtoTheme.xaml a livello applicazione — unico merge nel processo Revit.
        /// Evita di fare merge pack-URI dentro ogni UserControl, che causa crash
        /// quando Revit fa il layout iniziale dei DockablePane.
        /// </summary>
        private static void LoadThemeIntoApplicationResources()
        {
            var app = System.Windows.Application.Current;
            if (app == null)
            {
                CrashLogger.Warn("Application.Current è null: theme non caricato (le view mostreranno stili WPF default)");
                return;
            }

            // Evita doppio merge in caso di reload addin
            const string markerKey = "QtoThemeLoaded";
            if (app.Resources.Contains(markerKey)) return;

            try
            {
                var uri = new Uri(
                    "/QtoRevitPlugin;component/Theme/QtoTheme.xaml",
                    UriKind.Relative);
                var theme = (System.Windows.ResourceDictionary)
                    System.Windows.Application.LoadComponent(uri);
                app.Resources.MergedDictionaries.Add(theme);
                app.Resources[markerKey] = true;
                CrashLogger.Info($"Theme caricato: {theme.Count} risorse");
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("LoadThemeIntoApplicationResources", ex);
            }
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

            RibbonPanel panel = application.CreateRibbonPanel(tabName, "Computo Metrico");

            var launchButton = new PushButtonData(
                "LaunchQto",
                "Avvia QTO",
                assemblyPath,
                "QtoRevitPlugin.Commands.LaunchQtoCommand")
            {
                ToolTip = "Apre il pannello QTO",
                LargeImage = IconFactory.CreateLaunchIcon(32),
                Image = IconFactory.CreateLaunchIcon(16)
            };

            panel.AddItem(launchButton);
        }
    }
}
