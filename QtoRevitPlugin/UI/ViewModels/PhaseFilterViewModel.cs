using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// VM per la PhaseFilterView. Carica le fasi del documento Revit attivo,
    /// calcola lazy il count elementi per fase, espone la fase attualmente selezionata
    /// (persistita su <see cref="WorkSession.ActivePhaseId"/>).
    /// </summary>
    public partial class PhaseFilterViewModel : ViewModelBase
    {
        public ObservableCollection<PhaseItemVm> Phases { get; } = new();

        [ObservableProperty] private PhaseItemVm? _selectedPhase;
        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private bool _hasDocument;

        public bool HasSessionActive => QtoApplication.Instance?.SessionManager?.HasActiveSession ?? false;

        public PhaseFilterViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
            {
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => Refresh();
            }
            Refresh();
        }

        /// <summary>
        /// (Re)carica la lista fasi dal documento Revit attivo + allinea la selezione corrente
        /// con la ActivePhaseId della sessione.
        /// </summary>
        public void Refresh()
        {
            Phases.Clear();
            SelectedPhase = null;

            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                HasDocument = false;
                StatusMessage = "Nessun documento Revit attivo.";
                return;
            }
            HasDocument = true;

            try
            {
                var service = new PhaseService(doc);
                var phases = service.GetAvailablePhases();

                foreach (var p in phases)
                    Phases.Add(new PhaseItemVm(p));

                // Seleziona la fase attiva della sessione se presente
                var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
                if (session != null && session.ActivePhaseId > 0)
                {
                    SelectedPhase = Phases.FirstOrDefault(x => x.PhaseId == session.ActivePhaseId);
                }

                StatusMessage = phases.Count == 0
                    ? "Nessuna fase trovata nel documento."
                    : $"{phases.Count} fase(i) disponibili" +
                      (SelectedPhase != null ? $" · attiva: «{SelectedPhase.Name}»" : "");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore lettura fasi: {ex.Message}";
            }
        }

        /// <summary>
        /// Calcola lazily il count elementi per ogni fase (operazione più pesante della sola
        /// enumerazione: richiede FilteredElementCollector per-fase). Chiamare solo quando serve.
        /// </summary>
        public void ComputeElementCounts()
        {
            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null) return;

            var service = new PhaseService(doc);
            foreach (var vm in Phases)
            {
                try { vm.ElementCount = service.CountComputableElementsInPhase(vm.PhaseId); }
                catch { vm.ElementCount = -1; }
            }
        }

        /// <summary>
        /// Conferma la fase selezionata salvandola sulla sessione attiva
        /// (<see cref="WorkSession.ActivePhaseId"/> + ActivePhaseName).
        /// </summary>
        public void ConfirmSelectedPhase()
        {
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (session == null)
            {
                StatusMessage = "Nessun computo attivo: apri un .cme prima di confermare la fase.";
                return;
            }
            if (SelectedPhase == null)
            {
                StatusMessage = "Seleziona una fase prima di confermare.";
                return;
            }

            session.ActivePhaseId = SelectedPhase.PhaseId;
            session.ActivePhaseName = SelectedPhase.Name;
            QtoApplication.Instance!.SessionManager.Flush();

            StatusMessage = $"Fase attiva: «{SelectedPhase.Name}» · salvata sul computo.";
        }

        partial void OnSelectedPhaseChanged(PhaseItemVm? value)
        {
            // Sincronizza IsSelected sulle altre PhaseItemVm (single-select UX)
            foreach (var vm in Phases)
                vm.IsSelected = ReferenceEquals(vm, value);

            if (value != null)
                StatusMessage = $"Selezionata «{value.Name}» — clicca «Conferma» per salvare sulla sessione.";
        }
    }

    /// <summary>
    /// Wrapper ObservableObject di <see cref="PhaseInfo"/> per binding UI.
    /// </summary>
    public partial class PhaseItemVm : ObservableObject
    {
        private readonly PhaseInfo _info;

        public PhaseItemVm(PhaseInfo info) { _info = info; }

        public int PhaseId => _info.PhaseId;
        public string Name => _info.Name;
        public int Sequence => _info.Sequence;
        public string Description => _info.Description;

        [ObservableProperty] private int? _elementCount;

        /// <summary>True quando questa fase è quella correntemente selezionata (RadioButton bound).</summary>
        [ObservableProperty] private bool _isSelected;

        public string ElementCountLabel => ElementCount switch
        {
            null => "—",
            -1 => "errore",
            _ => $"{ElementCount} elem."
        };

        partial void OnElementCountChanged(int? value) => OnPropertyChanged(nameof(ElementCountLabel));
    }
}
