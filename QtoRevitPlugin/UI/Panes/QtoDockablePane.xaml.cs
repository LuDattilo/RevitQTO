using Microsoft.Win32;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.ViewModels;
using QtoRevitPlugin.UI.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace QtoRevitPlugin.UI.Panes
{
    /// <summary>
    /// Hub UI del plug-in. Ospita la barra switcher per le 9 view + Preview.
    /// Modello file-based: ogni computo è un file .cme selezionato via OpenFileDialog/SaveFileDialog.
    /// </summary>
    public partial class QtoDockablePane : UserControl
    {
        private readonly DockablePaneViewModel _vm;
        private readonly Dictionary<QtoViewKey, UserControl> _viewCache = new();
        private readonly Dictionary<QtoViewKey, ToggleButton> _buttonCache = new();
        private UserControl? _noSessionView;

        public QtoDockablePane(DockablePaneViewModel vm)
        {
            _vm = vm;
            DataContext = _vm;

            // BUGFIX #bug-invalidcast-style:
            // Il theme QtoTheme.xaml deve essere caricato nelle Resources di questo
            // UserControl (non su Application.Current: in Revit è null durante OnStartup).
            // Senza questo caricamento, FindResource("SwitcherButton") ritorna il
            // sentinel MS.Internal.NamedObject e il cast a Style crasha.
            EnsureThemeLoaded();

            InitializeComponent();

            // IMPORTANTE: BuildSwitcher() NON viene chiamato qui nel costruttore.
            // FindResource può ancora restituire il sentinel durante la fase di
            // costruzione (i ResourceDictionary MergedDictionaries sono aggiunti
            // ma non ancora completamente risolti per la lookup). Lo spostiamo
            // all'evento Loaded: a quel punto il controllo è agganciato all'albero
            // visuale e le Resources sono garantite disponibili.
            // NB: l'handler OnPaneLoaded già esiste (vedi sotto), agganciato via XAML
            // Loaded="OnPaneLoaded".

            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DockablePaneViewModel.ActiveView))
                    UpdateActiveView();
                if (e.PropertyName == nameof(DockablePaneViewModel.HasActiveSession))
                    UpdateSessionMenuEnabled();
            };

            // NB: UpdateActiveView/UpdateSessionMenuEnabled dipendono da _buttonCache
            // popolato in BuildSwitcher → spostati anch'essi in OnPaneLoaded.

            // §I15: dimensioni e posizione sono gestite da DockablePaneState.FloatingRectangle
            // e dal MinWidth/MinHeight dello XAML. Niente logica custom qui.
        }

        private bool _switcherBuilt;

        /// <summary>
        /// Safety net: se Revit restaura dimensioni stale da UIState.dat o se la reflection
        /// su DockablePaneState.MinimumWidth/Height è fallita silenziosamente, forza
        /// min-size sulla floating Window ospite. No-op quando il pane è docked dentro
        /// la MainWindow di Revit (distinguibile dalle dimensioni enormi).
        /// </summary>
        private void OnPaneLoaded(object sender, RoutedEventArgs e)
        {
            // STEP 1: Safety net min-size PRIMA di qualsiasi operazione che triggeri
            // layout pass. Se lo facciamo dopo BuildSwitcher/UpdateActiveView, il
            // layout iniziale del PlaceholderView può far collassare la finestra
            // floating (regressione osservata dopo fix b60ec39/d9991c3).
            var window = Window.GetWindow(this);
            if (window != null && window.ActualWidth <= 1500 && window.ActualHeight <= 1200)
            {
                // Non docked nella MainWindow di Revit (>1500×1200 = docked) → aggiusta
                if (window.MinWidth < 420) window.MinWidth = 420;
                if (window.MinHeight < 600) window.MinHeight = 600;
                if (window.Width < 420) window.Width = 520;
                if (window.Height < 600) window.Height = 760;
            }

            // STEP 2: BuildSwitcher qui (non nel ctor): a questo punto le Resources
            // MergedDictionaries sono sicuramente risolte e FindResource ritorna lo
            // Style reale invece del sentinel NamedObject. Idempotente via _switcherBuilt.
            if (!_switcherBuilt)
            {
                BuildSwitcher();
                UpdateActiveView();
                UpdateSessionMenuEnabled();
                _switcherBuilt = true;
            }
        }

        /// <summary>
        /// Merge idempotente di QtoTheme.xaml nelle Resources del pane.
        /// Necessario perché Application.Current è null in OnStartup di un plugin Revit:
        /// il load "globale" tentato in QtoApplication non funziona e bisogna caricare
        /// il tema al livello dell'UserControl. Guardie multi-livello: marker key per
        /// evitare double-load, try/catch per log diagnostico.
        /// </summary>
        private void EnsureThemeLoaded()
        {
            const string markerKey = "QtoThemeLoaded";
            if (Resources.Contains(markerKey)) return;

            try
            {
                var uri = new Uri("/QtoRevitPlugin;component/Theme/QtoTheme.xaml", UriKind.Relative);
                var theme = (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(uri);
                Resources.MergedDictionaries.Add(theme);
                Resources[markerKey] = true;

                // Propaga anche a Application.Current se disponibile (così window
                // fluttuanti/popup ereditano lo stile quando Application c'è).
                var app = System.Windows.Application.Current;
                if (app != null && !app.Resources.Contains(markerKey))
                {
                    app.Resources.MergedDictionaries.Add(theme);
                    app.Resources[markerKey] = true;
                }

                Services.CrashLogger.Info($"QtoDockablePane: theme caricato nel UserControl ({theme.Count} risorse)");
            }
            catch (Exception ex)
            {
                Services.CrashLogger.WriteException("QtoDockablePane.EnsureThemeLoaded", ex);
            }
        }

        private void BuildSwitcher()
        {
            var workflowStyle = TryFindResource("WorkflowNavButton") as Style;
            var utilityStyle = TryFindResource("SwitcherButton") as Style;

            if (workflowStyle == null || utilityStyle == null)
            {
                Services.CrashLogger.Warn(
                    "QtoDockablePane.BuildSwitcher: uno o più style di navigazione non trovati. " +
                    "Il tema QtoTheme.xaml non è stato caricato correttamente.");
            }

            foreach (var item in _vm.Views)
            {
                var btn = new ToggleButton
                {
                    Content = item.Label,
                    Tag = item
                };
                if (workflowStyle != null) btn.Style = workflowStyle;
                btn.Click += (_, _) => _vm.ActiveView = item;
                SwitcherHost.Children.Add(btn);
                _buttonCache[item.Key] = btn;
            }

            foreach (var item in _vm.SecondaryViews)
            {
                var btn = new ToggleButton
                {
                    Content = item.Label,
                    Tag = item
                };
                if (utilityStyle != null) btn.Style = utilityStyle;
                btn.Click += (_, _) => _vm.ActiveView = item;
                SecondarySwitcherHost.Children.Add(btn);
                _buttonCache[item.Key] = btn;
            }
        }

        private void UpdateActiveView()
        {
            // Nessun computo attivo: mostra empty state globale + disabilita switcher
            if (!_vm.HasActiveSession)
            {
                _noSessionView ??= CreateHomeView();
                ViewHost.Content = _noSessionView;

                foreach (var kv in _buttonCache)
                    kv.Value.IsChecked = kv.Key == QtoViewKey.Home;
                return;
            }

            var active = _vm.ActiveView;
            if (active == null) return;

            foreach (var kv in _buttonCache)
                kv.Value.IsChecked = kv.Key == active.Key;

            if (!_viewCache.TryGetValue(active.Key, out var view))
            {
                view = CreateViewFor(active);
                _viewCache[active.Key] = view;
            }

            ViewHost.Content = view;
        }

        private UserControl CreateViewFor(QtoViewItem item)
        {
            return item.Key switch
            {
                QtoViewKey.Home => CreateHomeView(),
                QtoViewKey.ProjectSetup => new ProjectInfoView(),
                QtoViewKey.PriceList => new SetupListinoView(),
                QtoViewKey.Verification => new PreviewView { DataContext = _vm },
                QtoViewKey.Preview => new PreviewView { DataContext = _vm },

                QtoViewKey.Setup => new SetupView(),

                QtoViewKey.Phase => new PhaseFilterView(),

                QtoViewKey.Selection => new SelectionView(),

                // Sprint 4 Task 3: MappingView condivide il tag "Tagging" finché non
                // creiamo una TaggingView dedicata in Sprint 5. Per ora la MappingView
                // permette CRUD in-memory di formule Room (Sorgente B) e voci manuali (Sorgente C)
                // + preview read-only delle famiglie (Sorgente A).
                QtoViewKey.Tagging => new MappingView(),

                QtoViewKey.ComputoStructure => new ComputoStructureView(),

                QtoViewKey.QtoViews => new PlaceholderView("Viste CME Dedicate", item.Reference, item.AvailableInSprint,
                    "Vista 3D isometrica CME + piante 2D per livello + 3 Schedule nativi " +
                    "(Assegnazioni / Mancanti / Nuovi Prezzi). Creazione idempotente, " +
                    "template applicati in cascata con override grafici per stato."),

                QtoViewKey.FilterManager => new PlaceholderView("Filtri Vista Nativi", item.Reference, item.AvailableInSprint,
                    "3 ParameterFilterElement persistenti: CME_Taggati (verde), CME_Mancanti (rosso), " +
                    "CME_Anomalie (grigio halftone). Applicabili a vista corrente, template o set di viste. " +
                    "Un singolo undo annulla l'intera operazione (TransactionGroup)."),

                QtoViewKey.Health => new PlaceholderView("Health Check", item.Reference, item.AvailableInSprint,
                    "Matrice 6 stati (Computato / Parziale / Non computato / Multi-EP / " +
                    "Escluso manuale / Escluso filtro) + AnomalyDetector z-score. " +
                    "Doppio click per navigare all'elemento critico in Revit."),

                QtoViewKey.Np => new PlaceholderView("Nuovo Prezzo", item.Reference, item.AvailableInSprint,
                    "Analisi prezzi strutturata secondo D.Lgs. 36/2023 All. II.14: " +
                    "CT (Manodopera + Materiali + Noli + Trasporti) × SG (13–17%) × Utile (10%). " +
                    "Workflow Bozza → Concordato → Approvato (RUP)."),

                // Step finale: lancia l'ExportWizardWindow (vedi ExportView.xaml.cs).
                // ExportView introdotta in main commit 3bb69bb — NON PlaceholderView.
                QtoViewKey.Export => new ExportView(),

                _ => new PlaceholderView("(sconosciuta)", "", 99, "View non ancora definita.")
            };
        }

        private UserControl CreateHomeView()
        {
            var view = new HomeView();
            view.NewSessionRequested += (_, _) => OnNewSession(view, new RoutedEventArgs());
            view.OpenSessionRequested += (_, _) => OnOpenSession(view, new RoutedEventArgs());
            view.ResumeLastSessionRequested += (_, _) => OnResumeLastSession(view, new RoutedEventArgs());
            return view;
        }

        // =====================================================================
        // Menu Sessione: apertura + handlers (file-based .cme)
        // =====================================================================

        private void OnSessionMenuClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void UpdateSessionMenuEnabled()
        {
            bool hasActive = _vm.HasActiveSession;

            // Menu item
            MiSave.IsEnabled = hasActive;
            MiSaveAs.IsEnabled = hasActive;
            MiRename.IsEnabled = hasActive;
            MiClose.IsEnabled = hasActive;
            MiDelete.IsEnabled = hasActive;

            // Switcher view: abilitati solo se c'è un computo aperto
            foreach (var kv in _buttonCache)
                kv.Value.IsEnabled = hasActive || kv.Key == QtoViewKey.Home;

            // Aggiorna contenuto (passa a empty state se sessione chiusa)
            UpdateActiveView();
        }

        // ---- NUOVO ----------------------------------------------------------

        private void OnNewSession(object sender, RoutedEventArgs e)
        {
            var doc = GetActiveDocument();
            if (doc == null) return;

            var dlg = new SaveFileDialog
            {
                Title = "Nuovo computo CME",
                Filter = SessionManager.FileFilter,
                DefaultExt = SessionManager.FileExtension,
                AddExtension = true,
                FileName = SuggestedFileName(doc),
                InitialDirectory = SuggestedInitialDir(doc)
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                QtoApplication.Instance.SessionManager.CreateSession(
                    dlg.FileName,
                    doc,
                    Path.GetFileNameWithoutExtension(dlg.FileName));
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore", $"Impossibile creare il file:\n{ex.Message}");
            }
        }

        // ---- APRI -----------------------------------------------------------

        private void OnOpenSession(object sender, RoutedEventArgs e)
        {
            var doc = GetActiveDocument();
            if (doc == null) return;

            var dlg = new OpenFileDialog
            {
                Title = "Apri computo CME",
                Filter = SessionManager.FileFilter,
                DefaultExt = SessionManager.FileExtension,
                InitialDirectory = SuggestedInitialDir(doc),
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                QtoApplication.Instance.SessionManager.OpenSession(dlg.FileName);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore apertura", $"Impossibile aprire il file:\n{ex.Message}");
            }
        }

        private void OnResumeLastSession(object sender, RoutedEventArgs e)
        {
            var doc = GetActiveDocument();
            if (doc == null) return;

            var settings = SettingsService.Load();
            var lastPath = settings.LastSessionFilePath;

            if (string.IsNullOrWhiteSpace(lastPath) || !File.Exists(lastPath))
            {
                settings.LastSessionFilePath = string.Empty;
                SettingsService.Save(settings);
                _vm.RefreshFromSession();
                TaskDialog.Show("CME", "Nessun ultimo computo disponibile da riprendere.");
                return;
            }

            try
            {
                QtoApplication.Instance.SessionManager.OpenSession(lastPath);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore apertura", $"Impossibile aprire il file:\n{ex.Message}");
            }
        }

        // ---- SALVA ----------------------------------------------------------

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var sessionMgr = QtoApplication.Instance.SessionManager;
            if (!sessionMgr.HasActiveSession) return;
            sessionMgr.Flush();
            QtoApplication.Instance.AutoSave?.FlushNow();
        }

        // ---- SALVA CON NOME -------------------------------------------------

        private void OnSaveAs(object sender, RoutedEventArgs e)
        {
            var sessionMgr = QtoApplication.Instance.SessionManager;
            if (!sessionMgr.HasActiveSession) return;

            var currentPath = sessionMgr.ActiveFilePath ?? "";
            var dlg = new SaveFileDialog
            {
                Title = "Salva computo con nome",
                Filter = SessionManager.FileFilter,
                DefaultExt = SessionManager.FileExtension,
                AddExtension = true,
                InitialDirectory = string.IsNullOrEmpty(currentPath) ? "" : Path.GetDirectoryName(currentPath),
                FileName = string.IsNullOrEmpty(currentPath)
                    ? "nuovo"
                    : Path.GetFileNameWithoutExtension(currentPath) + " - copia"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                sessionMgr.SaveAs(dlg.FileName);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore", $"Impossibile salvare con nome:\n{ex.Message}");
            }
        }

        // ---- RINOMINA -------------------------------------------------------

        private void OnRename(object sender, RoutedEventArgs e)
        {
            var sessionMgr = QtoApplication.Instance.SessionManager;
            if (!sessionMgr.HasActiveSession) return;

            var currentName = sessionMgr.ActiveSession!.SessionName;
            var newName = InputDialog.Prompt(
                "Rinomina computo",
                "Nuovo nome interno del computo (il file resta lo stesso):",
                currentName);
            if (string.IsNullOrWhiteSpace(newName) || newName == currentName) return;

            sessionMgr.RenameActiveSession(newName);
        }

        // ---- CHIUDI ---------------------------------------------------------

        private void OnClose(object sender, RoutedEventArgs e)
        {
            var sessionMgr = QtoApplication.Instance.SessionManager;
            if (!sessionMgr.HasActiveSession) return;
            sessionMgr.CloseSession();
        }

        // ---- IMPOSTAZIONI ---------------------------------------------------

        private void OnSettings(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog();
            new System.Windows.Interop.WindowInteropHelper(dlg).Owner =
                System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            dlg.ShowDialog();
        }

        // ---- ELIMINA --------------------------------------------------------

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            var sessionMgr = QtoApplication.Instance.SessionManager;
            if (!sessionMgr.HasActiveSession) return;

            var path = sessionMgr.ActiveFilePath!;
            var name = Path.GetFileName(path);

            var td = new TaskDialog("Elimina computo")
            {
                MainInstruction = $"Eliminare definitivamente il file «{name}»?",
                MainContent = $"Path:\n{path}\n\n" +
                              "Il file sarà rimosso dal disco. Questa azione non è reversibile.\n" +
                              "Il file Revit non viene toccato.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;

            try
            {
                sessionMgr.DeleteActiveFile();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore eliminazione", ex.Message);
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static Autodesk.Revit.DB.Document? GetActiveDocument()
        {
            var uiApp = QtoApplication.Instance.CurrentUiApp;
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("CME", "Apri un progetto Revit prima di operare sui computi.");
            }
            return doc;
        }

        /// <summary>Nome file suggerito: "{progetto} - computo {data}.cme"</summary>
        private static string SuggestedFileName(Autodesk.Revit.DB.Document doc)
        {
            var projectName = string.IsNullOrEmpty(doc.PathName)
                ? doc.Title
                : Path.GetFileNameWithoutExtension(doc.PathName);
            return $"{projectName} - computo {DateTime.Now:yyyyMMdd}{SessionManager.FileExtension}";
        }

        /// <summary>Cartella iniziale dialog: stessa del .rvt se salvato, altrimenti Documents.</summary>
        private static string SuggestedInitialDir(Autodesk.Revit.DB.Document doc)
        {
            if (!string.IsNullOrEmpty(doc.PathName))
            {
                var dir = Path.GetDirectoryName(doc.PathName);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
