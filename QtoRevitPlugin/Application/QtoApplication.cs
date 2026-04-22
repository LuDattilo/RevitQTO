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
    /// Applicazione plug-in (IExternalApplication). §I15 pattern:
    /// - Registra il DockablePane UNA VOLTA in OnStartup, senza Show()
    /// - Handler Idling one-shot: al primo idle forza Hide() per bypassare il restore
    ///   automatico di UIState.dat (scelta UX: il pane parte SEMPRE chiuso, si apre solo via ribbon)
    /// - Posizione/dimensione restano persistite da Revit (solo la visibilità è sovrascritta)
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

        // One-shot: al primo Idling nascondiamo il pane (override del restore di UIState.dat)
        // Retry pattern: il pane può non essere ancora "creato" da Revit ai primi Idling
        // (registrazione != creazione). Riproviamo finché Hide riesce o max attempts.
        private UIControlledApplication? _appForIdling;
        private bool _hiddenAtStartup;
        private int _idlingAttempts;
        private const int MaxIdlingAttempts = 30; // ~3s a frequenza Idling Revit

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

                // Idling one-shot: forza pane chiuso all'avvio (bypass UIState.dat restore)
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
            return Result.Succeeded;
        }

        /// <summary>
        /// Handler Idling con retry: al primo idle cattura UIApplication (necessario per
        /// GetActiveDocument dai menu Sessione quando il pane è già visibile da restore UIState.dat)
        /// e tenta Hide() sul DockablePane. Il pane può non essere ancora "creato" ai primi Idling
        /// (registrazione != istanziazione): in quel caso ritenta finché riesce o fino a MaxIdlingAttempts.
        /// Si unsubscribe SOLO dopo successo (o abbandono per timeout).
        /// </summary>
        private void OnFirstIdling(object? sender, IdlingEventArgs e)
        {
            if (_hiddenAtStartup) return;

            // Cattura UIApplication EARLY: risolve il bug "Apri un progetto Revit" quando il pane
            // è già visibile (UIState.dat restore) e l'utente apre menu Sessione senza aver mai cliccato ribbon
            if (sender is UIApplication uiApp)
            {
                CurrentUiApp = uiApp;
            }
            else
            {
                // Caso teorico: sender non è UIApplication. Abbandoniamo.
                UnsubscribeIdling();
                return;
            }

            _idlingAttempts++;
            if (_idlingAttempts > MaxIdlingAttempts)
            {
                CrashLogger.Warn($"OnFirstIdling: abbandono dopo {_idlingAttempts} tentativi (pane non creato in tempo)");
                _hiddenAtStartup = true;
                UnsubscribeIdling();
                return;
            }

            try
            {
                var pane = CurrentUiApp.GetDockablePane(PaneId);
                if (pane != null)
                {
                    if (pane.IsShown())
                    {
                        pane.Hide();
                        CrashLogger.Info($"Pane hidden al tentativo {_idlingAttempts} (override UIState.dat restore)");
                    }
                    else
                    {
                        CrashLogger.Info($"Pane già nascosto al tentativo {_idlingAttempts}");
                    }
                    _hiddenAtStartup = true;
                    UnsubscribeIdling();
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // "The requested dockable pane has not been created yet" — normal nei primi tentativi.
                // Log spam-free: solo ogni 5 tentativi.
                if (_idlingAttempts == 1 || _idlingAttempts % 5 == 0)
                    CrashLogger.Info($"OnFirstIdling: pane non ancora creato (tentativo {_idlingAttempts}), retry...");
                // NON unsubscribe: continua al prossimo Idling
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("OnFirstIdling", ex);
                _hiddenAtStartup = true;
                UnsubscribeIdling();
            }
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
