using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
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
    /// VM per la SelectionView (§I3 v1 — versione pragmatica): dropdown categoria +
    /// ricerca nome + tabella elementi + comandi Isola/Nascondi/Reset.
    /// Il FilterBuilder dinamico con regole parametriche composte è v2 (futuro).
    /// </summary>
    public partial class SelectionViewModel : ViewModelBase
    {
        private readonly SelectionService _service = new SelectionService();
        private readonly DispatcherTimer _searchDebounce;

        public ObservableCollection<CategoryItemVm> Categories { get; } = new();
        public ObservableCollection<ElementRowVm> Elements { get; } = new();

        [ObservableProperty] private CategoryItemVm? _selectedCategory;
        [ObservableProperty] private string _nameQuery = string.Empty;
        [ObservableProperty] private string _statusMessage = "Seleziona una categoria";
        [ObservableProperty] private bool _filterByActivePhase = true;
        [ObservableProperty] private int _activePhaseId;
        [ObservableProperty] private string _activePhaseName = "";

        public SelectionViewModel()
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _searchDebounce.Tick += OnSearchDebounceTick;

            foreach (var (bic, label) in SelectionService.PopularCategories)
                Categories.Add(new CategoryItemVm(bic, label));

            if (QtoApplication.Instance?.SessionManager != null)
            {
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => RefreshFromSession();
            }
            RefreshFromSession();
        }

        public void RefreshFromSession()
        {
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (session != null && session.ActivePhaseId > 0)
            {
                ActivePhaseId = session.ActivePhaseId;
                ActivePhaseName = session.ActivePhaseName;
            }
            else
            {
                ActivePhaseId = 0;
                ActivePhaseName = "";
            }
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

        partial void OnFilterByActivePhaseChanged(bool value) => Search();

        private void OnSearchDebounceTick(object? sender, EventArgs e)
        {
            _searchDebounce.Stop();
            Search();
        }

        /// <summary>Esegue la query con i parametri correnti e popola <see cref="Elements"/>.</summary>
        public void Search()
        {
            Elements.Clear();
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
                int? phaseFilter = (FilterByActivePhase && ActivePhaseId > 0) ? ActivePhaseId : (int?)null;

                var results = _service.FindElements(
                    doc,
                    SelectedCategory.Bic,
                    NameQuery,
                    phaseFilter);
                sw.Stop();

                foreach (var info in results)
                    Elements.Add(new ElementRowVm(info));

                StatusMessage = $"{results.Count} elementi · categoria «{SelectedCategory.Label}»" +
                                (phaseFilter.HasValue ? $" · fase «{ActivePhaseName}»" : " · tutte le fasi") +
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
}
