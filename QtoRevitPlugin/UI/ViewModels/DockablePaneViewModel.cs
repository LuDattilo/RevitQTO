using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    public enum QtoViewKey
    {
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

        public ObservableCollection<QtoViewItem> Views { get; } = new();

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

        public double TaggedPercent => TotalElements > 0
            ? (double)TaggedElements / TotalElements * 100.0
            : 0.0;

        public string ProgressText => TotalElements > 0
            ? $"{TaggedElements}/{TotalElements}  ({TaggedPercent:F1}%)"
            : "—";

        public string AmountText => $"€ {TotalAmount:N2}";

        public DockablePaneViewModel(SessionManager sessionManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            BuildViewList();
            _sessionManager.SessionChanged += OnSessionChanged;
            RefreshFromSession();
        }

        private void BuildViewList()
        {
            // Ordine logico di workflow: prima configuri, poi selezioni e vedi preview,
            // poi tagghi, poi verifichi (health/filtri/viste), poi NP, poi export.
            Views.Add(new QtoViewItem(QtoViewKey.Setup, "Setup", "§Fase 1", 2));
            Views.Add(new QtoViewItem(QtoViewKey.Phase, "Fasi", "§I9", 4));
            Views.Add(new QtoViewItem(QtoViewKey.Selection, "Selezione", "§I3", 4));
            Views.Add(new QtoViewItem(QtoViewKey.Preview, "Preview", "§Fase 11", 1));
            Views.Add(new QtoViewItem(QtoViewKey.Tagging, "Tagging", "§I1·I2·I12·I13", 5));
            Views.Add(new QtoViewItem(QtoViewKey.ComputoStructure, "Struttura Computo", "§Sprint9", 9));
            Views.Add(new QtoViewItem(QtoViewKey.Health, "Health", "§I5", 6));
            Views.Add(new QtoViewItem(QtoViewKey.FilterManager, "Filtri Vista", "§I11", 9));
            Views.Add(new QtoViewItem(QtoViewKey.QtoViews, "Viste CME", "§I14", 9));
            Views.Add(new QtoViewItem(QtoViewKey.Np, "Nuovo Prezzo", "§I8", 8));
            Views.Add(new QtoViewItem(QtoViewKey.Export, "Export", "§Fase 12", 9));

            ActiveView = Views.First(v => v.Key == QtoViewKey.Preview);   // Preview come default
        }

        private void OnSessionChanged(object? sender, SessionChangedEventArgs e)
        {
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
            }

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
            var target = Views.FirstOrDefault(v => v.Key == key);
            if (target != null) ActiveView = target;
        }
    }
}
