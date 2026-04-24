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

        // Cache dei valori distinti per (categoria, parametro). Invalidata al cambio categoria.
        private readonly Dictionary<(BuiltInCategory Bic, string Param), IReadOnlyList<string>> _valuesCache = new();

        // Mappa DisplayName → FilterableParam (contiene BuiltInParameter per risoluzione
        // language-independent). Ripopolata ad ogni cambio categoria.
        private readonly Dictionary<string, FilterableParam> _paramIndex =
            new Dictionary<string, FilterableParam>(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<CategoryItemVm> Categories { get; } = new();
        public ObservableCollection<PhaseItemVm> AvailablePhases { get; } = new();
        public ObservableCollection<ComputationModeOptionVm> ComputationModes { get; } = new();
        public ObservableCollection<ElementRowVm> Elements { get; } = new();
        public ObservableCollection<ParamFilterRuleVm> ParamRules { get; } = new ObservableCollection<ParamFilterRuleVm>();

        /// <summary>
        /// Colonne personalizzate (parametri Revit) aggiunte via menu contestuale sulle intestazioni.
        /// La View osserva i cambi di questa collection e crea/rimuove DataGridColumn dinamicamente.
        /// </summary>
        public ObservableCollection<FilterableParam> CustomColumns { get; } = new ObservableCollection<FilterableParam>();

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
            ComputationModes.Add(new ComputationModeOptionVm(SelectionComputationMode.NewOnly, "Solo Nuovo"));
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

        /// <summary>Parametri filtrabili per la categoria attiva — popolati via Revit API.</summary>
        public ObservableCollection<string> AvailableFilterParams { get; } = new();

        partial void OnSelectedCategoryChanged(CategoryItemVm? value)
        {
            _valuesCache.Clear();
            foreach (var rule in ParamRules)
            {
                rule.AreValuesLoaded = false;
                rule.AvailableValues.Clear();
            }
            RefreshAvailableParams();
            Search();
        }

        private void RefreshAvailableParams()
        {
            AvailableFilterParams.Clear();
            _paramIndex.Clear();
            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null || SelectedCategory == null) return;

            try
            {
                var paramList = GetFilterableParameters(doc, SelectedCategory.Bic);
                foreach (var fp in paramList)
                {
                    AvailableFilterParams.Add(fp.DisplayName);
                    _paramIndex[fp.DisplayName] = fp;
                }
            }
            catch { /* fuori dal Revit thread — lista resta vuota */ }

            // Propaga la lista aggiornata alle regole esistenti
            foreach (var rule in ParamRules)
                SyncAvailableParams(rule);
        }

        /// <summary>
        /// Enumera i parametri filtrabili per la categoria restituendo display name + BuiltInParameter
        /// (quando applicabile). Il BuiltInParameter è la fonte di verità language-independent per la
        /// risoluzione runtime; il display name serve solo alla UI.
        /// </summary>
        private static List<FilterableParam> GetFilterableParameters(
            Autodesk.Revit.DB.Document doc, BuiltInCategory category)
        {
            var result = new List<FilterableParam>();
            try
            {
#if REVIT2025_OR_LATER
                var catId = new ElementId((long)category);
#else
                var catId = new ElementId((int)category);
#endif
                var paramIds = Autodesk.Revit.DB.ParameterFilterUtilities.GetFilterableParametersInCommon(
                    doc, new List<ElementId> { catId });
                var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var pid in paramIds)
                {
#if REVIT2025_OR_LATER
                    long raw = pid.Value;
#else
                    long raw = pid.IntegerValue;
#endif
                    string? name = null;
                    BuiltInParameter? bipOrNull = null;
                    if (raw < 0)
                    {
                        // Built-in parameter: language-independent via enum.
                        var bip = (BuiltInParameter)raw;
                        bipOrNull = bip;
                        try { name = LabelUtils.GetLabelFor(bip); } catch { }
                    }
                    else
                    {
                        // Shared/project parameter: risolto per nome (i nomi utente non sono localizzati).
                        var pe = doc.GetElement(pid) as Autodesk.Revit.DB.ParameterElement;
                        name = pe?.GetDefinition()?.Name ?? pe?.Name;
                    }
                    if (!string.IsNullOrEmpty(name) && seen.Add(name!))
                        result.Add(new FilterableParam { DisplayName = name!, BuiltIn = bipOrNull });
                }
                result.Sort((a, b) => System.StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));
            }
            catch { }
            return result;
        }

        private void SyncAvailableParams(ParamFilterRuleVm rule)
        {
            rule.AvailableParams.Clear();
            foreach (var name in AvailableFilterParams)
                rule.AvailableParams.Add(name);
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
                    .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName) &&
                                (!r.NeedsValue || !string.IsNullOrWhiteSpace(r.Value)))
                    .Select(r =>
                    {
                        var m = r.ToModel();
                        if (_paramIndex.TryGetValue(m.ParameterName, out var fp))
                            m.BuiltIn = fp.BuiltIn;
                        return m;
                    })
                    .ToList();

                var extra = CustomColumns.Count > 0 ? CustomColumns.ToList() : null;
                var results = _service.FindElements(
                    doc,
                    SelectedCategory.Bic,
                    NameQuery,
                    SelectedPhase.PhaseId,
                    ComputationMode,
                    rules,
                    extra);
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

        /// <summary>
        /// Restituisce i parametri filtrabili della categoria corrente che NON sono già
        /// colonne custom. Usato dal code-behind per popolare il menu contestuale
        /// "Aggiungi colonna parametro…".
        /// </summary>
        public IReadOnlyList<FilterableParam> GetAddableColumnParams()
        {
            var already = new HashSet<string>(CustomColumns.Select(c => c.DisplayName),
                StringComparer.OrdinalIgnoreCase);
            return _paramIndex.Values
                .Where(fp => !already.Contains(fp.DisplayName))
                .OrderBy(fp => fp.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Aggiunge una colonna custom e rilancia la ricerca per popolare i valori.</summary>
        public void AddCustomColumn(FilterableParam param)
        {
            if (param == null || string.IsNullOrWhiteSpace(param.DisplayName)) return;
            if (CustomColumns.Any(c => string.Equals(c.DisplayName, param.DisplayName,
                StringComparison.OrdinalIgnoreCase))) return;
            CustomColumns.Add(param);
            Search();
        }

        /// <summary>Rimuove una colonna custom per display name.</summary>
        public void RemoveCustomColumn(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return;
            var existing = CustomColumns.FirstOrDefault(c =>
                string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) CustomColumns.Remove(existing);
        }

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

        public string ComputationModeLabel => ComputationMode switch
        {
            SelectionComputationMode.Demolitions => "Demolizioni",
            SelectionComputationMode.NewOnly     => "Solo Nuovo",
            _                                    => "Nuovo + Esistente"
        };

        [RelayCommand]
        private void AddParamRule()
        {
            var rule = new ParamFilterRuleVm();
            rule.IsFirst = ParamRules.Count == 0;
            SyncAvailableParams(rule);
            rule.PropertyChanged += (s, e) =>
            {
                // Rischedula il search solo per i campi che impattano il risultato.
                if (e.PropertyName == nameof(ParamFilterRuleVm.ParameterName) ||
                    e.PropertyName == nameof(ParamFilterRuleVm.Operator) ||
                    e.PropertyName == nameof(ParamFilterRuleVm.Value) ||
                    e.PropertyName == nameof(ParamFilterRuleVm.LogicOperator))
                {
                    _searchDebounce.Stop();
                    _searchDebounce.Start();
                }
            };
            ParamRules.Add(rule);
            UpdateHasParamRules();
            // Se ci sono già altre regole complete, riesegui subito (la nuova è vuota = ininfluente).
            Search();
        }

        /// <summary>
        /// Carica (lazy, al DropDownOpened della ComboBox valore) i valori distinti del parametro
        /// selezionato per la categoria corrente. Usa una cache per evitare riletture del modello.
        /// </summary>
        [RelayCommand]
        private void LoadParamValues(ParamFilterRuleVm? rule)
        {
            if (rule == null || rule.AreValuesLoaded) return;
            if (SelectedCategory == null || string.IsNullOrWhiteSpace(rule.ParameterName)) return;

            var paramName = rule.ParameterName.Trim();
            var key = (SelectedCategory.Bic, paramName);
            if (!_valuesCache.TryGetValue(key, out var values))
            {
                var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
                if (doc == null) return;
                try
                {
                    // Arricchisci con BuiltInParameter (language-independent) quando disponibile.
                    if (!_paramIndex.TryGetValue(paramName, out var fp))
                        fp = new FilterableParam { DisplayName = paramName, BuiltIn = null };

                    values = _service.EnumerateDistinctValues(
                        doc,
                        SelectedCategory.Bic,
                        fp,
                        SelectedPhase?.PhaseId,
                        ComputationMode);
                    _valuesCache[key] = values;
                }
                catch
                {
                    values = System.Array.Empty<string>();
                }
            }

            rule.AvailableValues.Clear();
            foreach (var v in values) rule.AvailableValues.Add(v);
            rule.AreValuesLoaded = true;
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
                (!r.NeedsValue || !string.IsNullOrWhiteSpace(r.Value)));

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
        private readonly Dictionary<string, string> _customValues;

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
            _customValues = info.CustomValues ?? new Dictionary<string, string>();
        }

        public int ElementId { get; }
        public string UniqueId { get; }
        public string Category { get; }
        public string FamilyName { get; }
        public string TypeName { get; }
        public string LevelName { get; }
        public string PhaseCreatedName { get; }
        public string PhaseDemolishedName { get; }

        /// <summary>Indexer usato dalle DataGridColumn dinamiche: Binding="{Binding [DisplayName]}".</summary>
        public string this[string key] =>
            _customValues != null && _customValues.TryGetValue(key, out var v) ? v : string.Empty;
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
