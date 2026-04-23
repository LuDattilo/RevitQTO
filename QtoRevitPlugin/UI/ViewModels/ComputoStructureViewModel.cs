using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// VM per la tab "Struttura Computo" nel DockablePane:
    /// gestisce CRUD sui ComputoChapter (3 livelli: Super→Cat→Sub),
    /// popola il TreeView e calcola i count di assegnazioni per capitolo.
    /// </summary>
    public partial class ComputoStructureViewModel : ObservableObject
    {
        private IQtoRepository? _repo;
        private int _sessionId;

        public ObservableCollection<ComputoChapterViewModel> Roots { get; } = new ObservableCollection<ComputoChapterViewModel>();

        [ObservableProperty] private ComputoChapterViewModel? _selectedNode;
        [ObservableProperty] private string _statusMessage = string.Empty;

        public ComputoStructureViewModel()
        {
            Reload();
        }

        public void Reload()
        {
            _repo = QtoApplication.Instance?.SessionManager?.Repository;
            _sessionId = QtoApplication.Instance?.SessionManager?.ActiveSession?.Id ?? 0;
            Roots.Clear();

            if (_repo == null || _sessionId == 0)
            {
                StatusMessage = "Nessun computo aperto.";
                return;
            }

            var all = _repo.GetComputoChapters(_sessionId);
            var assignments = _repo.GetAssignments(_sessionId);
            var countByChapter = assignments
                .Where(a => a.ComputoChapterId.HasValue && a.AuditStatus == AssignmentStatus.Active)
                .GroupBy(a => a.ComputoChapterId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var byId = all.ToDictionary(c => c.Id, c => new ComputoChapterViewModel(c)
            {
                DirectAssignmentsCount = countByChapter.TryGetValue(c.Id, out var cnt) ? cnt : 0
            });

            foreach (var vm in byId.Values.OrderBy(v => v.Model.SortOrder).ThenBy(v => v.Model.Code))
            {
                if (vm.Model.ParentChapterId.HasValue && byId.TryGetValue(vm.Model.ParentChapterId.Value, out var parent))
                    parent.Children.Add(vm);
                else if (vm.Model.Level == 1)
                    Roots.Add(vm);
            }

            StatusMessage = $"{all.Count} capitoli · {countByChapter.Sum(kv => kv.Value)} voci assegnate.";
        }

        [RelayCommand]
        private void AddSuper()
        {
            if (_repo == null) return;
            var ch = new ComputoChapter
            {
                SessionId = _sessionId,
                Code = NextCode(null, 1),
                Name = "Nuovo",
                Level = 1,
                SortOrder = Roots.Count,
                CreatedAt = DateTime.UtcNow
            };
            _repo.InsertComputoChapter(ch);
            Reload();
        }

        [RelayCommand]
        private void AddCategory()
        {
            if (_repo == null || SelectedNode == null || SelectedNode.Model.Level != 1) return;
            var parent = SelectedNode.Model;
            var ch = new ComputoChapter
            {
                SessionId = _sessionId,
                ParentChapterId = parent.Id,
                Code = NextCode(parent.Code, 2),
                Name = "Nuovo",
                Level = 2,
                SortOrder = SelectedNode.Children.Count,
                CreatedAt = DateTime.UtcNow
            };
            _repo.InsertComputoChapter(ch);
            Reload();
        }

        [RelayCommand]
        private void AddSubCategory()
        {
            if (_repo == null || SelectedNode == null || SelectedNode.Model.Level != 2) return;
            var parent = SelectedNode.Model;
            var ch = new ComputoChapter
            {
                SessionId = _sessionId,
                ParentChapterId = parent.Id,
                Code = NextCode(parent.Code, 3),
                Name = "Nuovo",
                Level = 3,
                SortOrder = SelectedNode.Children.Count,
                CreatedAt = DateTime.UtcNow
            };
            _repo.InsertComputoChapter(ch);
            Reload();
        }

        [RelayCommand]
        private void Delete()
        {
            if (_repo == null || SelectedNode == null) return;
            var msg = SelectedNode.TotalCount > 0
                ? $"Il capitolo contiene {SelectedNode.TotalCount} voci che torneranno a '(senza capitolo)'. Continuare?"
                : $"Eliminare '{SelectedNode.Model.Code} {SelectedNode.Model.Name}'?";
            var result = System.Windows.MessageBox.Show(msg, "Elimina capitolo",
                System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.OK) return;

            _repo.DeleteComputoChapter(SelectedNode.Model.Id);
            Reload();
        }

        [RelayCommand]
        private void Rename()
        {
            if (_repo == null || SelectedNode == null) return;
            // Popup di edit — delegato a ChapterEditorPopup in Task 9
            var popup = new QtoRevitPlugin.UI.Views.ChapterEditorPopup(SelectedNode.Model);
            if (popup.ShowDialog() == true)
            {
                _repo.UpdateComputoChapter(SelectedNode.Model);
                Reload();
            }
        }

        private string NextCode(string? parentCode, int level)
        {
            var siblings = _repo!.GetComputoChapters(_sessionId)
                .Where(c => c.Level == level && (parentCode == null || (c.Code.StartsWith(parentCode + ".") && c.Code.Count(ch => ch == '.') == parentCode.Count(ch => ch == '.') + 1)))
                .ToList();
            if (parentCode == null)
                return (siblings.Count + 1).ToString("D2");
            var prefix = parentCode + ".";
            var nextIdx = siblings.Count + 1;
            return level == 2
                ? $"{prefix}{(char)('A' + nextIdx - 1)}"
                : $"{prefix}{nextIdx:D2}";
        }
    }
}
