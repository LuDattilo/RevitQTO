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
        private PriceItemSearchService? _searchService;

        // ---------------------------------------------------------------------
        // Collections bindate al DataGrid
        // ---------------------------------------------------------------------

        public ObservableCollection<PriceListRow> PriceLists { get; } = new();
        public ObservableCollection<PriceItemRow> SearchResults { get; } = new();

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

        public bool HasSessionActive => QtoApplication.Instance?.SessionManager?.HasActiveSession ?? false;

        // ---------------------------------------------------------------------
        // Ctor
        // ---------------------------------------------------------------------

        public SetupViewModel()
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += OnSearchDebounceTick;

            // Aggiorna quando la sessione cambia (nuovo computo = listini diversi)
            if (QtoApplication.Instance?.SessionManager != null)
            {
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => RefreshPriceLists();
            }

            RefreshPriceLists();
        }

        // ---------------------------------------------------------------------
        // Listini
        // ---------------------------------------------------------------------

        public void RefreshPriceLists()
        {
            PriceLists.Clear();
            var repo = GetActiveRepo();
            if (repo == null)
            {
                StatusMessage = "Nessun computo aperto — apri un file .cme per gestire i listini";
                return;
            }

            try
            {
                var lists = repo.GetPriceLists();
                foreach (var l in lists)
                {
                    PriceLists.Add(new PriceListRow(l));
                }

                StatusMessage = lists.Count == 0
                    ? "Nessun listino caricato — clicca «Importa listino…»"
                    : $"{lists.Count} listino(i) · {lists.Sum(l => l.RowCount)} voci totali";

                // Reset search service cache
                _searchService = new PriceItemSearchService(repo);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore lettura listini: {ex.Message}";
            }
        }

        public PriceListImportResult? ImportFromFile(string filePath)
        {
            var repo = GetActiveRepo();
            if (repo == null) throw new InvalidOperationException("Nessun computo attivo.");

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
            }
            catch (Exception ex)
            {
                SearchStatus = $"Errore ricerca: {ex.Message}";
            }
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static QtoRepository? GetActiveRepo()
        {
            return QtoApplication.Instance?.SessionManager?.Repository;
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

    public class PriceListRow
    {
        public PriceListRow(PriceList list)
        {
            Id = list.Id;
            Name = list.Name;
            Source = list.Source;
            Region = list.Region;
            Version = list.Version;
            RowCount = list.RowCount;
            Priority = list.Priority;
            IsActive = list.IsActive;
            ImportedAt = list.ImportedAt;
        }

        public int Id { get; }
        public string Name { get; }
        public string Source { get; }
        public string Region { get; }
        public string Version { get; }
        public int RowCount { get; }
        public int Priority { get; }
        public bool IsActive { get; }
        public DateTime ImportedAt { get; }

        public string ImportedAtShort => ImportedAt == default ? "" : ImportedAt.ToLocalTime().ToString("dd/MM HH:mm");
    }

    public class PriceItemRow
    {
        public PriceItemRow(PriceItem item)
        {
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
                var parts = new System.Collections.Generic.List<string>(3);
                if (!string.IsNullOrWhiteSpace(SuperChapter)) parts.Add(SuperChapter);
                if (!string.IsNullOrWhiteSpace(Chapter)) parts.Add(Chapter);
                if (!string.IsNullOrWhiteSpace(SubChapter)) parts.Add(SubChapter);
                return string.Join("  ›  ", parts);
            }
        }
    }
}
