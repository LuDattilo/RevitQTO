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
    /// Gestisce l'albero WBS a profondità libera. Due alberi indipendenti per Kind
    /// (WbsCap = sul Prezziario, WbsComputo = sulle righe di computo).
    /// </summary>
    public partial class WbsNodesViewModel : ViewModelBase
    {
        private int _documentId;

        public ObservableCollection<WbsNodeVm> RootNodes { get; } = new();
        public ObservableCollection<string> Kinds { get; } = new() { "WbsCap", "WbsComputo" };

        [ObservableProperty] private WbsNodeVm? _selectedNode;
        [ObservableProperty] private string _selectedKind = "WbsComputo";
        [ObservableProperty] private string _newDesSintetica = "";
        [ObservableProperty] private string _statusMessage = "";

        public WbsNodesViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => Reload();
            Reload();
        }

        partial void OnSelectedKindChanged(string value) => Reload();

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
                var svc = new WbsService(repo);
                var all = svc.GetAll(_documentId, SelectedKind).ToList();
                BuildTree(all);
                StatusMessage = $"{all.Count} nodi ({SelectedKind})";
            }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void BuildTree(List<WbsNode> all)
        {
            var lookup = all.ToDictionary(n => n.Id, n => new WbsNodeVm(n));
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
        private void AddRoot()
        {
            if (_documentId == 0) return;
            TryExecute(() =>
            {
                new WbsService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .Add(_documentId, SelectedKind, null, NewDesSintetica.Trim());
                NewDesSintetica = ""; Reload();
            });
        }

        [RelayCommand]
        private void AddChild()
        {
            if (_documentId == 0 || SelectedNode == null) { StatusMessage = "Seleziona un nodo padre."; return; }
            TryExecute(() =>
            {
                new WbsService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .Add(_documentId, SelectedKind, SelectedNode.Model.Id, NewDesSintetica.Trim());
                NewDesSintetica = ""; Reload();
            });
        }

        [RelayCommand]
        private void DeleteSelected()
        {
            if (SelectedNode == null) { StatusMessage = "Seleziona un nodo."; return; }
            TryExecute(() =>
            {
                new WbsService(QtoApplication.Instance!.SessionManager!.Repository!)
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
    }

    public class WbsNodeVm
    {
        public WbsNode Model { get; }
        public ObservableCollection<WbsNodeVm> Children { get; } = new();
        public WbsNodeVm(WbsNode model) => Model = model;
        public string DisplayLabel => $"{Model.Codice} · {Model.DesSintetica}";
    }
}
