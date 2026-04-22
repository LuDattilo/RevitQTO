using QtoRevitPlugin.Application;
using QtoRevitPlugin.UI.ViewModels;
using QtoRevitPlugin.UI.Views;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace QtoRevitPlugin.UI.Panes
{
    /// <summary>
    /// Hub UI del plug-in. Ospita la barra switcher per le 9 view + Preview (sempre presente).
    /// Registrato come Revit DockablePane (stato flottante by default), UI autocontenuta
    /// separata dal ribbon che contiene solo "Avvia CME".
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
        // Menu Sessione: apertura + handlers
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

            var sessionMgr = QtoApplication.Instance.SessionManager;
            if (sessionMgr.Repository == null) sessionMgr.BindToDocument(doc);

            var defaultName = $"Computo {DateTime.Now:yyyy-MM-dd HH:mm}";
            var name = InputDialog.Prompt("Nuovo computo", "Nome del nuovo computo:", defaultName);
            if (string.IsNullOrWhiteSpace(name)) return;

            sessionMgr.CreateSession(doc, name);
        }

        // ---- APRI -----------------------------------------------------------

        private void OnOpenSession(object sender, RoutedEventArgs e)
        {
            var doc = GetActiveDocument();
            if (doc == null) return;

            var sessionMgr = QtoApplication.Instance.SessionManager;
            if (sessionMgr.Repository == null) sessionMgr.BindToDocument(doc);

            var sessions = sessionMgr.GetSessionsForCurrentDocument(doc);
            if (sessions.Count == 0)
            {
                TaskDialog.Show("CME", "Non ci sono computi salvati per questo progetto.");
                return;
            }

            var projectLabel = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
            var dialog = new SessionListWindow(projectLabel, sessions);
            OwnToRevit(dialog);
            if (dialog.ShowDialog() != true) return;

            if (dialog.Result == SessionDialogResult.Resume)
            {
                sessionMgr.ResumeSession(dialog.SelectedSessionId);
            }
            else if (dialog.Result == SessionDialogResult.NewSession)
            {
                OnNewSession(sender, e);
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

            var currentName = sessionMgr.ActiveSession!.SessionName;
            var newName = InputDialog.Prompt("Salva con nome",
                "Nome del nuovo computo (duplicato del corrente):",
                $"{currentName} – copia");
            if (string.IsNullOrWhiteSpace(newName)) return;

            sessionMgr.ForkSession(newName);
        }

        // ---- RINOMINA -------------------------------------------------------

        private void OnRename(object sender, RoutedEventArgs e)
        {
            var sessionMgr = QtoApplication.Instance.SessionManager;
            if (!sessionMgr.HasActiveSession) return;

            var currentName = sessionMgr.ActiveSession!.SessionName;
            var newName = InputDialog.Prompt("Rinomina computo",
                "Nuovo nome per il computo corrente:",
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

            var name = sessionMgr.ActiveSession!.SessionName;
            var td = new TaskDialog("Elimina computo")
            {
                MainInstruction = $"Eliminare definitivamente il computo «{name}»?",
                MainContent = "Tutti i dati collegati (assegnazioni, voci manuali, NP) saranno rimossi.\n" +
                              "Il file Revit non viene toccato — l'azione impatta solo il database CME locale.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;

            sessionMgr.DeleteSession(sessionMgr.ActiveSession.Id);
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
                TaskDialog.Show("CME", "Apri un progetto Revit prima di operare sulle sessioni.");
            }
            return doc;
        }

        private static void OwnToRevit(Window window)
        {
            try
            {
                var helper = new WindowInteropHelper(window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { }
        }
    }
}
