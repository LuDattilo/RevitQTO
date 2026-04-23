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
    /// VM per la SetupView: gestisce listini caricati + ricerca FTS5 live.
    /// Accede al QtoRepository della sessione attiva via <see cref="QtoApplication.Instance.SessionManager"/>.
    /// Il ViewModel è UI-thread-only (SQLite connection, DispatcherTimer).
    /// </summary>
    public partial class SetupViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _searchDebounce;
        private PriceItemSearchService? _searchService;

        // ---------------------------------------------------------------------
        // Collections bindate al DataGrid
        // ---------------------------------------------------------------------

        public ObservableCollection<PriceListRow> PriceLists { get; } = new();
        public ObservableCollection<PriceItemRow> SearchResults { get; } = new();
        public ObservableCollection<FavoriteRowVm> Favorites { get; } = new ObservableCollection<FavoriteRowVm>();

        /// <summary>Header dinamico dell'Expander preferiti: "★ I Miei Preferiti (N)" con conteggio.</summary>
        [ObservableProperty] private string _favoritesHeader = "★ I Miei Preferiti";

        /// <summary>True quando la lista preferiti è vuota — binding per mostrare placeholder.</summary>
        [ObservableProperty] private bool _favoritesIsEmpty = true;

        /// <summary>True se esiste almeno un preferito NON usato nel computo.
        /// Abilita/disabilita il bottone "🗑 Rimuovi inutilizzati".</summary>
        [ObservableProperty] private bool _hasUnusedFavorites;

        /// <summary>Conteggio preferiti non usati — usato nel testo del bottone.</summary>
        [ObservableProperty] private int _unusedFavoritesCount;

        // Nota: ProjectInfo non è più esposto qui come property — Sprint 10 rev. B ha
        // separato SetupView in 4 sub-tab UserControl indipendenti. ProjectInfoView
        // istanzia la propria ProjectInfoViewModel direttamente nel proprio XAML.

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
        [ObservableProperty] private FavoriteRowVm? _selectedFavorite;

        /// <summary>
        /// True se la voce selezionata nei risultati ricerca è già nei preferiti.
        /// Aggiornata da OnSelectedSearchResultChanged + dopo ToggleFavorite.
        /// </summary>
        [ObservableProperty] private bool _isSelectedResultFavorite;

        /// <summary>
        /// True quando la UserLibrary è disponibile (sempre true se il plugin è avviato correttamente).
        /// Rinominato in Sprint 10 da <c>HasSessionActive</c> a <c>HasUserLibrary</c> (LOW-S1):
        /// il nome precedente era fuorviante — non indica se c'è un computo aperto, solo
        /// se la libreria listini è caricata.
        /// </summary>
        public bool HasUserLibrary => QtoApplication.Instance?.UserLibrary?.Library != null;

        // ---------------------------------------------------------------------
        // Ctor
        // ---------------------------------------------------------------------

        public SetupViewModel()
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += OnSearchDebounceTick;

            RefreshPriceLists();
            LoadFavorites();
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

                // Reset search service cache
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

                // Invalida la cache fuzzy L3 e rilancia la ricerca corrente se attiva
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
            if (string.IsNullOrWhiteSpace(value))
            {
                SearchResults.Clear();
                SearchStatus = "Digita per cercare…";
                LastSearchLevel = "";
                return;
            }
            _searchDebounce.Start();
        }

        private void OnSearchDebounceTick(object? sender, EventArgs e)
        {
            _searchDebounce.Stop();
            ExecuteSearch();
        }

        private void ExecuteSearch()
        {
            if (_searchService == null)
            {
                SearchStatus = "Nessun computo aperto";
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = _searchService.Search(SearchQuery, maxResults: 50);
                sw.Stop();

                SearchResults.Clear();
                foreach (var item in result.Items)
                    SearchResults.Add(new PriceItemRow(item));

                LastSearchLevel = result.Level.ToString();
                SearchStatus = result.Count > 0
                    ? $"{result.Count} risultati · livello {result.Level} · {sw.ElapsedMilliseconds} ms"
                    : $"Nessun risultato · {sw.ElapsedMilliseconds} ms";

                SyncFavoriteFlagsOnSearchResults();
            }
            catch (Exception ex)
            {
                SearchStatus = $"Errore ricerca: {ex.Message}";
            }
        }

        // ---------------------------------------------------------------------
        // Preferiti
        // ---------------------------------------------------------------------

        /// <summary>
        /// Carica la lista preferiti dalla UserLibrary. Chiamato all'avvio e dopo
        /// ogni operazione che modifica la tabella UserFavorites.
        /// </summary>
        public void LoadFavorites()
        {
            try
            {
                var repo = GetActiveRepo();
                if (repo == null) return;

                Favorites.Clear();
                foreach (var f in repo.GetFavorites())
                    Favorites.Add(new FavoriteRowVm(f));

                RefreshFavoritesUsage();
                RefreshFavoritesHeader();
                RefreshIsSelectedResultFavorite();
                SyncFavoriteFlagsOnSearchResults();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.LoadFavorites", ex);
            }
        }

        /// <summary>
        /// Marca ogni preferito con IsUsedInComputo = true se il suo Code è assegnato
        /// attivamente (QtoAssignments Active) nella sessione computo corrente.
        /// Se non c'è una sessione .cme aperta, tutti restano "non usati" (safe fallback).
        /// </summary>
        public void RefreshFavoritesUsage()
        {
            try
            {
                var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
                var sessionRepo = QtoApplication.Instance?.SessionManager?.Repository;
                if (session == null || sessionRepo == null)
                {
                    // No active session: mark all as unused
                    foreach (var f in Favorites) f.IsUsedInComputo = false;
                    RefreshHasUnusedFavorites();
                    return;
                }

                var used = sessionRepo.GetUsedEpCodes(session.Id);
                foreach (var f in Favorites)
                    f.IsUsedInComputo = used.Contains(f.Code);
                RefreshHasUnusedFavorites();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.RefreshFavoritesUsage", ex);
            }
        }

        private void RefreshHasUnusedFavorites()
        {
            HasUnusedFavorites = Favorites.Any(f => !f.IsUsedInComputo);
            UnusedFavoritesCount = Favorites.Count(f => !f.IsUsedInComputo);
        }

        /// <summary>Aggiorna header Expander e placeholder dopo ogni mutazione di Favorites.</summary>
        private void RefreshFavoritesHeader()
        {
            FavoritesIsEmpty = Favorites.Count == 0;
            FavoritesHeader = Favorites.Count == 0
                ? "★ I Miei Preferiti"
                : $"★ I Miei Preferiti ({Favorites.Count})";
        }

        /// <summary>Aggiorna il flag booleano IsSelectedResultFavorite dopo un cambio selezione o toggle.</summary>
        private void RefreshIsSelectedResultFavorite()
        {
            var sel = SelectedSearchResult;
            IsSelectedResultFavorite = sel != null &&
                Favorites.Any(f => f.Code == sel.Code && f.Model.ListId == (int?)sel.ListId);
        }

        /// <summary>Propaga il flag IsFavoriteInLibrary su tutti i risultati di ricerca correnti.</summary>
        private void SyncFavoriteFlagsOnSearchResults()
        {
            if (SearchResults == null) return;
            var favKeys = new HashSet<(string code, int? listId)>(
                Favorites.Select(f => (f.Code, f.Model.ListId)));
            foreach (var r in SearchResults)
                r.IsFavoriteInLibrary = favKeys.Contains((r.Code, (int?)r.ListId));
        }

        partial void OnSelectedSearchResultChanged(PriceItemRow? value)
            => RefreshIsSelectedResultFavorite();

        [RelayCommand]
        private void ToggleFavorite()
        {
            var sel = SelectedSearchResult;
            if (sel == null) return;

            var repo = GetActiveRepo();
            if (repo == null) return;

            try
            {
                if (IsSelectedResultFavorite)
                {
                    // Rimuovi il preferito esistente per questo (Code, ListId)
                    var existing = Favorites.FirstOrDefault(f => f.Code == sel.Code && f.Model.ListId == (int?)sel.ListId);
                    if (existing != null)
                    {
                        repo.RemoveFavorite(existing.Id);
                        Favorites.Remove(existing);
                    }
                }
                else
                {
                    // Aggiungi nuovo preferito
                    var fav = new UserFavorite
                    {
                        PriceItemId = sel.Id,
                        Code = sel.Code,
                        Description = sel.Description,
                        Unit = sel.Unit,
                        UnitPrice = sel.UnitPrice,
                        ListName = sel.ListName,
                        ListId = sel.ListId,
                        AddedAt = DateTime.UtcNow
                    };
                    fav.Id = repo.AddFavorite(fav);
                    Favorites.Insert(0, new FavoriteRowVm(fav));
                }

                RefreshIsSelectedResultFavorite();
                RefreshFavoritesHeader();
                RefreshFavoritesUsage(); // marca il nuovo preferito se già usato nel computo
                // Aggiorna la star icon nella riga della griglia
                sel.IsFavoriteInLibrary = IsSelectedResultFavorite;
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.ToggleFavorite", ex);
            }
        }

        [RelayCommand]
        private void UseFavoriteInSearch()
        {
            if (SelectedFavorite == null) return;
            // Imposta la SearchQuery al Code del preferito — la ricerca scatta via debounce
            SearchQuery = SelectedFavorite.Code;
        }

        /// <summary>
        /// Variante di ToggleFavorite che opera su una riga specifica passata come
        /// parametro, invece di SelectedSearchResult. Usata da:
        /// - Bottone "+/★" della prima colonna DataGrid risultati
        /// - Doppio click su una riga dei risultati
        /// Seleziona la riga prima di togliare così il pannello dettaglio e il flag
        /// IsSelectedResultFavorite restano in sync col cambio.
        /// </summary>
        [RelayCommand]
        private void ToggleFavoriteForRow(PriceItemRow? row)
        {
            if (row == null) return;
            // Forza la selezione così che la UI (bottone dettaglio, badge) si aggiorni di riflesso
            SelectedSearchResult = row;
            ToggleFavorite();
        }

        [RelayCommand]
        private void RemoveFavorite(FavoriteRowVm row)
        {
            if (row == null) return;
            var repo = GetActiveRepo();
            if (repo == null) return;
            try
            {
                repo.RemoveFavorite(row.Id);
                Favorites.Remove(row);
                RefreshIsSelectedResultFavorite();
                RefreshFavoritesHeader();
                RefreshHasUnusedFavorites();
                SyncFavoriteFlagsOnSearchResults();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.RemoveFavorite", ex);
            }
        }

        /// <summary>
        /// Rimuove dai preferiti tutte le voci NON usate nel computo corrente.
        /// NON tocca il listino: le voci restano nel catalogo, solo la "bookmark" personale
        /// sparisce. Richiede conferma utente (TaskDialog) perché è un'operazione batch.
        /// </summary>
        [RelayCommand]
        private void RemoveUnusedFavorites()
        {
            var repo = GetActiveRepo();
            if (repo == null) return;

            var unused = Favorites.Where(f => !f.IsUsedInComputo).ToList();
            if (unused.Count == 0) return;

            var td = new Autodesk.Revit.UI.TaskDialog("Rimuovi preferiti inutilizzati")
            {
                MainInstruction = $"Rimuovere {unused.Count} preferit{(unused.Count == 1 ? "o" : "i")} non utilizzat{(unused.Count == 1 ? "o" : "i")} nel computo?",
                MainContent =
                    "Verranno rimosse SOLO le voci che non risultano assegnate a elementi Revit nel computo corrente.\n\n" +
                    "Le voci corrispondenti nel listino NON vengono cancellate: potrai sempre ri-aggiungerle ai preferiti dalla ricerca.",
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.No,
                DefaultButton = Autodesk.Revit.UI.TaskDialogResult.No
            };
            if (td.Show() != Autodesk.Revit.UI.TaskDialogResult.Yes) return;

            try
            {
                var ids = unused.Select(f => f.Id).ToList();
                var deleted = repo.RemoveFavorites(ids);

                // Rimuovi dalla collection UI mantenendo l'ordine
                foreach (var f in unused) Favorites.Remove(f);

                RefreshIsSelectedResultFavorite();
                RefreshFavoritesHeader();
                RefreshHasUnusedFavorites();
                SyncFavoriteFlagsOnSearchResults();

                Autodesk.Revit.UI.TaskDialog.Show("Preferiti",
                    $"Rimoss{(deleted == 1 ? "o" : "i")} {deleted} preferit{(deleted == 1 ? "o" : "i")} inutilizzat{(deleted == 1 ? "o" : "i")}. Il listino è intatto.");
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.RemoveUnusedFavorites", ex);
            }
        }

        /// <summary>
        /// Comando UI: forza il re-check dell'uso dei preferiti nel computo
        /// (es. dopo che l'utente ha modificato la scheda Mappatura).
        /// </summary>
        [RelayCommand]
        private void RefreshFavoritesUsageFromUi() => RefreshFavoritesUsage();

        // ---------------------------------------------------------------------
        // Commands per ContextMenu (Listini + Risultati ricerca)
        // ---------------------------------------------------------------------

        /// <summary>Toggle IsActive su un listino (ContextMenu → "Attiva/Disattiva listino").</summary>
        [RelayCommand]
        private void TogglePriceListActive(PriceListRow? row)
        {
            if (row == null) return;
            // Il setter IsActive invoca OnIsActiveChanged → OnPriceListActiveToggled
            // che persiste nel DB e rilancia la ricerca.
            row.IsActive = !row.IsActive;
        }

        /// <summary>Apre il CatalogBrowser sul listino selezionato (ContextMenu).</summary>
        [RelayCommand]
        private void BrowsePriceList()
        {
            // La finestra CatalogBrowser è orchestrata dal codebehind della view
            // (dipende da window owner/owner lifetime). Qui impostiamo solo una
            // flag e deleghiamo via evento.
            BrowseRequested?.Invoke(this, System.EventArgs.Empty);
        }

        /// <summary>Evento raised dal VM quando l'utente chiede di aprire CatalogBrowser.</summary>
        public event System.EventHandler? BrowseRequested;

        /// <summary>Elimina il listino selezionato dalla libreria (ContextMenu → delega al view).</summary>
        [RelayCommand]
        private void DeleteSelectedPriceList()
        {
            DeleteRequested?.Invoke(this, System.EventArgs.Empty);
        }

        /// <summary>Evento raised dal VM quando l'utente chiede la conferma di eliminazione.
        /// La view mostra il TaskDialog e invoca DeleteSelected() se confermato.</summary>
        public event System.EventHandler? DeleteRequested;

        /// <summary>Copia il codice EP di una riga risultati negli appunti.</summary>
        [RelayCommand]
        private void CopyCodeToClipboard(PriceItemRow? row)
        {
            if (row == null || string.IsNullOrEmpty(row.Code)) return;
            try
            {
                System.Windows.Clipboard.SetText(row.Code);
                SearchStatus = $"Copiato: {row.Code}";
            }
            catch (System.Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.CopyCodeToClipboard", ex);
            }
        }

        /// <summary>
        /// Aggiunge un PriceItemRow ai preferiti solo se NON già presente.
        /// Usato dal drop handler: il drag&drop "aggiunge" sempre, non toggla,
        /// per evitare che l'utente rilasciando per sbaglio rimuova una voce.
        /// </summary>
        public void AddFavoriteFromDrop(PriceItemRow row)
        {
            if (row == null) return;
            var repo = GetActiveRepo();
            if (repo == null) return;

            // Se già preferita, no-op (il drag&drop non deve mai rimuovere)
            var alreadyFav = Favorites.Any(f => f.Code == row.Code && f.Model.ListId == (int?)row.ListId);
            if (alreadyFav) return;

            try
            {
                var fav = new UserFavorite
                {
                    PriceItemId = row.Id,
                    Code = row.Code,
                    Description = row.Description,
                    Unit = row.Unit,
                    UnitPrice = row.UnitPrice,
                    ListName = row.ListName,
                    ListId = row.ListId,
                    AddedAt = DateTime.UtcNow
                };
                fav.Id = repo.AddFavorite(fav);
                Favorites.Insert(0, new FavoriteRowVm(fav));
                row.IsFavoriteInLibrary = true;

                RefreshIsSelectedResultFavorite();
                RefreshFavoritesHeader();
                RefreshFavoritesUsage();
                SyncFavoriteFlagsOnSearchResults();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupViewModel.AddFavoriteFromDrop", ex);
            }
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static QtoRepository? GetActiveRepo()
        {
            // Listini sono nella UserLibrary globale (persistenti), NON nel .cme.
            // Così l'import del listino è one-time: disponibile per ogni computo futuro.
            return QtoApplication.Instance?.UserLibrary?.Library;
        }

        private static IPriceListParser? FindParserFor(string filePath)
        {
            var parsers = new IPriceListParser[] { new DcfParser(), new ExcelParser(), new CsvParser() };
            return parsers.FirstOrDefault(p => p.CanHandle(filePath));
        }
    }

    // -------------------------------------------------------------------------
    // Row DTOs (projection sulle entities — tengono solo ciò che il DataGrid mostra)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Riga del DataGrid "Listini attivi" — osservabile per propagare il toggle
    /// IsActive al repository (persistenza + invalidazione cache ricerca).
    /// </summary>
    public partial class PriceListRow : ObservableObject
    {
        private readonly SetupViewModel? _owner;

        /// <summary>
        /// Costruttore con owner — usato da SetupViewModel per abilitare la persistenza del toggle IsActive.
        /// </summary>
        public PriceListRow(SetupViewModel owner, PriceList list) : this(list)
        {
            _owner = owner;
        }

        /// <summary>
        /// Costruttore senza owner — usato da CatalogBrowserViewModel (IsActive è read-only in quel contesto).
        /// </summary>
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
            _isActive = list.IsActive; // set via field to avoid triggering OnIsActiveChanged at load
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
        /// <summary>Description completa multi-line (es. livello3 + livello4 concatenati in EASY Toscana).</summary>
        public string Description { get; }
        public string Unit { get; }
        public double UnitPrice { get; }
        public string ListName { get; }

        [ObservableProperty] private bool _isFavoriteInLibrary;

        /// <summary>Label del MenuItem "toggle preferiti" nel context menu dei risultati ricerca.
        /// Cambia dinamicamente: "★ Aggiungi ai preferiti" o "★ Rimuovi dai preferiti".</summary>
        public string FavoriteMenuLabel => IsFavoriteInLibrary
            ? "★ Rimuovi dai preferiti"
            : "★ Aggiungi ai preferiti";

        partial void OnIsFavoriteInLibraryChanged(bool value) => OnPropertyChanged(nameof(FavoriteMenuLabel));

        public string ShortDescTrimmed =>
            string.IsNullOrEmpty(ShortDesc) ? "" :
            ShortDesc.Length > 140 ? ShortDesc.Substring(0, 140).Replace('\n', ' ') + "…" :
            ShortDesc.Replace('\n', ' ');

        public string UnitPriceFormatted => UnitPrice > 0 ? $"€ {UnitPrice:N2}" : "—";

        /// <summary>Path gerarchico visualizzabile nel detail panel (Super &gt; Chapter &gt; Sub).</summary>
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
    }
}
