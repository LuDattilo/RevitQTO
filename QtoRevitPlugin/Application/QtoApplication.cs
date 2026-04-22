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
    /// <summary>
    /// Applicazione plug-in (IExternalApplication). §I15 pattern corretto:
    /// - Registra il DockablePane UNA VOLTA in OnStartup, senza Show()
    /// - NON sottoscrive eventi DocumentOpened/ViewActivated per forzare visibilità
    /// - La persistenza (UIState.dat) è gestita nativamente da Revit
    /// </summary>
    public class QtoApplication : IExternalApplication
    {
        public static QtoApplication Instance { get; private set; } = null!;

        /// <summary>Guid stabile esposto come alias del PaneId nel provider (comodità LaunchQtoCommand).</summary>
        public static DockablePaneId PaneId => QtoDockablePaneProvider.PaneId;

        public SessionManager SessionManager { get; private set; } = null!;
        public AutoSaveService AutoSave { get; private set; } = null!;
        public DockablePaneViewModel PaneViewModel { get; private set; } = null!;
        public UIApplication? CurrentUiApp { get; set; }

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

                // §I15: registrazione idempotente, senza Show(), senza event handlers
                // per forzare visibilità. Revit gestisce la persistenza da UIState.dat.
                CrashLogger.Info("→ Constructing QtoDockablePane");
                var pane = new QtoDockablePane(PaneViewModel);

                CrashLogger.Info($"→ RegisterDockablePane Guid={QtoDockablePaneProvider.PaneGuid}");
                var provider = new QtoDockablePaneProvider(pane);
                application.RegisterDockablePane(
                    QtoDockablePaneProvider.PaneId,
                    QtoDockablePaneProvider.PaneTitle,
                    provider);

                CrashLogger.Info("→ CreateRibbon");
                CreateRibbon(application);

                CrashLogger.Info("OnStartup completed OK");
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("OnStartup", ex);
                TaskDialog.Show("CME – Errore avvio",
                    $"{ex.GetType().Name}: {ex.Message}\n\nLog: %AppData%\\QtoPlugin\\startup.log");
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            CrashLogger.Info("OnShutdown");
            try
            {
                // Salva solo la sessione attiva (file .cme sempre consistente)
                SessionManager?.Flush();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("OnShutdown.Flush", ex);
            }

            AutoSave?.Dispose();
            SessionManager?.Dispose();
            return Result.Succeeded;
        }

        // =====================================================================
        // Theme + Ribbon
        // =====================================================================

        private static void LoadThemeIntoApplicationResources()
        {
            var app = System.Windows.Application.Current;
            if (app == null)
            {
                CrashLogger.Warn("Application.Current è null: theme non caricato");
                return;
            }

            const string markerKey = "QtoThemeLoaded";
            if (app.Resources.Contains(markerKey)) return;

            try
            {
                var uri = new Uri("/QtoRevitPlugin;component/Theme/QtoTheme.xaml", UriKind.Relative);
                var theme = (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(uri);
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
            const string tabName = "CME";

            try { application.CreateRibbonTab(tabName); }
            catch { /* già esistente */ }

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            RibbonPanel panel = application.CreateRibbonPanel(tabName, "Computo Metrico");

            var launchButton = new PushButtonData(
                "LaunchCme",
                "Avvia CME",
                assemblyPath,
                "QtoRevitPlugin.Commands.LaunchQtoCommand")
            {
                ToolTip = "Apre il pannello CME – Computo Metrico Estimativo",
                LargeImage = IconFactory.CreateLaunchIcon(32),
                Image = IconFactory.CreateLaunchIcon(16)
            };
            panel.AddItem(launchButton);
        }
    }
}
