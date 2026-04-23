using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    public enum QtoViewKey
    {
        Home,
        ProjectSetup,
        PriceList,
        Verification,
        Preview,
        Setup,
        Phase,
        Selection,
        Tagging,
        QtoViews,
        FilterManager,
        Health,
        Np,
        Export,
        ComputoStructure
    }

    public class QtoViewItem : ObservableObject
    {
        public QtoViewItem(QtoViewKey key, string label, string reference, int availableInSprint)
        {
            Key = key;
            Label = label;
            Reference = reference;
            AvailableInSprint = availableInSprint;
        }

        public QtoViewKey Key { get; }
        public string Label { get; }
        /// <summary>Riferimento alla spec — es. "§I3 · Fase 2".</summary>
        public string Reference { get; }
        /// <summary>Sprint in cui la view diventa operativa. 1 = già attiva.</summary>
        public int AvailableInSprint { get; }
    }

    /// <summary>
    /// VM radice del DockablePane. Gestisce:
    ///  - switcher tra le 9 viste
    ///  - binding alla sessione attiva del SessionManager (header + status bar)
    /// Le singole view sono UserControl con proprio VM, ospitate nel ContentPresenter.
    /// </summary>
    public partial class DockablePaneViewModel : ViewModelBase
    {
        private readonly SessionManager _sessionManager;
        private readonly UserLibraryManager _userLibrary;
        private readonly WorkflowStateEvaluator _workflowStateEvaluator = new WorkflowStateEvaluator();

        public ObservableCollection<QtoViewItem> Views { get; } = new();
        public ObservableCollection<QtoViewItem> SecondaryViews { get; } = new();
        public ObservableCollection<string> HomeWorkflowSteps { get; } = new()
        {
            "1 Setup progetto",
            "2 Listino",
            "3 Selezione",
            "4 Tagging",
            "5 Verifica",
            "6 Export"
        };

        [ObservableProperty]
        private QtoViewItem? _activeView;

        [ObservableProperty]
        private string _sessionTitle = "Nessuna sessione attiva";

        [ObservableProperty]
        private string _projectSubtitle = "Avvia una sessione dal ribbon";

        [ObservableProperty]
        private string _statusMessage = "Pronto";

        [ObservableProperty]
        private int _totalElements;

        [ObservableProperty]
        private int _taggedElements;

        [ObservableProperty]
        private double _totalAmount;

        [ObservableProperty]
        private bool _hasActiveSession;

        [ObservableProperty]
        private string _homePrimaryMessage = "Per iniziare serve un computo attivo";

        [ObservableProperty]
        private string _homeSecondaryMessage = "Crea o apri un file .cme per attivare il workflow CME";

        [ObservableProperty]
        private bool _canResumeLastSession;

        [ObservableProperty]
        private string _lastSessionHint = "Nessun ultimo computo disponibile";

        [ObservableProperty]
        private string _previewPhaseContext = "Nessuna fase attiva.";

        public double TaggedPercent => TotalElements > 0
            ? (double)TaggedElements / TotalElements * 100.0
            : 0.0;

        public string ProgressText => TotalElements > 0
            ? $"{TaggedElements}/{TotalElements}  ({TaggedPercent:F1}%)"
            : "—";

        public string AmountText => $"€ {TotalAmount:N2}";

        public DockablePaneViewModel(SessionManager sessionManager, UserLibraryManager userLibrary)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _userLibrary = userLibrary ?? throw new ArgumentNullException(nameof(userLibrary));
            BuildViewList();
            _sessionManager.SessionChanged += OnSessionChanged;
            SettingsService.SettingsChanged += (_, _) => RefreshWorkflowState();
            RefreshFromSession();
        }

        private void BuildViewList()
        {
            Views.Add(new QtoViewItem(QtoViewKey.Home, "Home", "Avvio", 1));
            Views.Add(new QtoViewItem(QtoViewKey.ProjectSetup, "Setup progetto", "§Fase 1", 2));
            Views.Add(new QtoViewItem(QtoViewKey.PriceList, "Listino", "§Fase 1 · Listino", 2));
            Views.Add(new QtoViewItem(QtoViewKey.Selection, "Selezione", "§I3", 4));
            Views.Add(new QtoViewItem(QtoViewKey.Tagging, "Tagging", "§I1·I2·I12·I13", 5));
            Views.Add(new QtoViewItem(QtoViewKey.Verification, "Verifica", "§Fase 11", 1));
            Views.Add(new QtoViewItem(QtoViewKey.Export, "Esporta", "§Fase 12", 9));

            SecondaryViews.Add(new QtoViewItem(QtoViewKey.Health, "Health", "§I5", 6));
            SecondaryViews.Add(new QtoViewItem(QtoViewKey.FilterManager, "Filtri Vista", "§I11", 9));
            SecondaryViews.Add(new QtoViewItem(QtoViewKey.QtoViews, "Viste CME", "§I14", 9));

            ActiveView = Views.First(v => v.Key == QtoViewKey.Home);
        }

        private void OnSessionChanged(object? sender, SessionChangedEventArgs e)
        {
            if (e.Kind is SessionChangeKind.Created or SessionChangeKind.Resumed or SessionChangeKind.Forked
                or SessionChangeKind.Closed or SessionChangeKind.Deleted)
            {
                NavigateTo(QtoViewKey.Home);
            }

            RefreshFromSession();
        }

        public void RefreshFromSession()
        {
            var session = _sessionManager.ActiveSession;
            if (session == null)
            {
                HasActiveSession = false;
                SessionTitle = "Nessuna sessione attiva";
                ProjectSubtitle = "Usa il menu «Sessione ▾» per creare o aprire un computo .cme";
                TotalElements = 0;
                TaggedElements = 0;
                TotalAmount = 0;
                StatusMessage = "Pronto";
            }
            else
            {
                HasActiveSession = true;
                SessionTitle = string.IsNullOrWhiteSpace(session.SessionName) ? "(sessione)" : session.SessionName;
                ProjectSubtitle = session.ProjectName;
                TotalElements = session.TotalElements;
                TaggedElements = session.TaggedElements;
                TotalAmount = session.TotalAmount;
                StatusMessage = session.LastSavedAt.HasValue
                    ? $"Salvato {session.LastSavedAt.Value.ToLocalTime():HH:mm}"
                    : "Sessione creata";
                PreviewPhaseContext = string.IsNullOrWhiteSpace(session.ActivePhaseName)
                    ? "Nessuna fase attiva."
                    : $"Contesto fase corrente: «{session.ActivePhaseName}».";
            }

            if (session == null)
                PreviewPhaseContext = "Nessuna fase attiva.";

            RefreshWorkflowState();
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(AmountText));
            OnPropertyChanged(nameof(TaggedPercent));
        }

        partial void OnTotalElementsChanged(int value) => OnPropertyChanged(nameof(ProgressText));
        partial void OnTaggedElementsChanged(int value) => OnPropertyChanged(nameof(ProgressText));
        partial void OnTotalAmountChanged(double value) => OnPropertyChanged(nameof(AmountText));

        /// <summary>Cambia view dal codice (es. dopo azione "Apri Health Check" da un bottone contestuale).</summary>
        public void NavigateTo(QtoViewKey key)
        {
            var target = Views.Concat(SecondaryViews).FirstOrDefault(v => v.Key == key);
            if (target != null) ActiveView = target;
        }

        private void RefreshWorkflowState()
        {
            var workflow = _workflowStateEvaluator.Evaluate(HasActiveSession, HasActivePriceList());
            HomePrimaryMessage = workflow.PrimaryMessage;
            HomeSecondaryMessage = workflow.SecondaryMessage;

            var lastPath = SettingsService.Load().LastSessionFilePath;
            CanResumeLastSession = !string.IsNullOrWhiteSpace(lastPath) && File.Exists(lastPath);
            LastSessionHint = CanResumeLastSession
                ? Path.GetFileNameWithoutExtension(lastPath)
                : "Nessun ultimo computo disponibile";
        }

        private bool HasActivePriceList()
        {
            try
            {
                return _userLibrary.Library.GetPriceLists().Any(x => x.IsActive);
            }
            catch
            {
                return false;
            }
        }
    }
}
