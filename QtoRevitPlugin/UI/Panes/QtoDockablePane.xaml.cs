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

        public QtoDockablePane(DockablePaneViewModel vm)
        {
            _vm = vm;
            DataContext = _vm;
            InitializeComponent();

            BuildSwitcher();
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DockablePaneViewModel.ActiveView))
                    UpdateActiveView();
                if (e.PropertyName == nameof(DockablePaneViewModel.HasActiveSession))
                    UpdateSessionMenuEnabled();
            };

            UpdateActiveView();
            UpdateSessionMenuEnabled();
        }

        private void BuildSwitcher()
        {
            foreach (var item in _vm.Views)
            {
                var btn = new ToggleButton
                {
                    Content = item.Label,
                    Tag = item,
                    Style = (Style)FindResource("SwitcherButton")
                };
                btn.Click += (_, _) => _vm.ActiveView = item;
                SwitcherHost.Children.Add(btn);
                _buttonCache[item.Key] = btn;
            }
        }

        private void UpdateActiveView()
        {
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
                QtoViewKey.Preview => new PreviewView { DataContext = _vm },

                QtoViewKey.Setup => new PlaceholderView("Setup", item.Reference, item.AvailableInSprint,
                    "Caricamento listini multi-prezzario, regole di misurazione " +
                    "(vuoto per pieno, deduzioni aperture), regole di esclusione globale, " +
                    "configurazione altezza locali per Sorgente B."),

                QtoViewKey.Phase => new PlaceholderView("Filtro Fase Revit", item.Reference, item.AvailableInSprint,
                    "Step 0 obbligatorio: selezione delle fasi di lavoro " +
                    "(Nuova costruzione / Demolizioni / Esistente). Il capitolo Demolizioni " +
                    "del listino si apre automaticamente quando si lavora su elementi demoliti."),

                QtoViewKey.Selection => new PlaceholderView("Selezione Elementi", item.Reference, item.AvailableInSprint,
                    "FilterBuilder stile Revit con regole parametriche, ricerca testuale inline, " +
                    "preset filtri salvabili. Comandi Isola / Nascondi / Togli isolamento."),

                QtoViewKey.Tagging => new PlaceholderView("Assegnazione EP", item.Reference, item.AvailableInSprint,
                    "3 sorgenti di quantità: (A) famiglie Revit con multi-EP, " +
                    "(B) Room/Space con formula NCalc, (C) voci manuali svincolate dal modello. " +
                    "Scrittura bidirezionale dei parametri CME_Codice / CME_Descrizione / CME_Stato."),

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

                QtoViewKey.Export => new PlaceholderView("Esporta Computo", item.Reference, item.AvailableInSprint,
                    "Formato primario XPWE con gerarchia Capitoli/Sottocapitoli preservata " +
                    "(import diretto in PriMus). Secondari: Excel .xlsx con foglio analisi NP, " +
                    "TSV per compatibilità SA, Delta report dall'ultimo export."),

                _ => new PlaceholderView("(sconosciuta)", "", 99, "View non ancora definita.")
            };
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
            MiSave.IsEnabled = hasActive;
            MiSaveAs.IsEnabled = hasActive;
            MiRename.IsEnabled = hasActive;
            MiClose.IsEnabled = hasActive;
            MiDelete.IsEnabled = hasActive;
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
