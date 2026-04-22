using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// VM per il CatalogBrowserWindow: costruisce un TreeView gerarchico del listino
    /// (SuperChapter → Chapter → SubChapter → Voce) con count per livello e filtro testo.
    ///
    /// Performance: su 23k voci, build del tree in ~100-300ms. Il filtro è applicato
    /// collapsing/hiding i nodi in-place, senza rebuild dell'albero.
    /// </summary>
    public partial class CatalogBrowserViewModel : ViewModelBase
    {
        private readonly FileFavoritesRepository _favoritesRepo;
        private readonly MappingRulesService _mappingRulesService;

        public ObservableCollection<PriceListRow> AvailableLists { get; } = new();
        public ObservableCollection<CatalogNode> Tree { get; } = new();

        [ObservableProperty] private PriceListRow? _selectedList;
        [ObservableProperty] private CatalogNode? _selectedNode;
        [ObservableProperty] private string _filterText = string.Empty;
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private bool _isLoading;

        [ObservableProperty] private ObservableCollection<FavoriteItem> _favorites = new ObservableCollection<FavoriteItem>();
        [ObservableProperty] private FavoriteItem? _activeFavoriteItem;
        [ObservableProperty] private string _selectedQuantityParam = "Count";
        [ObservableProperty] private ObservableCollection<string> _allowedParams = new ObservableCollection<string> { "Area", "Volume", "Length", "Count" };
        [ObservableProperty] private bool _canAssign;

        public CatalogBrowserViewModel()
        {
            _favoritesRepo = new FileFavoritesRepository(FileFavoritesRepository.GetDefaultGlobalDir());
            _mappingRulesService = new MappingRulesService();
            LoadAvailableLists();
            LoadFavorites();
        }

        public void LoadAvailableLists()
        {
            AvailableLists.Clear();
            // Listini sono nella UserLibrary globale, non nel .cme
            var repo = QtoApplication.Instance?.UserLibrary?.Library;
            if (repo == null)
            {
                StatusMessage = "UserLibrary non inizializzata — riavvia Revit.";
                return;
            }

            var lists = repo.GetPriceLists();
            foreach (var l in lists)
                AvailableLists.Add(new PriceListRow(l));

            if (AvailableLists.Count == 0)
            {
                StatusMessage = "Nessun listino caricato nel computo. Importane uno dalla view Setup.";
            }
            else
            {
                // Seleziona automaticamente il primo listino attivo
                SelectedList = AvailableLists.FirstOrDefault(l => l.IsActive) ?? AvailableLists[0];
            }
        }

        partial void OnSelectedListChanged(PriceListRow? value)
        {
            if (value != null) BuildTreeForList(value.Id);
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter(value);

        private void BuildTreeForList(int listId)
        {
            Tree.Clear();
            var repo = QtoApplication.Instance?.UserLibrary?.Library;
            if (repo == null) return;

            IsLoading = true;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var items = repo.GetPriceItemsByList(listId);
                sw.Stop();

                // Raggruppamento a 3 livelli: Super → Chapter → SubChapter → Voce
                // Se un livello è vuoto (stringa empty), normalizziamo a "(senza capitolo)" per raggrupparli
                foreach (var superGroup in items
                    .GroupBy(i => EmptyToLabel(i.SuperChapter, "(senza super capitolo)"))
                    .OrderBy(g => g.Key))
                {
                    var superNode = new CatalogNode(superGroup.Key, superGroup.Count());

                    foreach (var chapterGroup in superGroup
                        .GroupBy(i => EmptyToLabel(i.Chapter, "(senza capitolo)"))
                        .OrderBy(g => g.Key))
                    {
                        var chapterNode = new CatalogNode(chapterGroup.Key, chapterGroup.Count());

                        // Se esistono SubChapter variati, aggiungo livello intermedio
                        var distinctSubs = chapterGroup.Select(i => i.SubChapter ?? "").Distinct().ToList();
                        var hasSubChapters = distinctSubs.Count > 1 ||
                                             (distinctSubs.Count == 1 && !string.IsNullOrEmpty(distinctSubs[0]));

                        if (hasSubChapters)
                        {
                            foreach (var subGroup in chapterGroup
                                .GroupBy(i => EmptyToLabel(i.SubChapter, "(varie)"))
                                .OrderBy(g => g.Key))
                            {
                                var subNode = new CatalogNode(subGroup.Key, subGroup.Count());
                                foreach (var item in subGroup.OrderBy(i => i.Code))
                                    subNode.Children.Add(new CatalogNode(item));
                                chapterNode.Children.Add(subNode);
                            }
                        }
                        else
                        {
                            foreach (var item in chapterGroup.OrderBy(i => i.Code))
                                chapterNode.Children.Add(new CatalogNode(item));
                        }

                        superNode.Children.Add(chapterNode);
                    }

                    Tree.Add(superNode);
                }

                StatusMessage = $"{items.Count} voci · {Tree.Count} super-capitoli · caricato in {sw.ElapsedMilliseconds} ms";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore caricamento: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilter(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                foreach (var node in Tree) node.ResetVisibility();
                return;
            }

            var needle = filter.Trim().ToLowerInvariant();
            int totalVisible = 0;
            foreach (var node in Tree)
                totalVisible += node.ApplyFilter(needle);

            StatusMessage = $"Filtro «{filter}» · {totalVisible} voci visibili";
        }

        private static string EmptyToLabel(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value!;

        // ── Favorites ────────────────────────────────────────────────────────────

        private void LoadFavorites()
        {
            var set = _favoritesRepo.LoadGlobal();
            Favorites.Clear();
            foreach (var item in set.Items)
                Favorites.Add(item);
        }

        public void AddToFavorites(FavoriteItem item)
        {
            if (!Favorites.Contains(item))
                Favorites.Add(item);
            SaveFavorites();
        }

        public void RemoveFromFavorites(FavoriteItem item)
        {
            Favorites.Remove(item);
            SaveFavorites();
        }

        private void SaveFavorites()
        {
            var set = new FavoriteSet { Items = new System.Collections.Generic.List<FavoriteItem>(Favorites) };
            _favoritesRepo.SaveGlobal(set);
        }

        public void OnRevitCategoryChanged(string revitCategoryOst)
        {
            var rule = _mappingRulesService.GetRule(revitCategoryOst);
            SelectedQuantityParam = rule.DefaultParam;
            AllowedParams = new ObservableCollection<string>(rule.AllowedParams);
        }

        partial void OnActiveFavoriteItemChanged(FavoriteItem? value)
            => CanAssign = value != null;

        [RelayCommand]
        private void AddSelectedToFavorites()
        {
            // stub — will be wired to selection in Task 11
        }

        [RelayCommand]
        private void RemoveFavorite()
        {
            if (ActiveFavoriteItem != null)
                RemoveFromFavorites(ActiveFavoriteItem);
        }

        [RelayCommand]
        private void Assign()
        {
            // stub — will be implemented in Task 11
        }
    }

    /// <summary>
    /// Nodo del TreeView: può essere un gruppo (SuperChapter/Chapter/SubChapter)
    /// con <see cref="Children"/> o una foglia (<see cref="Leaf"/> = PriceItem singolo).
    /// </summary>
    public partial class CatalogNode : ObservableObject
    {
        public CatalogNode(string label, int directCount)
        {
            Label = label;
            DirectCount = directCount;
            Children = new ObservableCollection<CatalogNode>();
        }

        public CatalogNode(PriceItem item)
        {
            Leaf = item;
            Label = $"{item.Code}  ·  {TruncateToOneLine(FirstNonEmpty(item.ShortDesc, item.Description), 80)}";
            Children = new ObservableCollection<CatalogNode>();
            DirectCount = 0;
        }

        public string Label { get; }
        public int DirectCount { get; }
        public PriceItem? Leaf { get; }
        public ObservableCollection<CatalogNode> Children { get; }

        [ObservableProperty] private bool _isExpanded;
        [ObservableProperty] private bool _isVisible = true;

        /// <summary>True se il nodo è un raggruppamento (ha figli, non è una voce singola).</summary>
        public bool IsGroup => Leaf == null;

        public string CountLabel => IsGroup ? $"{DirectCount} voci" : (Leaf!.UnitPrice > 0 ? $"€ {Leaf.UnitPrice:N2}" : "—");

        /// <summary>Etichetta tooltip espansa (per leaf): gerarchia + descrizione full.</summary>
        public string Tooltip
        {
            get
            {
                if (Leaf == null) return Label;
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Leaf.Code)) parts.Add(Leaf.Code);
                if (!string.IsNullOrEmpty(Leaf.Unit)) parts.Add($"UM: {Leaf.Unit}");
                if (Leaf.UnitPrice > 0) parts.Add($"€ {Leaf.UnitPrice:N2}");
                if (!string.IsNullOrEmpty(Leaf.Description)) parts.Add("\n" + Leaf.Description);
                return string.Join("  ·  ", parts);
            }
        }

        /// <summary>
        /// Applica il filtro ricorsivamente: leaf → match su Code/ShortDesc/Description,
        /// group → visibile se almeno un descendant è visibile. Ritorna count leaf visibili.
        /// </summary>
        public int ApplyFilter(string lowerNeedle)
        {
            if (!IsGroup)
            {
                // Match case-insensitive su Code, ShortDesc, Description
                IsVisible = MatchesFilter(lowerNeedle);
                return IsVisible ? 1 : 0;
            }

            int visibleChildren = 0;
            foreach (var child in Children)
                visibleChildren += child.ApplyFilter(lowerNeedle);

            IsVisible = visibleChildren > 0;
            if (IsVisible) IsExpanded = true; // espandi automaticamente i rami che hanno match
            return visibleChildren;
        }

        private bool MatchesFilter(string lower)
        {
            if (Leaf == null) return false;
            if (Leaf.Code?.ToLowerInvariant().Contains(lower) == true) return true;
            if (Leaf.ShortDesc?.ToLowerInvariant().Contains(lower) == true) return true;
            if (Leaf.Description?.ToLowerInvariant().Contains(lower) == true) return true;
            return false;
        }

        public void ResetVisibility()
        {
            IsVisible = true;
            IsExpanded = false;
            foreach (var c in Children) c.ResetVisibility();
        }

        private static string FirstNonEmpty(string? a, string? b) =>
            !string.IsNullOrEmpty(a) ? a! : (b ?? "");

        private static string TruncateToOneLine(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s!.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
