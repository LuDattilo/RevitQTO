using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Gestisce l'albero dei CategoryNode (SpCat → Cat → SbCat) per il Setup.
    /// Runtime-defined per documento (non c'è standard SOA: quelle sono un'altra entità).
    /// </summary>
    public partial class CategoryNodesViewModel : ViewModelBase
    {
        private int _documentId;

        public ObservableCollection<CategoryNodeVm> RootNodes { get; } = new();

        [ObservableProperty] private CategoryNodeVm? _selectedNode;
        [ObservableProperty] private string _newCodice = "";
        [ObservableProperty] private string _newDesSintetica = "";
        [ObservableProperty] private string _statusMessage = "Nessun documento attivo.";

        public CategoryNodesViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => Reload();
            Reload();
        }

        partial void OnSelectedNodeChanged(CategoryNodeVm? value)
        {
            OnPropertyChanged(nameof(CanAddCategory));
            OnPropertyChanged(nameof(CanAddSubCategory));
            AddCategoryCommand.NotifyCanExecuteChanged();
            AddSubCategoryCommand.NotifyCanExecuteChanged();
        }

        public bool CanAddCategory => SelectedNode?.Model.Level == "SpCat";
        public bool CanAddSubCategory => SelectedNode?.Model.Level == "Cat";

        [RelayCommand]
        private void Reload()
        {
            RootNodes.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || sess == null) { _documentId = 0; StatusMessage = "Nessuna sessione."; return; }
            try
            {
                var docSvc = new ComputoDocumentService(repo);
                var doc = docSvc.GetOrCreate(sess.Id);
                _documentId = doc.Id;
                var svc = new CategoryService(repo);
                var all = svc.GetAll(_documentId).ToList();
                BuildTree(all);
                StatusMessage = $"{all.Count} categorie nel documento #{_documentId}";
            }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void BuildTree(List<CategoryNode> all)
        {
            var lookup = all.ToDictionary(n => n.Id, n => new CategoryNodeVm(n));
            foreach (var kv in lookup)
            {
                var vm = kv.Value;
                if (vm.Model.ParentId.HasValue && lookup.TryGetValue(vm.Model.ParentId.Value, out var parent))
                    parent.Children.Add(vm);
                else
                    RootNodes.Add(vm);
            }
        }

        [RelayCommand]
        private void AddSuperCategory()
        {
            if (_documentId == 0) return;
            TryExecute(() =>
            {
                new CategoryService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .AddSuperCategory(_documentId, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm(); Reload();
            });
        }

        [RelayCommand(CanExecute = nameof(CanAddCategory))]
        private void AddCategory()
        {
            if (_documentId == 0 || SelectedNode == null) return;
            TryExecute(() =>
            {
                new CategoryService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .AddCategory(_documentId, SelectedNode.Model.Id, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm(); Reload();
            });
        }

        [RelayCommand(CanExecute = nameof(CanAddSubCategory))]
        private void AddSubCategory()
        {
            if (_documentId == 0 || SelectedNode == null) return;
            TryExecute(() =>
            {
                new CategoryService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .AddSubCategory(_documentId, SelectedNode.Model.Id, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm(); Reload();
            });
        }

        [RelayCommand]
        private void DeleteSelected()
        {
            if (SelectedNode == null) { StatusMessage = "Seleziona un nodo."; return; }
            TryExecute(() =>
            {
                new CategoryService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .Delete(SelectedNode.Model.Id);
                SelectedNode = null; Reload();
            });
        }

        private void TryExecute(System.Action action)
        {
            try { action(); }
            catch (DomainValidationException dex) { StatusMessage = $"{dex.RuleCode}: {dex.Message}"; }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void ResetForm() { NewCodice = ""; NewDesSintetica = ""; }
    }

    public class CategoryNodeVm
    {
        public CategoryNode Model { get; }
        public ObservableCollection<CategoryNodeVm> Children { get; } = new();
        public CategoryNodeVm(CategoryNode model) => Model = model;
        public string DisplayLabel => $"{Model.Codice} · {Model.DesSintetica} [{Model.Level}]";
    }
}
