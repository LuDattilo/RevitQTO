using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.Services.Computi;
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

        // -------- Plan C-6: Assegnazione EP --------

        [ObservableProperty] private string _activeEpCode = "";
        [ObservableProperty] private string _activeEpDescription = "";
        [ObservableProperty] private QuantityMode _quantityMode = QuantityMode.Count;
        [ObservableProperty] private string _assignPreview = "";
        [ObservableProperty] private string _assignButtonText = "Assegna";

        /// <summary>Elementi selezionati nel DataGrid (aggiornati dal code-behind via SetSelectedElements).</summary>
        public ObservableCollection<ElementRowVm> SelectedElements { get; } = new();

        public ObservableCollection<QuantityModeOption> QuantityModeOptions { get; } =
            new ObservableCollection<QuantityModeOption>
            {
                new QuantityModeOption(QuantityMode.Count,  "Conteggio (cad)"),
                new QuantityModeOption(QuantityMode.Area,   "Area (m²)"),
                new QuantityModeOption(QuantityMode.Volume, "Volume (m³)"),
                new QuantityModeOption(QuantityMode.Length, "Lunghezza (m)")
            };

        public bool CanApply =>
            !string.IsNullOrWhiteSpace(ActiveEpCode) && SelectedElements.Count > 0;

        partial void OnActiveEpCodeChanged(string value)
        {
            OnPropertyChanged(nameof(CanApply));
            ApplyEpCommand.NotifyCanExecuteChanged();
            UpdateAssignPreview();
        }

        partial void OnQuantityModeChanged(QuantityMode value) => UpdateAssignPreview();

        /// <summary>
        /// Plan C-6: chiamato dal code-behind quando cambia la selezione nel DataGrid.
        /// Aggiorna SelectedElements + preview + CanApply.
        /// </summary>
        public void SetSelectedElements(System.Collections.Generic.IEnumerable<ElementRowVm> selected)
        {
            SelectedElements.Clear();
            if (selected != null)
                foreach (var el in selected) SelectedElements.Add(el);
            OnPropertyChanged(nameof(CanApply));
            ApplyEpCommand.NotifyCanExecuteChanged();
            UpdateAssignPreview();
        }

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
                var sm = QtoApplication.Instance.SessionManager;
                sm.SessionChanged += (_, _) => RefreshFromSession();
                // Plan C-6: voce EP attiva propagata cross-scheda dal Listino
                sm.ActiveEpChanged += (_, _) =>
                {
                    ActiveEpCode = sm.ActiveEpCode;
                    ActiveEpDescription = sm.ActiveEpDescription;
                };
                ActiveEpCode = sm.ActiveEpCode;
                ActiveEpDescription = sm.ActiveEpDescription;
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

                // Plan C-6: reset selezione quando i risultati cambiano (il DataGrid perde la
                // selezione automaticamente quando ItemsSource cambia, il code-behind richiamerà
                // SetSelectedElements(empty) via evento).
                SelectedElements.Clear();
                UpdateAssignPreview();
                OnPropertyChanged(nameof(CanApply));
                ApplyEpCommand.NotifyCanExecuteChanged();
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

        // =====================================================================
        // Plan C-6: Assegnazione EP agli elementi filtrati
        // =====================================================================

        /// <summary>
        /// Copia (upsert) un PriceItem dalla UserLibrary al .cme della sessione corrente.
        /// Crea anche il PriceList se non esiste (match per Name + Source). Idempotente:
        /// se la voce è già presente nel .cme (match per Code dentro lo stesso listino),
        /// ritorna quella esistente invece di duplicare.
        /// </summary>
        private static PriceItem CopyPriceItemToCmeRepo(PriceItem source, QtoRevitPlugin.Data.QtoRepository cmeRepo)
        {
            // 1. GetOrCreate PriceList nel .cme (match per ListName del source).
            var listName = string.IsNullOrWhiteSpace(source.ListName) ? "Listino importato" : source.ListName;
            var allLists = cmeRepo.GetPriceLists();
            var targetList = allLists.FirstOrDefault(l =>
                string.Equals(l.Name, listName, StringComparison.OrdinalIgnoreCase));
            if (targetList == null)
            {
                targetList = new PriceList
                {
                    Name = listName,
                    Source = "UserLibrary",
                    IsActive = true,
                    Priority = 0,
                    ImportedAt = DateTime.UtcNow,
                    RowCount = 0
                };
                cmeRepo.InsertPriceList(targetList);
                AssignEpLogger.Log($"  Nuovo PriceList nel .cme · id={targetList.Id} name='{listName}'");
            }

            // 2. Check esistenza nella lista target (idempotenza)
            var existing = cmeRepo.GetPriceItemsByCode(source.Code)
                                  .FirstOrDefault(p => p.PriceListId == targetList.Id);
            if (existing != null)
            {
                AssignEpLogger.Log($"  PriceItem già presente nel .cme · id={existing.Id}");
                return existing;
            }

            // 3. INSERT nuovo PriceItem nel .cme copiando i campi
            var inserted = cmeRepo.InsertPriceItemSingle(source, targetList.Id);
            AssignEpLogger.Log($"  Insert PriceItem nel .cme · id={inserted.Id}");
            return inserted;
        }

        /// <summary>
        /// Ricalcola la preview "N selezionati · tot X m²" leggendo i valori geometrici
        /// degli elementi ATTUALMENTE SELEZIONATI nel DataGrid (non dell'intera tabella filtrata).
        /// Aggiorna anche il testo dinamico del bottone (AssignButtonText).
        /// </summary>
        private void UpdateAssignPreview()
        {
            if (SelectedElements.Count == 0)
            {
                AssignPreview = Elements.Count > 0
                    ? $"⚠ Nessuna riga selezionata · filtrati {Elements.Count} · usa Ctrl/Shift+Click per multi-selezione"
                    : "";
                AssignButtonText = "Assegna";
                return;
            }

            if (string.IsNullOrWhiteSpace(ActiveEpCode))
            {
                AssignPreview = $"✓ {SelectedElements.Count} selezionati · seleziona una voce dal Listino";
                AssignButtonText = $"Assegna {SelectedElements.Count}";
                return;
            }

            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null) { AssignPreview = "Nessun documento"; AssignButtonText = "Assegna"; return; }

            try
            {
                var reader = new RevitElementMeasurementReader();
                double total = 0;
                int counted = 0;
                foreach (var vm in SelectedElements)
                {
#if REVIT2025_OR_LATER
                    var el = doc.GetElement(new ElementId((long)vm.ElementId));
#else
                    var el = doc.GetElement(new ElementId(vm.ElementId));
#endif
                    if (el == null) continue;
                    var v = reader.GetValue(el, QuantityMode);
                    if (v.HasValue) { total += v.Value; counted++; }
                }
                var unit = QuantityMode switch
                {
                    QuantityMode.Area => "m²",
                    QuantityMode.Volume => "m³",
                    QuantityMode.Length => "m",
                    _ => "pz"
                };
                AssignPreview = $"✓ Selezionati: {counted} · totale {total:N2} {unit}";
                AssignButtonText = counted == 1
                    ? "Assegna 1 elemento"
                    : $"Assegna {counted} elementi";
            }
            catch (Exception ex)
            {
                AssignPreview = $"Preview errata: {ex.Message}";
                AssignButtonText = "Assegna";
            }
        }

        [RelayCommand(CanExecute = nameof(CanApply))]
        private void ApplyEp()
        {
            AssignEpLogger.Log("========== ApplyEp start ==========");
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            AssignEpLogger.Log($"Context · repo={(repo != null ? "ok" : "null")} · session={(sess != null ? $"id={sess.Id}" : "null")} · doc={(doc != null ? $"'{doc.Title}'" : "null")}");

            if (repo == null || sess == null || doc == null)
            {
                StatusMessage = "Sessione o documento non disponibili.";
                AssignEpLogger.Log("ABORT: context mancante");
                return;
            }

            try
            {
                // 1. Garantisci ComputoDocument per la sessione
                var docSvc = new ComputoDocumentService(repo);
                var cmeDoc = docSvc.GetOrCreate(sess.Id);
                AssignEpLogger.Log($"ComputoDocument · id={cmeDoc.Id} · tipo={cmeDoc.TipoDocumento}");

                // 2. Risolvi PriceItem per Code.
                // Il Listino (ricerca ibrida + preferiti) popola ActiveEpCode con voci che
                // vivono nella UserLibrary globale (%AppData%\QtoPlugin\UserLibrary.db),
                // NON nel .cme del progetto (SessionManager.Repository). Il MeasurementRow
                // che creeremo ha FK su PriceItems.Id del .cme → serve "copiare" la voce
                // dal UserLibrary al .cme la prima volta che viene usata.
                var codeLookup = (ActiveEpCode ?? "").Trim();
                AssignEpLogger.Log($"Lookup voce EP · code=[{codeLookup}] len={codeLookup.Length}");
                var hex = string.Join(" ", codeLookup.Select(c => ((int)c).ToString("X2")));
                AssignEpLogger.Log($"  bytes: {hex}");

                // 2a. Prima cerca nel .cme (già copiata in passato? idempotenza)
                var items = repo.GetPriceItemsByCode(codeLookup);
                AssignEpLogger.Log($"GetPriceItemsByCode su .cme · matches={items.Count}");

                PriceItem? pi = items.FirstOrDefault();

                // 2b. Se non c'è nel .cme, cerca nella UserLibrary globale e copia
                if (pi == null)
                {
                    var userLib = QtoApplication.Instance?.UserLibrary?.Library;
                    if (userLib == null)
                    {
                        AssignEpLogger.Log("ABORT: UserLibrary non inizializzata");
                        StatusMessage = "UserLibrary non disponibile. Riavvia Revit.";
                        return;
                    }
                    var userItems = userLib.GetPriceItemsByCode(codeLookup);
                    AssignEpLogger.Log($"GetPriceItemsByCode su UserLibrary · matches={userItems.Count}");

                    var sourcePi = userItems.FirstOrDefault();
                    if (sourcePi == null)
                    {
                        var similar = userLib.SearchPriceItemsByCodeLike(codeLookup, 5);
                        AssignEpLogger.Log($"Fallback LIKE su UserLibrary · {similar.Count} simili:");
                        foreach (var s in similar)
                            AssignEpLogger.Log($"  · listId={s.PriceListId} · code=[{s.Code}] · list={s.ListName}");

                        var logHint = AssignEpLogger.LastError != null
                            ? $" LOGGER ERROR: {AssignEpLogger.LastError}"
                            : $" Log: {AssignEpLogger.LogPath}";
                        StatusMessage = similar.Count > 0
                            ? $"Voce '{codeLookup}' non trovata esatta. {similar.Count} simili in UserLibrary: es. '{similar[0].Code}'.{logHint}"
                            : $"Voce '{codeLookup}' non trovata né in .cme né in UserLibrary.{logHint}";
                        AssignEpLogger.Log($"ABORT: {StatusMessage}");
                        return;
                    }

                    // Copia la voce nel .cme (idempotente: se il listino non esiste, lo crea con lo stesso Name)
                    AssignEpLogger.Log($"Copy UserLibrary → .cme · source id={sourcePi.Id} listId={sourcePi.PriceListId} list={sourcePi.ListName}");
                    pi = CopyPriceItemToCmeRepo(sourcePi, repo);
                    AssignEpLogger.Log($"Copied PriceItem → .cme id={pi.Id} listId={pi.PriceListId}");
                }
                AssignEpLogger.Log($"PriceItem risolto · id={pi.Id} · listId={pi.PriceListId} · list={pi.ListName}");

                // 3. Crea un nuovo MeasurementRow (VCItem)
                var msvc = new MeasurementService(repo);
                var row = msvc.CreateRow(cmeDoc.Id, pi.Id);

                // 4. Per ciascun elemento SELEZIONATO nel DataGrid → MeasurementSubRow (RGItem)
                AssignEpLogger.Log($"SelectedElements count={SelectedElements.Count}");
                var reader = new RevitElementMeasurementReader();
                int addedCount = 0;
                double totalQty = 0;
                foreach (var elVm in SelectedElements)
                {
#if REVIT2025_OR_LATER
                    var el = doc.GetElement(new ElementId((long)elVm.ElementId));
#else
                    var el = doc.GetElement(new ElementId(elVm.ElementId));
#endif
                    if (el == null) continue;
                    var v = reader.GetValue(el, QuantityMode) ?? 0;

                    // Mappa il valore geometrico sulla dimensione corrispondente della formula
                    // PartiUguali × Lunghezza × Larghezza × HPeso (valori null = 1).
                    double partiUguali = 1;
                    double? lung = QuantityMode == QuantityMode.Length ? (double?)v : null;
                    double? larg = QuantityMode == QuantityMode.Area ? (double?)v : null;
                    double? hPeso = QuantityMode == QuantityMode.Volume ? (double?)v : null;
                    // Per Count, lascia tutte null → Quantita = PartiUguali = 1

                    msvc.AddOrUpdateSubRow(
                        row.Id,
                        idvv: elVm.ElementId,
                        descrizione: $"[{elVm.ElementId}] {elVm.FamilyName} · {elVm.TypeName}",
                        partiUguali: partiUguali,
                        lunghezza: lung, larghezza: larg, hPeso: hPeso);
                    addedCount++;
                    totalQty += v;
                }

                var unit = QuantityMode switch
                {
                    QuantityMode.Area => "m²",
                    QuantityMode.Volume => "m³",
                    QuantityMode.Length => "m",
                    _ => "pz"
                };
                StatusMessage = $"✓ Assegnati {addedCount} elementi a '{ActiveEpCode}' · tot {totalQty:N2} {unit}";
                AssignEpLogger.Log($"SUCCESS · {addedCount} SubRow · tot={totalQty:N2} {unit}");
            }
            catch (DomainValidationException dex)
            {
                StatusMessage = $"{dex.RuleCode}: {dex.Message}";
                AssignEpLogger.Log($"DomainValidationException · {dex.RuleCode}: {dex.Message}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore assegnazione: {ex.Message}";
                AssignEpLogger.Log($"EXCEPTION · {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
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

    /// <summary>
    /// Plan C-6: opzione dropdown per QuantityMode (enum + label localizzata).
    /// </summary>
    public class QuantityModeOption
    {
        public QuantityModeOption(QuantityMode mode, string label)
        {
            Mode = mode;
            Label = label;
        }
        public QuantityMode Mode { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }
}
