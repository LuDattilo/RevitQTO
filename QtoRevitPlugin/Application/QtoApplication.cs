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

        /// <summary>UIApplication catturata all'ultimo click di "Avvia QTO". Usata dai
        /// ContextMenu handler del pane che non hanno accesso diretto a commandData.</summary>
        public UIApplication? CurrentUiApp { get; set; }

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
                        "CME – Computo Metrico Estimativo",
                        provider);

                    // Revit persiste lo stato visible/hidden del pane tra sessioni.
                    // Forziamo Hide() al primo Idling per garantire che il pane
                    // sia nascosto all'avvio. L'utente lo apre con "Avvia QTO".
                    _revitApp = application;
                    application.Idling += HidePaneOnFirstIdle;

                    // Salva e chiudi sessione CME quando l'utente chiude il .rvt associato
                    application.ControlledApplication.DocumentClosing += OnDocumentClosing;
                }

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

            // 1) Salva sessione attiva (se presente) → file .cme sempre consistente
            try
            {
                if (SessionManager?.HasActiveSession == true)
                {
                    var filePath = SessionManager.ActiveFilePath;
                    SessionManager.Flush();
                    CrashLogger.Info($"OnShutdown: sessione salvata in {filePath}");
                }
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("OnShutdown.Flush", ex);
            }

            // 2) Nascondi il DockablePane: al prossimo boot Revit non lo ripristinerà visible
            try
            {
                if (CurrentUiApp != null)
                {
                    var pane = CurrentUiApp.GetDockablePane(QtoDockablePaneProvider.PaneId);
                    if (pane != null && pane.IsShown())
                    {
                        pane.Hide();
                        CrashLogger.Info("OnShutdown: DockablePane nascosto");
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("OnShutdown.HidePane", ex);
            }

            AutoSave?.Dispose();
            SessionManager?.Dispose();
            return Result.Succeeded;
        }

        private static UIControlledApplication? _revitApp;
        private static int _hideAttempts;
        private const int MaxHideAttempts = 500; // ~5-8 secondi di tentativi (Idling fires ~60-100Hz)

        /// <summary>
        /// Al primo Idling utile dopo il boot:
        /// 1) Cattura UIApplication (se non già fatto da LaunchQtoCommand)
        /// 2) Nasconde il DockablePane se Revit lo aveva persistito come visibile
        /// Retry pattern: il pane può non essere ancora creato al primo Idling.
        /// Unsubscribe solo quando entrambi gli obiettivi sono raggiunti.
        /// </summary>
        private static void HidePaneOnFirstIdle(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            _hideAttempts++;

            try
            {
                if (sender is not UIApplication uiApp) return;

                // 1) Cattura UIApplication una volta per tutte
                Instance.CurrentUiApp = uiApp;

                // 2) Hide del pane (con retry se non creato)
                Autodesk.Revit.UI.DockablePane? pane;
                try
                {
                    pane = uiApp.GetDockablePane(QtoDockablePaneProvider.PaneId);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    if (_hideAttempts > MaxHideAttempts)
                    {
                        UnsubscribeIdling();
                        CrashLogger.Warn($"HidePaneOnFirstIdle: pane non creato dopo {MaxHideAttempts} tentativi");
                    }
                    return;
                }

                if (pane != null && pane.IsShown())
                {
                    pane.Hide();
                    CrashLogger.Info($"DockablePane nascosto al boot (tentativo {_hideAttempts})");
                }

                UnsubscribeIdling();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("HidePaneOnFirstIdle", ex);
                UnsubscribeIdling();
            }
        }

        private static void UnsubscribeIdling()
        {
            if (_revitApp != null)
            {
                _revitApp.Idling -= HidePaneOnFirstIdle;
                _revitApp = null;
            }
        }

        /// <summary>
        /// Quando l'utente chiude il file Revit associato al computo CME attivo:
        /// 1) Flush della sessione (il .cme resta salvato su disco)
        /// 2) Chiudi la sessione (torna a stato "nessun computo aperto")
        /// 3) Nascondi il DockablePane
        /// Così il lavoro non si perde e al prossimo apri .rvt il pane parte pulito.
        /// </summary>
        private static void OnDocumentClosing(object? sender, Autodesk.Revit.DB.Events.DocumentClosingEventArgs e)
        {
            try
            {
                var sessionMgr = Instance.SessionManager;
                if (sessionMgr?.HasActiveSession != true) return;

                var docPath = string.IsNullOrEmpty(e.Document.PathName)
                    ? $"unsaved://{e.Document.Title}"
                    : e.Document.PathName;

                // Chiudi sessione solo se il doc che chiude è quello associato al computo corrente
                if (sessionMgr.ActiveSession!.ProjectPath == docPath)
                {
                    var cmePath = sessionMgr.ActiveFilePath;
                    sessionMgr.CloseSession();
                    CrashLogger.Info($"DocumentClosing: sessione chiusa (file .cme salvato: {cmePath})");

                    // Nascondi pane: al prossimo apri .rvt parte OFF
                    if (Instance.CurrentUiApp != null)
                    {
                        var pane = Instance.CurrentUiApp.GetDockablePane(QtoDockablePaneProvider.PaneId);
                        if (pane?.IsShown() == true)
                            pane.Hide();
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("OnDocumentClosing", ex);
            }
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
            const string tabName = "CME";

            try
            {
                application.CreateRibbonTab(tabName);
                CrashLogger.Info($"  CreateRibbonTab({tabName}) OK");
            }
            catch (Exception ex)
            {
                CrashLogger.Info($"  CreateRibbonTab({tabName}) skipped: {ex.GetType().Name}: {ex.Message}");
            }

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            CrashLogger.Info($"  Assembly path: {assemblyPath}");

            RibbonPanel panel;
            try
            {
                panel = application.CreateRibbonPanel(tabName, "Computo Metrico");
                CrashLogger.Info("  CreateRibbonPanel OK");
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("CreateRibbonPanel", ex);
                throw;
            }

            try
            {
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
                CrashLogger.Info("  PushButton LaunchCme aggiunto");
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("CreateRibbon:AddButton", ex);
                throw;
            }
        }
    }
}
