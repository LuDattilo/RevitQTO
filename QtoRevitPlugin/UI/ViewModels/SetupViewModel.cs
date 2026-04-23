using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Parsers;
using QtoRevitPlugin.Search;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// VM per SetupListinoView — Fase 4 (Listino ibrido + doppio preferiti).
    ///
    /// Tre collezioni distinte bindate al TabControl:
    ///   · <see cref="SearchResults"/>     — risultati della ricerca ibrida (listino attivo)
    ///   · <see cref="ProjectFavorites"/>  — preferiti legati al .cme corrente (favorites.project.json)
    ///   · <see cref="PersonalFavorites"/> — preferiti globali utente (favorites.personal.json in AppData)
    ///
    /// Lo scope dei preferiti è persistito via <see cref="FileFavoritesRepository"/>
    /// (Core). Il DB della UserLibrary contiene solo i listini — i preferiti sono file JSON.
    ///
    /// Il tracking "usato nel computo" (badge ✓) viene calcolato confrontando il Code
    /// di ciascun preferito con <c>QtoRepository.GetUsedEpCodes(sessionId)</c> del repository
    /// della sessione .cme attiva. Se non c'è sessione, tutti risultano "non usati".
    /// </summary>
    public partial class SetupViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _searchDebounce;
        private readonly FileFavoritesRepository _favoritesRepository;
        private PriceItemSearchService? _searchService;

        private FavoriteSet _projectFavoritesSet = new FavoriteSet
        {
            Name = "Preferiti progetto",
            Scope = FavoriteScope.Project
        };

        private FavoriteSet _personalFavoritesSet = new FavoriteSet
        {
            Name = "Preferiti personali",
            Scope = FavoriteScope.Personal
        };

        // ---------------------------------------------------------------------
        // Collections bindate al TabControl / DataGrid
        // ---------------------------------------------------------------------

        public ObservableCollection<PriceListRow> PriceLists { get; } = new();
        public ObservableCollection<SearchScopeOptionRow> AvailableScopes { get; } = new();
        public ObservableCollection<PriceItemRow> SearchResults { get; } = new();
        public ObservableCollection<FavoriteItemRow> ProjectFavorites { get; } = new();
        public ObservableCollection<FavoriteItemRow> PersonalFavorites { get; } = new();

        // ---------------------------------------------------------------------
        // Properties osservabili
        // ---------------------------------------------------------------------

        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private string _statusMessage = "Nessun listino caricato";
        [ObservableProperty] private string _searchStatus = "Digita per cercare…";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _lastSearchLevel = "";
        [ObservableProperty] private PriceListRow? _selectedPriceList;
        [ObservableProperty] private PriceItemRow? _selectedSearchResult;
        [ObservableProperty] private FavoriteItemRow? _selectedProjectFavorite;
        [ObservableProperty] private FavoriteItemRow? _selectedPersonalFavorite;
        [ObservableProperty] private HybridSearchScope _selectedScope = HybridSearchScope.All;
        [ObservableProperty] private SearchDetailRow? _selectedDetailItem;

        /// <summary>Header dinamico Tab "Preferiti progetto": "Preferiti progetto (N)".</summary>
        [ObservableProperty] private string _projectFavoritesHeader = "Preferiti progetto";

        /// <summary>Header dinamico Tab "Preferiti personali": "Preferiti personali (N)".</summary>
        [ObservableProperty] private string _personalFavoritesHeader = "Preferiti personali";

        [ObservableProperty] private int _projectUnusedCount;
        [ObservableProperty] private int _personalUnusedCount;
        [ObservableProperty] private bool _hasProjectUnused;
        [ObservableProperty] private bool _hasPersonalUnused;

        [ObservableProperty] private bool _projectFavoritesIsEmpty = true;
        [ObservableProperty] private bool _personalFavoritesIsEmpty = true;

        /// <summary>
        /// True quando la UserLibrary è disponibile (sempre true se il plugin è avviato correttamente).
        /// </summary>
        public bool HasUserLibrary => QtoApplication.Instance?.UserLibrary?.Library != null;

        public bool HasActivePriceList => PriceLists.Any(x => x.IsActive);

        // ---------------------------------------------------------------------
        // Ctor
        // ---------------------------------------------------------------------

        public SetupViewModel()
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += OnSearchDebounceTick;
            _favoritesRepository = new FileFavoritesRepository(FileFavoritesRepository.GetDefaultGlobalDir());

            AvailableScopes.Add(new SearchScopeOptionRow(HybridSearchScope.All, "Tutti"));
            AvailableScopes.Add(new SearchScopeOptionRow(HybridSearchScope.ActivePriceList, "Listino attivo"));
            AvailableScopes.Add(new SearchScopeOptionRow(HybridSearchScope.ProjectFavorites, "Preferiti progetto"));
            AvailableScopes.Add(new SearchScopeOptionRow(HybridSearchScope.PersonalFavorites, "Preferiti personali"));

            RefreshPriceLists();
            LoadFavorites();

            if (QtoApplication.Instance?.SessionManager != null)
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) =>
                {
                    LoadFavorites();
                    ExecuteSearch();
                };
        }

        // ---------------------------------------------------------------------
        // Listini (da UserLibrary globale — persistenti tra sessioni)
        // ---------------------------------------------------------------------

        public void RefreshPriceLists()
        {
            PriceLists.Clear();
            var repo = GetActiveRepo();
            if (repo == null)
            {
                StatusMessage = "UserLibrary non inizializzata — riavvia Revit";
                return;
            }

            try
            {
                var lists = repo.GetPriceLists();
                foreach (var l in lists)
                {
                    PriceLists.Add(new PriceListRow(this, l));
                }

                StatusMessage = lists.Count == 0
                    ? "Libreria vuota — clicca «+ Importa listino…» per aggiungerne uno (persistente)"
                    : $"{lists.Count} listino(i) in libreria · {lists.Sum(l => l.RowCount)} voci totali · persistenti tra computi";

                _searchService = new PriceItemSearchService(repo);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore lettura libreria: {ex.Message}";
            }
        }

        /// <summary>
        /// Invocato da PriceListRow quando la checkbox IsActive viene togglata.
        /// Persiste lo stato nel repository e invalida la cache della ricerca.
        /// </summary>
        public void OnPriceListActiveToggled(PriceListRow row, bool newValue)
        {
            try
            {
                var repo = GetActiveRepo();
                if (repo == null) return;
                repo.UpdatePriceListFlags(row.Id, newValue, row.Priority);

                _searchService?.InvalidateCache();
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                    ExecuteSearch();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.OnPriceListActiveToggled", ex);
            }
        }

        public PriceListImportResult? ImportFromFile(string filePath)
        {
            var repo = GetActiveRepo();
            if (repo == null) throw new InvalidOperationException("UserLibrary non inizializzata.");

            var parser = FindParserFor(filePath);
            if (parser == null)
                throw new InvalidOperationException(
                    $"Estensione '{Path.GetExtension(filePath)}' non supportata. Usa DCF/XPWE/XML/CSV/TSV/XLSX.");

            IsBusy = true;
            try
            {
                var result = parser.Parse(filePath);
                if (result.Items.Count == 0)
                {
                    StatusMessage = $"Import fallito: 0 voci estratte. Warning: {result.Warnings.Count}";
                    return result;
                }

                result.Metadata.Name = string.IsNullOrWhiteSpace(result.Metadata.Name)
                    ? Path.GetFileNameWithoutExtension(filePath)
                    : result.Metadata.Name;

                var listId = repo.InsertPriceList(result.Metadata);
                var imported = repo.InsertPriceItemsBatch(listId, result.Items);

                StatusMessage = $"Importato: {imported} voci da {Path.GetFileName(filePath)}" +
                                (result.Warnings.Count > 0 ? $" ({result.Warnings.Count} warning)" : "");
                RefreshPriceLists();
                return result;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void DeleteSelected()
        {
            if (SelectedPriceList == null) return;
            var repo = GetActiveRepo();
            if (repo == null) return;

            repo.DeletePriceList(SelectedPriceList.Id);
            StatusMessage = $"Eliminato listino «{SelectedPriceList.Name}»";
            RefreshPriceLists();
            SearchResults.Clear();
            SearchStatus = "Digita per cercare…";
        }

        // ---------------------------------------------------------------------
        // Ricerca (debounced)
        // ---------------------------------------------------------------------

        partial void OnSearchQueryChanged(string value)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        partial void OnSelectedScopeChanged(HybridSearchScope value) => ExecuteSearch();

        partial void OnSelectedSearchResultChanged(PriceItemRow? value)
        {
            if (value != null)
            {
                SelectedProjectFavorite = null;
                SelectedPersonalFavorite = null;
            }
            SelectedDetailItem = value?.ToDetail();
        }

        partial void OnSelectedProjectFavoriteChanged(FavoriteItemRow? value)
        {
            if (value != null)
            {
                SelectedSearchResult = null;
                SelectedPersonalFavorite = null;
                SelectedDetailItem = value.ToDetail();
            }
        }

        partial void OnSelectedPersonalFavoriteChanged(FavoriteItemRow? value)
        {
            if (value != null)
            {
                SelectedSearchResult = null;
                SelectedProjectFavorite = null;
                SelectedDetailItem = value.ToDetail();
            }
        }

        private void OnSearchDebounceTick(object? sender, EventArgs e)
        {
            _searchDebounce.Stop();
            ExecuteSearch();
        }

        private void ExecuteSearch()
        {
            var resolver = new HybridSearchScopeResolver();
            var resolved = resolver.Resolve(SelectedScope, HasActivePriceList);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                SearchResults.Clear();
                LastSearchLevel = "";

                var resultCount = 0;
                if (resolved.UseActivePriceList && _searchService != null && !string.IsNullOrWhiteSpace(SearchQuery))
                {
                    var result = _searchService.Search(SearchQuery, maxResults: 50);
                    foreach (var item in result.Items)
                        SearchResults.Add(new PriceItemRow(item));

                    LastSearchLevel = result.Level.ToString();
                    resultCount += result.Count;
                }

                SyncFavoriteFlagsOnSearchResults();

                sw.Stop();

                if (!resolved.UseActivePriceList && !resolved.UseProjectFavorites && !resolved.UsePersonalFavorites)
                {
                    SearchStatus = "Nessuna sorgente disponibile per l'ambito selezionato.";
                }
                else if (resultCount == 0 && string.IsNullOrWhiteSpace(SearchQuery))
                {
                    SearchStatus = "Digita per cercare…";
                }
                else if (resultCount == 0)
                {
                    SearchStatus = $"Nessun risultato · {sw.ElapsedMilliseconds} ms";
                }
                else
                {
                    SearchStatus = $"{SearchResults.Count} risultati · livello {LastSearchLevel} · {sw.ElapsedMilliseconds} ms";
                }
            }
            catch (Exception ex)
            {
                SearchStatus = $"Errore ricerca: {ex.Message}";
            }
        }

        // ---------------------------------------------------------------------
        // Preferiti (FileFavoritesRepository · scope-aware)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Carica i preferiti da file JSON (Project + Personal) e popola le ObservableCollections.
        /// Aggiorna anche il tracking "usato nel computo" via GetUsedEpCodes.
        /// </summary>
        public void LoadFavorites()
        {
            try
            {
                _personalFavoritesSet = _favoritesRepository.LoadGlobal() ?? new FavoriteSet
                {
                    Name = "Preferiti personali",
                    Scope = FavoriteScope.Personal
                };

                var cmePath = QtoApplication.Instance?.SessionManager?.ActiveFilePath;
                _projectFavoritesSet = !string.IsNullOrWhiteSpace(cmePath)
                    ? (_favoritesRepository.LoadForProject(cmePath!) ?? new FavoriteSet
                    {
                        Name = "Preferiti progetto",
                        Scope = FavoriteScope.Project
                    })
                    : new FavoriteSet { Name = "Preferiti progetto", Scope = FavoriteScope.Project };

                ProjectFavorites.Clear();
                foreach (var item in _projectFavoritesSet.Items)
                    ProjectFavorites.Add(new FavoriteItemRow(item, FavoriteScope.Project));

                PersonalFavorites.Clear();
                foreach (var item in _personalFavoritesSet.Items)
                    PersonalFavorites.Add(new FavoriteItemRow(item, FavoriteScope.Personal));

                RefreshFavoritesUsage();
                RefreshFavoriteHeaders();
                SyncFavoriteFlagsOnSearchResults();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.LoadFavorites", ex);
            }
        }

        /// <summary>
        /// Marca ogni preferito con IsUsedInComputo=true se il suo Code è assegnato
        /// attivamente (QtoAssignments Active) nella sessione computo corrente.
        /// Safe fallback: senza sessione attiva tutti restano "non usati".
        /// </summary>
        public void RefreshFavoritesUsage()
        {
            try
            {
                var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
                var sessionRepo = QtoApplication.Instance?.SessionManager?.Repository;

                HashSet<string> used = (session != null && sessionRepo != null)
                    ? sessionRepo.GetUsedEpCodes(session.Id)
                    : new HashSet<string>();

                foreach (var f in ProjectFavorites)
                    f.IsUsedInComputo = used.Contains(f.Code);
                foreach (var f in PersonalFavorites)
                    f.IsUsedInComputo = used.Contains(f.Code);

                ProjectUnusedCount = ProjectFavorites.Count(f => !f.IsUsedInComputo);
                PersonalUnusedCount = PersonalFavorites.Count(f => !f.IsUsedInComputo);
                HasProjectUnused = ProjectUnusedCount > 0;
                HasPersonalUnused = PersonalUnusedCount > 0;
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.RefreshFavoritesUsage", ex);
            }
        }

        private void RefreshFavoriteHeaders()
        {
            ProjectFavoritesIsEmpty = ProjectFavorites.Count == 0;
            PersonalFavoritesIsEmpty = PersonalFavorites.Count == 0;
            ProjectFavoritesHeader = ProjectFavorites.Count == 0
                ? "Preferiti progetto"
                : $"Preferiti progetto ({ProjectFavorites.Count})";
            PersonalFavoritesHeader = PersonalFavorites.Count == 0
                ? "Preferiti personali"
                : $"Preferiti personali ({PersonalFavorites.Count})";
        }

        /// <summary>
        /// Propaga il flag 3-stato FavoriteScopeInLibrary su tutti i risultati ricerca:
        /// null = non preferita, Project = già in ProjectFavorites, Personal = in PersonalFavorites.
        /// Se una stessa Code è in entrambi scope, Project prevale (più specifico del .cme).
        /// </summary>
        private void SyncFavoriteFlagsOnSearchResults()
        {
            if (SearchResults == null) return;
            var projectCodes = new HashSet<string>(
                ProjectFavorites.Select(f => f.Code),
                StringComparer.OrdinalIgnoreCase);
            var personalCodes = new HashSet<string>(
                PersonalFavorites.Select(f => f.Code),
                StringComparer.OrdinalIgnoreCase);

            foreach (var r in SearchResults)
            {
                if (projectCodes.Contains(r.Code))
                    r.FavoriteScopeInLibrary = FavoriteScope.Project;
                else if (personalCodes.Contains(r.Code))
                    r.FavoriteScopeInLibrary = FavoriteScope.Personal;
                else
                    r.FavoriteScopeInLibrary = null;
            }
        }

        // ---------------------------------------------------------------------
        // Commands preferiti
        // ---------------------------------------------------------------------

        /// <summary>
        /// Toggle preferito: se la voce NON è in nessun scope, la aggiunge al Project
        /// (default: più locale, si sposta con il .cme). Se è già in qualche scope, la rimuove.
        /// L'utente può forzare lo scope via context menu (AddFavoriteToProject/Personal).
        /// </summary>
        [RelayCommand]
        private void ToggleFavoriteForRow(PriceItemRow? row)
        {
            if (row == null) return;
            SelectedSearchResult = row;

            if (row.FavoriteScopeInLibrary == FavoriteScope.Project)
            {
                RemoveFromScope(row.Code, FavoriteScope.Project);
            }
            else if (row.FavoriteScopeInLibrary == FavoriteScope.Personal)
            {
                RemoveFromScope(row.Code, FavoriteScope.Personal);
            }
            else
            {
                AddRowToScope(row, FavoriteScope.Project);
            }
        }

        /// <summary>Aggiunge esplicitamente una riga al Project scope (context menu / drop).</summary>
        [RelayCommand]
        private void AddFavoriteToProject(PriceItemRow? row)
        {
            if (row == null) return;
            AddRowToScope(row, FavoriteScope.Project);
        }

        /// <summary>Aggiunge esplicitamente una riga al Personal scope (context menu / drop).</summary>
        [RelayCommand]
        private void AddFavoriteToPersonal(PriceItemRow? row)
        {
            if (row == null) return;
            AddRowToScope(row, FavoriteScope.Personal);
        }

        /// <summary>
        /// Metodo pubblico wrapper per il code-behind: usato dal Drop handler sul tab
        /// "Preferiti progetto" (il drag&drop aggiunge, non toggla).
        /// </summary>
        public void AddFavoriteFromDropToProject(PriceItemRow row)
        {
            if (row == null) return;
            AddRowToScope(row, FavoriteScope.Project);
        }

        /// <summary>Idem per il tab "Preferiti personali".</summary>
        public void AddFavoriteFromDropToPersonal(PriceItemRow row)
        {
            if (row == null) return;
            AddRowToScope(row, FavoriteScope.Personal);
        }

        /// <summary>Sposta un preferito da Personal a Project (context menu "Sposta in Progetto").</summary>
        [RelayCommand]
        private void MoveFavoriteToProject(FavoriteItemRow? row)
        {
            if (row == null || row.Scope == FavoriteScope.Project) return;
            var item = CloneItem(row);
            _personalFavoritesSet.Items.RemoveAll(x =>
                string.Equals(x.Code, row.Code, StringComparison.OrdinalIgnoreCase));
            UpsertInScope(item, FavoriteScope.Project);
            PersistAndRefresh();
        }

        [RelayCommand]
        private void MoveFavoriteToPersonal(FavoriteItemRow? row)
        {
            if (row == null || row.Scope == FavoriteScope.Personal) return;
            var item = CloneItem(row);
            _projectFavoritesSet.Items.RemoveAll(x =>
                string.Equals(x.Code, row.Code, StringComparison.OrdinalIgnoreCase));
            UpsertInScope(item, FavoriteScope.Personal);
            PersistAndRefresh();
        }

        /// <summary>Rimuove un singolo preferito (context menu preferiti).</summary>
        [RelayCommand]
        private void RemoveFavorite(FavoriteItemRow? row)
        {
            if (row == null) return;
            RemoveFromScope(row.Code, row.Scope);
        }

        /// <summary>Rimuove TUTTI i preferiti progetto non usati nel computo (con conferma).</summary>
        [RelayCommand]
        private void RemoveUnusedProjectFavorites()
        {
            RemoveUnusedForScope(FavoriteScope.Project);
        }

        /// <summary>Rimuove TUTTI i preferiti personali non usati nel computo (con conferma).</summary>
        [RelayCommand]
        private void RemoveUnusedPersonalFavorites()
        {
            RemoveUnusedForScope(FavoriteScope.Personal);
        }

        /// <summary>Copia il codice EP di una riga (risultati o preferiti) negli appunti.</summary>
        [RelayCommand]
        private void CopyCodeToClipboard(object? parameter)
        {
            string? code = null;
            if (parameter is PriceItemRow pr) code = pr.Code;
            else if (parameter is FavoriteItemRow fr) code = fr.Code;
            else if (parameter is string s) code = s;

            if (string.IsNullOrEmpty(code)) return;
            try
            {
                System.Windows.Clipboard.SetText(code);
                SearchStatus = $"Copiato: {code}";
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.CopyCodeToClipboard", ex);
            }
        }

        [RelayCommand]
        private void UseFavoriteInSearch(FavoriteItemRow? row)
        {
            if (row == null) return;
            SelectedScope = HybridSearchScope.ActivePriceList;
            SearchQuery = row.Code;
        }

        /// <summary>Comando UI: forza il re-check dell'uso dei preferiti nel computo.</summary>
        [RelayCommand]
        private void RefreshFavoritesUsageFromUi() => RefreshFavoritesUsage();

        // ---------------------------------------------------------------------
        // ContextMenu Listini
        // ---------------------------------------------------------------------

        [RelayCommand]
        private void TogglePriceListActive(PriceListRow? row)
        {
            if (row == null) return;
            row.IsActive = !row.IsActive;
        }

        [RelayCommand]
        private void BrowsePriceList()
        {
            BrowseRequested?.Invoke(this, System.EventArgs.Empty);
        }

        public event System.EventHandler? BrowseRequested;

        [RelayCommand]
        private void DeleteSelectedPriceList()
        {
            DeleteRequested?.Invoke(this, System.EventArgs.Empty);
        }

        public event System.EventHandler? DeleteRequested;

        // ---------------------------------------------------------------------
        // Helpers interni — scope operations
        // ---------------------------------------------------------------------

        private void AddRowToScope(PriceItemRow row, FavoriteScope scope)
        {
            var item = new FavoriteItem
            {
                Code = row.Code,
                ShortDesc = !string.IsNullOrWhiteSpace(row.ShortDesc) ? row.ShortDesc : row.Description,
                Description = row.Description,
                Unit = row.Unit,
                UnitPrice = row.UnitPrice,
                ListName = row.ListName,
                ListId = (int?)row.ListId,
                AddedAt = DateTime.UtcNow
            };
            UpsertInScope(item, scope);
            PersistAndRefresh();
        }

        private void UpsertInScope(FavoriteItem item, FavoriteScope scope)
        {
            var set = scope == FavoriteScope.Project ? _projectFavoritesSet : _personalFavoritesSet;
            var existing = set.Items.FirstOrDefault(x =>
                string.Equals(x.Code, item.Code, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                set.Items.Add(item);
            }
            else
            {
                existing.ShortDesc = item.ShortDesc;
                existing.Description = item.Description;
                existing.Unit = item.Unit;
                existing.UnitPrice = item.UnitPrice;
                existing.ListName = item.ListName;
                existing.ListId = item.ListId;
            }
        }

        private void RemoveFromScope(string code, FavoriteScope scope)
        {
            var set = scope == FavoriteScope.Project ? _projectFavoritesSet : _personalFavoritesSet;
            var existing = set.Items.FirstOrDefault(x =>
                string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;
            set.Items.Remove(existing);
            PersistAndRefresh();
        }

        private void RemoveUnusedForScope(FavoriteScope scope)
        {
            var rows = scope == FavoriteScope.Project ? ProjectFavorites : PersonalFavorites;
            var unused = rows.Where(f => !f.IsUsedInComputo).Select(f => f.Code).ToList();
            if (unused.Count == 0) return;

            var label = scope == FavoriteScope.Project ? "progetto" : "personali";
            var td = new Autodesk.Revit.UI.TaskDialog($"Rimuovi preferiti {label} inutilizzati")
            {
                MainInstruction = $"Rimuovere {unused.Count} preferit{(unused.Count == 1 ? "o" : "i")} {label} non utilizzat{(unused.Count == 1 ? "o" : "i")} nel computo?",
                MainContent =
                    $"Verranno rimosse SOLO le voci dallo scope «{label}» che non risultano assegnate " +
                    "a elementi Revit nel computo corrente.\n\n" +
                    "Le voci corrispondenti nel listino NON vengono cancellate: potrai sempre ri-aggiungerle ai preferiti dalla ricerca.",
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.No,
                DefaultButton = Autodesk.Revit.UI.TaskDialogResult.No
            };
            if (td.Show() != Autodesk.Revit.UI.TaskDialogResult.Yes) return;

            var set = scope == FavoriteScope.Project ? _projectFavoritesSet : _personalFavoritesSet;
            set.Items.RemoveAll(x => unused.Contains(x.Code, StringComparer.OrdinalIgnoreCase));
            PersistAndRefresh();

            Autodesk.Revit.UI.TaskDialog.Show("Preferiti",
                $"Rimoss{(unused.Count == 1 ? "o" : "i")} {unused.Count} preferit{(unused.Count == 1 ? "o" : "i")} {label} inutilizzat{(unused.Count == 1 ? "o" : "i")}. Il listino è intatto.");
        }

        private void PersistAndRefresh()
        {
            try
            {
                _favoritesRepository.SaveGlobal(_personalFavoritesSet);

                var cmePath = QtoApplication.Instance?.SessionManager?.ActiveFilePath;
                if (!string.IsNullOrWhiteSpace(cmePath))
                    _favoritesRepository.SaveForProject(cmePath!, _projectFavoritesSet);

                ProjectFavorites.Clear();
                foreach (var item in _projectFavoritesSet.Items)
                    ProjectFavorites.Add(new FavoriteItemRow(item, FavoriteScope.Project));

                PersonalFavorites.Clear();
                foreach (var item in _personalFavoritesSet.Items)
                    PersonalFavorites.Add(new FavoriteItemRow(item, FavoriteScope.Personal));

                RefreshFavoritesUsage();
                RefreshFavoriteHeaders();
                SyncFavoriteFlagsOnSearchResults();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.PersistAndRefresh", ex);
            }
        }

        private static FavoriteItem CloneItem(FavoriteItemRow row) => new FavoriteItem
        {
            Code = row.Code,
            ShortDesc = row.ShortDesc,
            Description = row.Description,
            Unit = row.Unit,
            UnitPrice = row.UnitPrice,
            ListName = row.ListName,
            ListId = row.ListId,
            AddedAt = row.AddedAt
        };

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static QtoRepository? GetActiveRepo()
        {
            return QtoApplication.Instance?.UserLibrary?.Library;
        }

        private static IPriceListParser? FindParserFor(string filePath)
        {
            var parsers = new IPriceListParser[] { new DcfParser(), new ExcelParser(), new CsvParser() };
            return parsers.FirstOrDefault(p => p.CanHandle(filePath));
        }
    }

    // -------------------------------------------------------------------------
    // Row DTOs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Riga del DataGrid "Listini attivi" — osservabile per propagare il toggle
    /// IsActive al repository (persistenza + invalidazione cache ricerca).
    /// </summary>
    public partial class PriceListRow : ObservableObject
    {
        private readonly SetupViewModel? _owner;

        public PriceListRow(SetupViewModel owner, PriceList list) : this(list)
        {
            _owner = owner;
        }

        public PriceListRow(PriceList list)
        {
            Id = list.Id;
            Name = list.Name;
            Source = list.Source;
            Region = list.Region;
            Version = list.Version;
            RowCount = list.RowCount;
            Priority = list.Priority;
            ImportedAt = list.ImportedAt;
            _isActive = list.IsActive;
        }

        public int Id { get; }
        public string Name { get; }
        public string Source { get; }
        public string Region { get; }
        public string Version { get; }
        public int RowCount { get; }
        public int Priority { get; }
        public DateTime ImportedAt { get; }

        [ObservableProperty] private bool _isActive;

        public string ImportedAtShort => ImportedAt == default ? "" : ImportedAt.ToLocalTime().ToString("dd/MM HH:mm");

        partial void OnIsActiveChanged(bool value)
        {
            _owner?.OnPriceListActiveToggled(this, value);
        }
    }

    /// <summary>
    /// Riga del DataGrid risultati ricerca. Il flag <see cref="FavoriteScopeInLibrary"/>
    /// è 3-stato: null (non preferita), Project, Personal — guida icone/tooltip/menu.
    /// </summary>
    public partial class PriceItemRow : ObservableObject
    {
        public PriceItemRow(PriceItem item)
        {
            Id = item.Id;
            ListId = item.PriceListId;
            Code = item.Code;
            SuperChapter = item.SuperChapter;
            Chapter = item.Chapter;
            SubChapter = item.SubChapter;
            ShortDesc = !string.IsNullOrWhiteSpace(item.ShortDesc) ? item.ShortDesc : item.Description;
            Description = item.Description;
            Unit = item.Unit;
            UnitPrice = item.UnitPrice;
            ListName = item.ListName;
        }

        public int Id { get; }
        public int ListId { get; }
        public string Code { get; }
        public string SuperChapter { get; }
        public string Chapter { get; }
        public string SubChapter { get; }
        public string ShortDesc { get; }
        public string Description { get; }
        public string Unit { get; }
        public double UnitPrice { get; }
        public string ListName { get; }

        /// <summary>
        /// Scope del preferito se la voce è nei preferiti; null altrimenti.
        /// Aggiornato da SetupViewModel.SyncFavoriteFlagsOnSearchResults.
        /// </summary>
        [ObservableProperty] private FavoriteScope? _favoriteScopeInLibrary;

        partial void OnFavoriteScopeInLibraryChanged(FavoriteScope? value)
        {
            OnPropertyChanged(nameof(IsFavoriteInLibrary));
            OnPropertyChanged(nameof(FavoriteMenuLabel));
            OnPropertyChanged(nameof(FavoriteBadge));
        }

        /// <summary>True se la voce è preferita in QUALSIASI scope.</summary>
        public bool IsFavoriteInLibrary => FavoriteScopeInLibrary.HasValue;

        /// <summary>Label dinamica context menu — cambia tra "Aggiungi" e "Rimuovi".</summary>
        public string FavoriteMenuLabel => IsFavoriteInLibrary
            ? "★ Rimuovi dai preferiti"
            : "★ Aggiungi ai preferiti";

        /// <summary>Icona mostrata nella cell button: ★ se fav, + se non fav.</summary>
        public string FavoriteBadge => IsFavoriteInLibrary ? "★" : "+";

        public string ShortDescTrimmed =>
            string.IsNullOrEmpty(ShortDesc) ? "" :
            ShortDesc.Length > 140 ? ShortDesc.Substring(0, 140).Replace('\n', ' ') + "…" :
            ShortDesc.Replace('\n', ' ');

        public string UnitPriceFormatted => UnitPrice > 0 ? $"€ {UnitPrice:N2}" : "—";

        public string HierarchyPath
        {
            get
            {
                var parts = new List<string>(3);
                if (!string.IsNullOrWhiteSpace(SuperChapter)) parts.Add(SuperChapter);
                if (!string.IsNullOrWhiteSpace(Chapter)) parts.Add(Chapter);
                if (!string.IsNullOrWhiteSpace(SubChapter)) parts.Add(SubChapter);
                return string.Join("  ›  ", parts);
            }
        }

        public SearchDetailRow ToDetail() => new SearchDetailRow
        {
            Code = Code,
            SourceLabel = ListName,
            HierarchyPath = HierarchyPath,
            Description = Description,
            Unit = Unit,
            UnitPriceFormatted = UnitPriceFormatted
        };
    }

    /// <summary>
    /// Riga UI di un preferito (Project o Personal). Espone IsUsedInComputo
    /// per il badge "✓ usato" e tutti i campi mostrati nel DataGrid preferiti.
    /// </summary>
    public partial class FavoriteItemRow : ObservableObject
    {
        public FavoriteItem Model { get; }
        public FavoriteScope Scope { get; }

        public FavoriteItemRow(FavoriteItem item, FavoriteScope scope)
        {
            Model = item;
            Scope = scope;
        }

        public string Code => Model.Code;
        public string ShortDesc => Model.ShortDesc;
        public string Description => !string.IsNullOrEmpty(Model.Description) ? Model.Description : Model.ShortDesc;
        public string Unit => Model.Unit;
        public double UnitPrice => Model.UnitPrice;
        public string ListName => Model.ListName;
        public int? ListId => Model.ListId;
        public DateTime AddedAt => Model.AddedAt;
        public string AddedAtShort => AddedAt == default ? "" : AddedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public string UnitPriceFormatted => UnitPrice > 0 ? $"€ {UnitPrice:N2}" : "—";
        public string ScopeBadge => Scope == FavoriteScope.Project ? "Proj" : "Pers";

        [ObservableProperty] private bool _isUsedInComputo;

        public string UsedBadge => IsUsedInComputo ? "✓ usato" : "—";

        partial void OnIsUsedInComputoChanged(bool value) => OnPropertyChanged(nameof(UsedBadge));

        public SearchDetailRow ToDetail() => new SearchDetailRow
        {
            Code = Code,
            SourceLabel = Scope == FavoriteScope.Project ? "Preferiti progetto" : "Preferiti personali",
            HierarchyPath = ListName,
            Description = Description,
            Unit = Unit,
            UnitPriceFormatted = UnitPriceFormatted
        };
    }

    public class SearchScopeOptionRow
    {
        public SearchScopeOptionRow(HybridSearchScope scope, string label)
        {
            Scope = scope;
            Label = label;
        }

        public HybridSearchScope Scope { get; }
        public string Label { get; }
    }

    public class SearchDetailRow
    {
        public string Code { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public string HierarchyPath { get; set; } = "";
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public string UnitPriceFormatted { get; set; } = "";
    }
}
