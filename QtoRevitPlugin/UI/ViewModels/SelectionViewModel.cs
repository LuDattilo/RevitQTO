using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// VM per la SelectionView (§I3): dropdown fase Revit + modalità computo +
    /// dropdown categoria + ricerca nome + filtri parametrici + tabella elementi +
    /// comandi Isola/Nascondi/Reset.
    /// </summary>
    public partial class SelectionViewModel : ViewModelBase
    {
        private readonly SelectionService _service = new SelectionService();
        private readonly DispatcherTimer _searchDebounce;
        private bool _isRefreshingPhaseSelection;

        public ObservableCollection<CategoryItemVm> Categories { get; } = new();
        public ObservableCollection<PhaseItemVm> AvailablePhases { get; } = new();
        public ObservableCollection<ComputationModeOptionVm> ComputationModes { get; } = new();
        public ObservableCollection<ElementRowVm> Elements { get; } = new();
        public ObservableCollection<ParamFilterRuleVm> ParamRules { get; } = new ObservableCollection<ParamFilterRuleVm>();

        [ObservableProperty] private CategoryItemVm? _selectedCategory;
        [ObservableProperty] private PhaseItemVm? _selectedPhase;
        [ObservableProperty] private string _nameQuery = string.Empty;
        [ObservableProperty] private string _statusMessage = "Seleziona una fase Revit e una categoria";
        [ObservableProperty] private int _activePhaseId;
        [ObservableProperty] private string _activePhaseName = "";
        [ObservableProperty] private SelectionComputationMode _computationMode = SelectionComputationMode.NewAndExisting;
        [ObservableProperty] private bool _hasParamRules;

        public SelectionViewModel()
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _searchDebounce.Tick += OnSearchDebounceTick;

            foreach (var (bic, label) in SelectionService.PopularCategories)
                Categories.Add(new CategoryItemVm(bic, label));
            ComputationModes.Add(new ComputationModeOptionVm(SelectionComputationMode.NewAndExisting, "Nuovo + Esistente"));
            ComputationModes.Add(new ComputationModeOptionVm(SelectionComputationMode.Demolitions, "Demolizioni"));

            if (QtoApplication.Instance?.SessionManager != null)
            {
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => RefreshFromSession();
            }
            RefreshFromSession();
            ParamRules.CollectionChanged += (_, _) => UpdateHasParamRules();
        }

        public void RefreshFromSession()
        {
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            RefreshPhaseOptions(session);
        }

        partial void OnSelectedCategoryChanged(CategoryItemVm? value)
        {
            Search();
        }

        partial void OnNameQueryChanged(string value)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        partial void OnSelectedPhaseChanged(PhaseItemVm? value)
        {
            if (_isRefreshingPhaseSelection || value == null)
                return;

            ActivePhaseId = value.PhaseId;
            ActivePhaseName = value.Name;
            PersistSelectedPhase(value);
            Search();
        }

        partial void OnComputationModeChanged(SelectionComputationMode value)
        {
            OnPropertyChanged(nameof(ComputationModeLabel));
            Search();
        }

        private void OnSearchDebounceTick(object? sender, EventArgs e)
        {
            _searchDebounce.Stop();
            Search();
        }

        /// <summary>Esegue la query con i parametri correnti e popola <see cref="Elements"/>.</summary>
        public void Search()
        {
            Elements.Clear();
            if (SelectedPhase == null)
            {
                StatusMessage = "Seleziona una fase Revit per cominciare.";
                return;
            }

            if (SelectedCategory == null)
            {
                StatusMessage = "Seleziona una categoria per cominciare.";
                return;
            }

            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                StatusMessage = "Nessun documento Revit attivo.";
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var rules = ParamRules
                    .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName) && !string.IsNullOrWhiteSpace(r.Value))
                    .Select(r => r.ToModel())
                    .ToList();

                var results = _service.FindElements(
                    doc,
                    SelectedCategory.Bic,
                    NameQuery,
                    SelectedPhase.PhaseId,
                    ComputationMode,
                    rules);
                sw.Stop();

                foreach (var info in results)
                    Elements.Add(new ElementRowVm(info));

                var rulesLabel = rules.Count > 0 ? $" · {rules.Count} filtro/i param." : "";
                StatusMessage = $"{results.Count} elementi · categoria «{SelectedCategory.Label}»" +
                                $" · fase «{ActivePhaseName}»" +
                                $" · modalità «{ComputationModeLabel}»" +
                                rulesLabel +
                                $" · {sw.ElapsedMilliseconds} ms";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore ricerca: {ex.Message}";
            }
        }

        /// <summary>Gli Id attualmente mostrati (dopo filtro).</summary>
        public IEnumerable<int> CurrentElementIds() => Elements.Select(e => e.ElementId);

        /// <summary>Isola gli elementi filtrati correnti sulla vista attiva.</summary>
        public void IsolateCurrent()
        {
            var uidoc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument;
            if (uidoc == null) { StatusMessage = "Nessun documento attivo."; return; }
            var ids = CurrentElementIds().ToList();
            if (ids.Count == 0) { StatusMessage = "Nessun elemento da isolare."; return; }

            _service.IsolateElements(uidoc, ids);
            StatusMessage = $"Isolati {ids.Count} elementi nella vista corrente.";
        }

        /// <summary>Nasconde gli elementi filtrati correnti.</summary>
        public void HideCurrent()
        {
            var uidoc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument;
            if (uidoc == null) { StatusMessage = "Nessun documento attivo."; return; }
            var ids = CurrentElementIds().ToList();
            if (ids.Count == 0) { StatusMessage = "Nessun elemento da nascondere."; return; }

            _service.HideElements(uidoc, ids);
            StatusMessage = $"Nascosti {ids.Count} elementi nella vista corrente.";
        }

        /// <summary>Rimuove isola/nascondi temporanei dalla vista.</summary>
        public void ResetView()
        {
            var uidoc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument;
            if (uidoc == null) return;
            _service.ResetTemporaryView(uidoc);
            StatusMessage = "Vista ripristinata.";
        }

        /// <summary>Seleziona in Revit l'elemento (singolo) — utile per vedere i parametri nel Properties panel.</summary>
        public void SelectInRevit(int elementId)
        {
            var uidoc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument;
            if (uidoc == null) return;
            _service.SelectInRevit(uidoc, new[] { elementId });
        }

        public string ComputationModeLabel =>
            ComputationMode == SelectionComputationMode.Demolitions
                ? "Demolizioni"
                : "Nuovo + Esistente";

        [RelayCommand]
        private void AddParamRule()
        {
            var rule = new ParamFilterRuleVm();
            // Debounce ricerca quando l'utente edita un campo della regola
            rule.PropertyChanged += (_, _) =>
            {
                _searchDebounce.Stop();
                _searchDebounce.Start();
            };
            ParamRules.Add(rule);
            UpdateHasParamRules();
        }

        [RelayCommand]
        private void RemoveParamRule(ParamFilterRuleVm? rule)
        {
            if (rule == null) return;
            ParamRules.Remove(rule);
            UpdateHasParamRules();
            Search();
        }

        private void UpdateHasParamRules() =>
            HasParamRules = ParamRules.Any(r =>
                !string.IsNullOrWhiteSpace(r.ParameterName) &&
                !string.IsNullOrWhiteSpace(r.Value));

        private void RefreshPhaseOptions(WorkSession? session)
        {
            AvailablePhases.Clear();

            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                ActivePhaseId = 0;
                ActivePhaseName = "";
                _isRefreshingPhaseSelection = true;
                SelectedPhase = null;
                _isRefreshingPhaseSelection = false;
                return;
            }

            var phases = new PhaseService(doc).GetAvailablePhases();
            foreach (var phase in phases)
                AvailablePhases.Add(new PhaseItemVm(phase));

            var selected = session != null && session.ActivePhaseId > 0
                ? AvailablePhases.FirstOrDefault(x => x.PhaseId == session.ActivePhaseId)
                : AvailablePhases.FirstOrDefault();

            _isRefreshingPhaseSelection = true;
            SelectedPhase = selected;
            _isRefreshingPhaseSelection = false;

            if (selected != null)
            {
                ActivePhaseId = selected.PhaseId;
                ActivePhaseName = selected.Name;

                if (session != null && session.ActivePhaseId != selected.PhaseId)
                    PersistSelectedPhase(selected);
            }
            else
            {
                ActivePhaseId = 0;
                ActivePhaseName = "";
            }
        }

        private static void PersistSelectedPhase(PhaseItemVm selectedPhase)
        {
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (session == null)
                return;

            session.ActivePhaseId = selectedPhase.PhaseId;
            session.ActivePhaseName = selectedPhase.Name;
            // Notifica tutte le view phase-bound (ComputoStructure, Verifica, ecc.)
            // che la fase attiva è cambiata → soft-switch senza ricaricare il computo.
            QtoApplication.Instance!.SessionManager.NotifyActivePhaseChanged();
        }
    }

    public class CategoryItemVm
    {
        public CategoryItemVm(BuiltInCategory bic, string label)
        {
            Bic = bic;
            Label = label;
        }
        public BuiltInCategory Bic { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    public class ElementRowVm
    {
        public ElementRowVm(ElementRowInfo info)
        {
            ElementId = info.ElementId;
            UniqueId = info.UniqueId;
            Category = info.Category;
            FamilyName = info.FamilyName;
            TypeName = info.TypeName;
            LevelName = info.LevelName;
            PhaseCreatedName = info.PhaseCreatedName;
            PhaseDemolishedName = info.PhaseDemolishedName;
        }

        public int ElementId { get; }
        public string UniqueId { get; }
        public string Category { get; }
        public string FamilyName { get; }
        public string TypeName { get; }
        public string LevelName { get; }
        public string PhaseCreatedName { get; }
        public string PhaseDemolishedName { get; }
    }

    public class ComputationModeOptionVm
    {
        public ComputationModeOptionVm(SelectionComputationMode mode, string label)
        {
            Mode = mode;
            Label = label;
        }

        public SelectionComputationMode Mode { get; }
        public string Label { get; }
    }
}
