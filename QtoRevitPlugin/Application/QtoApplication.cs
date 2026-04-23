using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
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
    /// Applicazione plug-in (IExternalApplication). §I15 pattern puro:
    /// - Registra il DockablePane UNA VOLTA in OnStartup, senza Show()
    /// - Handler Idling one-shot per CATTURARE <see cref="UIApplication"/> early in
    ///   <see cref="CurrentUiApp"/> (necessario ai menu Sessione quando il pane è già
    ///   visibile dal restore di UIState.dat e l'utente non ha ancora cliccato ribbon).
    /// - Visibilità/posizione/dimensione delegate interamente a Revit (UIState.dat).
    ///   Il pane si riapre come l'utente l'ha lasciato l'ultima sessione — niente force-Hide.
    /// </summary>
    public class QtoApplication : IExternalApplication
    {
        public static QtoApplication Instance { get; private set; } = null!;

        /// <summary>Guid stabile esposto come alias del PaneId nel provider (comodità LaunchQtoCommand).</summary>
        public static DockablePaneId PaneId => QtoConstants.MainPaneId;

        public SessionManager SessionManager { get; private set; } = null!;
        public AutoSaveService AutoSave { get; private set; } = null!;
        public DockablePaneViewModel PaneViewModel { get; private set; } = null!;
        public UIApplication? CurrentUiApp { get; set; }

        /// <summary>
        /// Libreria globale dei listini — persistita in %AppData%\QtoPlugin\UserLibrary.db,
        /// condivisa da tutti i computi .cme aperti da questo utente.
        /// </summary>
        public UserLibraryManager UserLibrary { get; private set; } = null!;
        public IUserContext UserContext { get; private set; } = null!;
        // CatalogBrowser property rimossa: il Prezzario è ora accessibile solo dalla
        // SetupView (via bottone "Sfoglia listino…"), non più da un ribbon button dedicato.
        // Il ciclo di vita della CatalogBrowserWindow è gestito da SetupView stessa.

        // One-shot: al primo Idling catturiamo l'UIApplication.
        // Non tentiamo Hide/Show sul pane: quella è gestita da Revit via UIState.dat.
        private UIControlledApplication? _appForIdling;
        private bool _uiAppCaptured;

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

                CrashLogger.Info("→ UserLibraryManager (listini globali)");
                UserLibrary = new UserLibraryManager();
                CrashLogger.Info($"   UserLibrary path: {UserLibrary.LibraryPath}");

                CrashLogger.Info("→ IUserContext");
                UserContext = new WindowsUserContext();

                CrashLogger.Info("→ SessionManager + AutoSave + PaneVM");
                SessionManager = new SessionManager();
                AutoSave = new AutoSaveService(SessionManager);
                PaneViewModel = new DockablePaneViewModel(SessionManager, UserLibrary);

                SessionManager.SessionChanged += (_, args) =>
                {
                    switch (args.Kind)
                    {
                        case SessionChangeKind.Created:
                        case SessionChangeKind.Resumed:
                        case SessionChangeKind.Forked:
                            PersistLastSessionPath(SessionManager.ActiveFilePath);
                            AutoSave.Start();
                            break;
                        case SessionChangeKind.Closed:
                        case SessionChangeKind.Deleted:
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

                // Idling one-shot: cattura UIApplication in CurrentUiApp (per menu Sessione)
                _appForIdling = application;
                application.Idling += OnFirstIdling;

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

            // Sicurezza: unsubscribe se l'handler non si è ancora fire-ed o completato
            UnsubscribeIdling();

            AutoSave?.Dispose();
            SessionManager?.Dispose();
            UserLibrary?.Dispose();
            return Result.Succeeded;
        }

        /// <summary>
        /// Handler Idling one-shot: cattura <see cref="UIApplication"/> in
        /// <see cref="CurrentUiApp"/> (necessario per GetActiveDocument dai menu Sessione
        /// quando il pane è già visibile da restore UIState.dat e l'utente non ha cliccato ribbon).
        /// NON tocca la visibilità del pane — quella è gestita da Revit.
        /// </summary>
        private void OnFirstIdling(object? sender, IdlingEventArgs e)
        {
            if (_uiAppCaptured) return;

            if (sender is UIApplication uiApp)
            {
                CurrentUiApp = uiApp;
                _uiAppCaptured = true;
                CrashLogger.Info("UIApplication captured at first Idling");
            }

            UnsubscribeIdling();
        }

        private void UnsubscribeIdling()
        {
            if (_appForIdling != null)
            {
                try { _appForIdling.Idling -= OnFirstIdling; } catch { /* best effort */ }
                _appForIdling = null;
            }
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
            try { application.CreateRibbonTab(QtoConstants.RibbonTabName); }
            catch { /* già esistente */ }

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            RibbonPanel panel = application.CreateRibbonPanel(QtoConstants.RibbonTabName, QtoConstants.RibbonPanelName);

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

            var exportButton = new PushButtonData(
                "ExportCme",
                "Export",
                assemblyPath,
                "QtoRevitPlugin.Commands.ExportCommand")
            {
                ToolTip = "Esporta il computo in XPWE (PriMus) / Excel / PDF / CSV",
                LargeImage = IconFactory.CreateLaunchIcon(32),
                Image = IconFactory.CreateLaunchIcon(16)
            };

            // Il Prezzario (CatalogBrowserWindow) non ha un bottone ribbon dedicato:
            // è accessibile dalla SetupView via "Sfoglia listino…". Evita duplicazione.
            panel.AddItem(launchButton);
            panel.AddSeparator();
            panel.AddItem(exportButton);
        }

        private static void PersistLastSessionPath(string? activeFilePath)
        {
            if (string.IsNullOrWhiteSpace(activeFilePath))
                return;

            var settings = SettingsService.Load();
            if (string.Equals(settings.LastSessionFilePath, activeFilePath, StringComparison.OrdinalIgnoreCase))
                return;

            settings.LastSessionFilePath = activeFilePath;
            SettingsService.Save(settings);
        }
    }
}
