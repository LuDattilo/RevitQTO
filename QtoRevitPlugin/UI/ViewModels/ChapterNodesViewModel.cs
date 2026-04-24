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
    /// Gestisce l'albero dei ChapterNode (SpCap → Cap → SbCap) per il Setup.
    /// Usa IChapterService (Plan C-2) per operazioni CRUD con validazioni.
    /// </summary>
    public partial class ChapterNodesViewModel : ViewModelBase
    {
        private int _documentId;

        public ObservableCollection<ChapterNodeVm> RootNodes { get; } = new();

        [ObservableProperty] private ChapterNodeVm? _selectedNode;
        [ObservableProperty] private string _newCodice = "";
        [ObservableProperty] private string _newDesSintetica = "";
        [ObservableProperty] private string _statusMessage = "Nessun documento attivo.";

        public ChapterNodesViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
            {
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => Reload();
            }
            Reload();
        }

        partial void OnSelectedNodeChanged(ChapterNodeVm? value)
        {
            OnPropertyChanged(nameof(CanAddChapter));
            OnPropertyChanged(nameof(CanAddSubChapter));
            AddChapterCommand.NotifyCanExecuteChanged();
            AddSubChapterCommand.NotifyCanExecuteChanged();
        }

        public bool CanAddChapter => SelectedNode?.Model.Level == "SpCap";
        public bool CanAddSubChapter => SelectedNode?.Model.Level == "Cap";

        [RelayCommand]
        private void Reload()
        {
            RootNodes.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || sess == null)
            {
                _documentId = 0;
                StatusMessage = "Nessuna sessione attiva.";
                return;
            }
            try
            {
                var docSvc = new ComputoDocumentService(repo);
                var doc = docSvc.GetOrCreate(sess.Id);
                _documentId = doc.Id;

                var chapSvc = new ChapterService(repo);
                var all = chapSvc.GetAll(_documentId).ToList();
                BuildTree(all);
                StatusMessage = $"{all.Count} capitoli nel documento #{_documentId}";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Errore caricamento: {ex.Message}";
            }
        }

        private void BuildTree(List<ChapterNode> all)
        {
            var lookup = all.ToDictionary(n => n.Id, n => new ChapterNodeVm(n));
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
        private void AddSuperChapter()
        {
            if (_documentId == 0) { StatusMessage = "Nessun documento attivo."; return; }
            TryExecute(() =>
            {
                var svc = new ChapterService(QtoApplication.Instance!.SessionManager!.Repository!);
                svc.AddSuperChapter(_documentId, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm();
                Reload();
            });
        }

        [RelayCommand(CanExecute = nameof(CanAddChapter))]
        private void AddChapter()
        {
            if (_documentId == 0 || SelectedNode == null) return;
            TryExecute(() =>
            {
                var svc = new ChapterService(QtoApplication.Instance!.SessionManager!.Repository!);
                svc.AddChapter(_documentId, SelectedNode.Model.Id, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm();
                Reload();
            });
        }

        [RelayCommand(CanExecute = nameof(CanAddSubChapter))]
        private void AddSubChapter()
        {
            if (_documentId == 0 || SelectedNode == null) return;
            TryExecute(() =>
            {
                var svc = new ChapterService(QtoApplication.Instance!.SessionManager!.Repository!);
                svc.AddSubChapter(_documentId, SelectedNode.Model.Id, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm();
                Reload();
            });
        }

        [RelayCommand]
        private void DeleteSelected()
        {
            if (SelectedNode == null) { StatusMessage = "Seleziona un nodo da eliminare."; return; }
            TryExecute(() =>
            {
                var svc = new ChapterService(QtoApplication.Instance!.SessionManager!.Repository!);
                svc.Delete(SelectedNode.Model.Id);
                SelectedNode = null;
                Reload();
            });
        }

        private void TryExecute(System.Action action)
        {
            try { action(); }
            catch (DomainValidationException dex) { StatusMessage = $"{dex.RuleCode}: {dex.Message}"; }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void ResetForm()
        {
            NewCodice = "";
            NewDesSintetica = "";
        }
    }

    public class ChapterNodeVm
    {
        public ChapterNode Model { get; }
        public ObservableCollection<ChapterNodeVm> Children { get; } = new();
        public ChapterNodeVm(ChapterNode model) => Model = model;
        public string DisplayLabel => $"{Model.Codice} · {Model.DesSintetica} [{Model.Level}]";
    }
}
