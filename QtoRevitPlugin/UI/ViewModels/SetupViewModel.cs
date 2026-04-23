using CommunityToolkit.Mvvm.ComponentModel;
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
    /// VM per la SetupView: gestisce listini caricati + ricerca FTS5 live.
    /// Accede al QtoRepository della sessione attiva via <see cref="QtoApplication.Instance.SessionManager"/>.
    /// Il ViewModel è UI-thread-only (SQLite connection, DispatcherTimer).
    /// </summary>
    public partial class SetupViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _searchDebounce;
        private readonly FileFavoritesRepository _favoritesRepository;
        private PriceItemSearchService? _searchService;
        private FavoriteSet _projectFavorites = new FavoriteSet { Name = "Preferiti progetto", Scope = FavoriteScope.Project };
        private FavoriteSet _personalFavorites = new FavoriteSet { Name = "Preferiti personali", Scope = FavoriteScope.Personal };

        public ObservableCollection<PriceListRow> PriceLists { get; } = new();
        public ObservableCollection<SearchScopeOptionRow> AvailableScopes { get; } = new();
        public ObservableCollection<PriceItemRow> SearchResults { get; } = new();
        public ObservableCollection<FavoriteItemRow> FavoriteResults { get; } = new();

        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private string _statusMessage = "Nessun listino caricato";
        [ObservableProperty] private string _searchStatus = "Digita per cercare…";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _lastSearchLevel = "";
        [ObservableProperty] private PriceListRow? _selectedPriceList;
        [ObservableProperty] private PriceItemRow? _selectedSearchResult;
        [ObservableProperty] private FavoriteItemRow? _selectedFavoriteResult;
        [ObservableProperty] private HybridSearchScope _selectedScope = HybridSearchScope.All;
        [ObservableProperty] private SearchDetailRow? _selectedDetailItem;
        [ObservableProperty] private bool _canAddProjectFavorite;
        [ObservableProperty] private bool _canAddPersonalFavorite;
        [ObservableProperty] private bool _canRemoveFavorite;

        public bool HasUserLibrary => QtoApplication.Instance?.UserLibrary?.Library != null;

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

        partial void OnSearchQueryChanged(string value)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        partial void OnSelectedScopeChanged(HybridSearchScope value) => ExecuteSearch();

        partial void OnSelectedSearchResultChanged(PriceItemRow? value)
        {
            if (value != null)
                SelectedFavoriteResult = null;

            SelectedDetailItem = value?.ToDetail();
            UpdateFavoriteActionState();
        }

        partial void OnSelectedFavoriteResultChanged(FavoriteItemRow? value)
        {
            if (value != null)
                SelectedSearchResult = null;

            SelectedDetailItem = value?.ToDetail();
            UpdateFavoriteActionState();
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
                FavoriteResults.Clear();
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

                foreach (var favorite in FilterFavorites(_projectFavorites.Items, "Proj", resolved.UseProjectFavorites))
                    FavoriteResults.Add(favorite);

                foreach (var favorite in FilterFavorites(_personalFavorites.Items, "Pers", resolved.UsePersonalFavorites))
                    FavoriteResults.Add(favorite);

                resultCount += FavoriteResults.Count;
                sw.Stop();

                if (!resolved.UseActivePriceList && !resolved.UseProjectFavorites && !resolved.UsePersonalFavorites)
                {
                    SearchStatus = "Nessuna sorgente disponibile per l'ambito selezionato.";
                }
                else if (resultCount == 0)
                {
                    SearchStatus = $"Nessun risultato · {sw.ElapsedMilliseconds} ms";
                }
                else
                {
                    SearchStatus = $"{SearchResults.Count} risultati listino · {FavoriteResults.Count} preferiti · {sw.ElapsedMilliseconds} ms";
                }
            }
            catch (Exception ex)
            {
                SearchStatus = $"Errore ricerca: {ex.Message}";
            }
        }

        private static QtoRepository? GetActiveRepo()
        {
            return QtoApplication.Instance?.UserLibrary?.Library;
        }

        private static IPriceListParser? FindParserFor(string filePath)
        {
            var parsers = new IPriceListParser[] { new DcfParser(), new ExcelParser(), new CsvParser() };
            return parsers.FirstOrDefault(p => p.CanHandle(filePath));
        }

        public bool HasActivePriceList => PriceLists.Any(x => x.IsActive);

        public void AddSelectedToProjectFavorites()
        {
            if (SelectedSearchResult == null)
            {
                SearchStatus = "Seleziona prima una voce di listino.";
                return;
            }

            var cmePath = QtoApplication.Instance?.SessionManager?.ActiveFilePath;
            if (string.IsNullOrWhiteSpace(cmePath))
            {
                SearchStatus = "Apri o crea un computo per usare i preferiti progetto.";
                return;
            }

            UpsertFavorite(_projectFavorites, SelectedSearchResult.ToFavoriteItem());
            _favoritesRepository.SaveForProject(cmePath, _projectFavorites);
            LoadFavorites();
            ExecuteSearch();
        }

        public void AddSelectedToPersonalFavorites()
        {
            if (SelectedSearchResult == null)
            {
                SearchStatus = "Seleziona prima una voce di listino.";
                return;
            }

            UpsertFavorite(_personalFavorites, SelectedSearchResult.ToFavoriteItem());
            _favoritesRepository.SaveGlobal(_personalFavorites);
            LoadFavorites();
            ExecuteSearch();
        }

        public void RemoveSelectedFavorite()
        {
            if (SelectedFavoriteResult == null)
            {
                SearchStatus = "Seleziona prima un preferito.";
                return;
            }

            if (SelectedFavoriteResult.ScopeBadge == "Proj")
            {
                var cmePath = QtoApplication.Instance?.SessionManager?.ActiveFilePath;
                if (string.IsNullOrWhiteSpace(cmePath))
                {
                    SearchStatus = "Apri o crea un computo per modificare i preferiti progetto.";
                    return;
                }

                RemoveFavorite(_projectFavorites, SelectedFavoriteResult.Code);
                _favoritesRepository.SaveForProject(cmePath, _projectFavorites);
            }
            else
            {
                RemoveFavorite(_personalFavorites, SelectedFavoriteResult.Code);
                _favoritesRepository.SaveGlobal(_personalFavorites);
            }

            LoadFavorites();
            ExecuteSearch();
        }

        private void LoadFavorites()
        {
            _personalFavorites = _favoritesRepository.LoadGlobal();

            var cmePath = QtoApplication.Instance?.SessionManager?.ActiveFilePath;
            _projectFavorites = !string.IsNullOrWhiteSpace(cmePath)
                ? _favoritesRepository.LoadForProject(cmePath) ?? new FavoriteSet { Name = "Preferiti progetto", Scope = FavoriteScope.Project }
                : new FavoriteSet { Name = "Preferiti progetto", Scope = FavoriteScope.Project };

            UpdateFavoriteActionState();
        }

        private IEnumerable<FavoriteItemRow> FilterFavorites(IEnumerable<FavoriteItem> items, string badge, bool enabled)
        {
            if (!enabled)
                yield break;

            var query = SearchQuery?.Trim();
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(query))
                {
                    var haystack = $"{item.Code} {item.ShortDesc} {item.Unit}".ToLowerInvariant();
                    if (!haystack.Contains(query.ToLowerInvariant()))
                        continue;
                }

                yield return new FavoriteItemRow(item, badge);
            }
        }

        private static void UpsertFavorite(FavoriteSet set, FavoriteItem item)
        {
            var existing = set.Items.FirstOrDefault(x => string.Equals(x.Code, item.Code, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                set.Items.Add(item);
                return;
            }

            existing.ShortDesc = item.ShortDesc;
            existing.Unit = item.Unit;
            existing.UnitPrice = item.UnitPrice;
        }

        private static void RemoveFavorite(FavoriteSet set, string code)
        {
            var existing = set.Items.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                set.Items.Remove(existing);
        }

        private void UpdateFavoriteActionState()
        {
            CanAddProjectFavorite = SelectedSearchResult != null;
            CanAddPersonalFavorite = SelectedSearchResult != null;
            CanRemoveFavorite = SelectedFavoriteResult != null;
        }
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

        [ObservableProperty] private bool _isFavoriteInLibrary;

        public string FavoriteMenuLabel => IsFavoriteInLibrary
            ? "★ Rimuovi dai preferiti"
            : "★ Aggiungi ai preferiti";

        partial void OnIsFavoriteInLibraryChanged(bool value) => OnPropertyChanged(nameof(FavoriteMenuLabel));

        public string ShortDescTrimmed =>
            string.IsNullOrEmpty(ShortDesc) ? "" :
            ShortDesc.Length > 140 ? ShortDesc.Substring(0, 140).Replace('\n', ' ') + "…" :
            ShortDesc.Replace('\n', ' ');

        public string UnitPriceFormatted => UnitPrice > 0 ? $"€ {UnitPrice:N2}" : "—";

        public string HierarchyPath
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>(3);
                if (!string.IsNullOrWhiteSpace(SuperChapter)) parts.Add(SuperChapter);
                if (!string.IsNullOrWhiteSpace(Chapter)) parts.Add(Chapter);
                if (!string.IsNullOrWhiteSpace(SubChapter)) parts.Add(SubChapter);
                return string.Join("  ›  ", parts);
            }
        }

        public FavoriteItem ToFavoriteItem() => new FavoriteItem
        {
            Code = Code,
            ShortDesc = !string.IsNullOrWhiteSpace(ShortDesc) ? ShortDesc : Description,
            Unit = Unit,
            UnitPrice = UnitPrice
        };

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

    public class FavoriteItemRow
    {
        public FavoriteItemRow(FavoriteItem item, string scopeBadge)
        {
            Code = item.Code;
            ShortDesc = item.ShortDesc;
            Unit = item.Unit;
            UnitPrice = item.UnitPrice;
            ScopeBadge = scopeBadge;
        }

        public string Code { get; }
        public string ShortDesc { get; }
        public string Unit { get; }
        public double UnitPrice { get; }
        public string ScopeBadge { get; }
        public string UnitPriceFormatted => UnitPrice > 0 ? $"€ {UnitPrice:N2}" : "—";

        public SearchDetailRow ToDetail() => new SearchDetailRow
        {
            Code = Code,
            SourceLabel = ScopeBadge == "Proj" ? "Preferiti progetto" : "Preferiti personali",
            HierarchyPath = "",
            Description = ShortDesc,
            Unit = Unit,
            UnitPriceFormatted = UnitPriceFormatted
        };
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
