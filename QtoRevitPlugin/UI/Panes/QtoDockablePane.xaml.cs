using QtoRevitPlugin.UI.ViewModels;
using QtoRevitPlugin.UI.Views;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace QtoRevitPlugin.UI.Panes
{
    /// <summary>
    /// Hub UI del plug-in. Ospita la barra switcher per le 9 view + Preview (sempre presente).
    /// Registrato come Revit DockablePane (stato flottante by default), UI autocontenuta
    /// separata dal ribbon che contiene solo "Avvia QTO".
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
            };

            UpdateActiveView();
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

            // Aggiorna stato ToggleButton
            foreach (var kv in _buttonCache)
                kv.Value.IsChecked = kv.Key == active.Key;

            // Lazy-load della view
            if (!_viewCache.TryGetValue(active.Key, out var view))
            {
                view = CreateViewFor(active);
                _viewCache[active.Key] = view;
            }

            ViewHost.Content = view;
        }

        private UserControl CreateViewFor(QtoViewItem item)
        {
            // Preview è l'unica view attiva allo Sprint 1.5.
            // Le altre 8 sono PlaceholderView parametrizzate con title/reference/sprint/description.
            return item.Key switch
            {
                QtoViewKey.Preview => new PreviewView { DataContext = _vm },

                QtoViewKey.Setup => new PlaceholderView(
                    "Setup",
                    item.Reference,
                    item.AvailableInSprint,
                    "Caricamento listini multi-prezzario, regole di misurazione " +
                    "(vuoto per pieno, deduzioni aperture), regole di esclusione globale, " +
                    "configurazione altezza locali per Sorgente B."),

                QtoViewKey.Phase => new PlaceholderView(
                    "Filtro Fase Revit",
                    item.Reference,
                    item.AvailableInSprint,
                    "Step 0 obbligatorio: selezione delle fasi di lavoro " +
                    "(Nuova costruzione / Demolizioni / Esistente). Il capitolo Demolizioni " +
                    "del listino si apre automaticamente quando si lavora su elementi demoliti."),

                QtoViewKey.Selection => new PlaceholderView(
                    "Selezione Elementi",
                    item.Reference,
                    item.AvailableInSprint,
                    "FilterBuilder stile Revit con regole parametriche, ricerca testuale inline, " +
                    "preset filtri salvabili. Comandi Isola / Nascondi / Togli isolamento."),

                QtoViewKey.Tagging => new PlaceholderView(
                    "Assegnazione EP",
                    item.Reference,
                    item.AvailableInSprint,
                    "3 sorgenti di quantità: (A) famiglie Revit con multi-EP, " +
                    "(B) Room/Space con formula NCalc, (C) voci manuali svincolate dal modello. " +
                    "Scrittura bidirezionale dei parametri QTO_Codice / QTO_DescrizioneBreve / QTO_Stato."),

                QtoViewKey.QtoViews => new PlaceholderView(
                    "Viste QTO Dedicate",
                    item.Reference,
                    item.AvailableInSprint,
                    "Vista 3D isometrica QTO + piante 2D per livello + 3 Schedule nativi " +
                    "(Assegnazioni / Mancanti / Nuovi Prezzi). Creazione idempotente, " +
                    "template applicati in cascata con override grafici per stato."),

                QtoViewKey.FilterManager => new PlaceholderView(
                    "Filtri Vista Nativi",
                    item.Reference,
                    item.AvailableInSprint,
                    "3 ParameterFilterElement persistenti: QTO_Taggati (verde), QTO_Mancanti (rosso), " +
                    "QTO_Anomalie (grigio halftone). Applicabili a vista corrente, template o set di viste. " +
                    "Un singolo undo annulla l'intera operazione (TransactionGroup)."),

                QtoViewKey.Health => new PlaceholderView(
                    "Health Check",
                    item.Reference,
                    item.AvailableInSprint,
                    "Matrice 6 stati (Computato / Parziale / Non computato / Multi-EP / " +
                    "Escluso manuale / Escluso filtro) + AnomalyDetector z-score. " +
                    "Doppio click per navigare all'elemento critico in Revit."),

                QtoViewKey.Np => new PlaceholderView(
                    "Nuovo Prezzo",
                    item.Reference,
                    item.AvailableInSprint,
                    "Analisi prezzi strutturata secondo D.Lgs. 36/2023 All. II.14: " +
                    "CT (Manodopera + Materiali + Noli + Trasporti) × SG (13–17%) × Utile (10%). " +
                    "Workflow Bozza → Concordato → Approvato (RUP)."),

                QtoViewKey.Export => new PlaceholderView(
                    "Esporta Computo",
                    item.Reference,
                    item.AvailableInSprint,
                    "Formato primario XPWE con gerarchia Capitoli/Sottocapitoli preservata " +
                    "(import diretto in PriMus). Secondari: Excel .xlsx con foglio analisi NP, " +
                    "TSV per compatibilità SA, Delta report dall'ultimo export."),

                _ => new PlaceholderView("(sconosciuta)", "", 99, "View non ancora definita.")
            };
        }
    }
}
