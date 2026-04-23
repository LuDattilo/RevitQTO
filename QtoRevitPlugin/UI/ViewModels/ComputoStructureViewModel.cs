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

        /// <summary>
        /// Elenco SOA caricato dal DB (read-only, letto una volta al Reload).
        /// Binding del ComboBox OG/OS nel pannello dettaglio nodo.
        /// </summary>
        public ObservableCollection<SoaCategory> AvailableSoa { get; } = new ObservableCollection<SoaCategory>();

        [ObservableProperty] private ComputoChapterViewModel? _selectedNode;
        [ObservableProperty] private string _statusMessage = string.Empty;

        /// <summary>SOA corrente del nodo selezionato (null = non assegnato proprio).
        /// Bind TwoWay col ComboBox: set scatena UpdateSoaOnSelectedNode.</summary>
        [ObservableProperty] private SoaCategory? _selectedNodeSoa;

        public ComputoStructureViewModel()
        {
            Reload();
        }

        partial void OnSelectedNodeChanged(ComputoChapterViewModel? value)
        {
            // Sincronizza SelectedNodeSoa col nodo selezionato (no feedback loop:
            // check sul valore prima di set).
            var expected = value?.OwnSoaCategoryId is int id
                ? AvailableSoa.FirstOrDefault(s => s.Id == id)
                : null;
            if (!ReferenceEquals(_selectedNodeSoa, expected))
            {
                _isSyncingSoa = true;
                SelectedNodeSoa = expected;
                _isSyncingSoa = false;
            }
        }

        private bool _isSyncingSoa;
        partial void OnSelectedNodeSoaChanged(SoaCategory? value)
        {
            if (_isSyncingSoa) return;            // stiamo solo sincronizzando UI, non salvare
            if (_repo == null || SelectedNode == null) return;

            var newId = value?.Id;
            if (SelectedNode.Model.SoaCategoryId == newId) return;  // no-op

            SelectedNode.Model.SoaCategoryId = newId;
            try
            {
                _repo.UpdateComputoChapter(SelectedNode.Model);
                // Aggiorna EffectiveSoaCode sull'intero sottoalbero (ereditarietà implicita)
                RefreshEffectiveSoa();
                StatusMessage = value == null
                    ? $"Rimosso codice SOA da «{SelectedNode.Model.Code}»."
                    : $"Assegnato {value.Code} a «{SelectedNode.Model.Code}».";
            }
            catch (Exception ex)
            {
                QtoRevitPlugin.Services.CrashLogger.WriteException("ComputoStructureViewModel.UpdateSoa", ex);
                StatusMessage = $"Errore salvataggio SOA: {ex.Message}";
            }
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

            // MED-C2: nota — SQLite con singola SqliteConnection fornisce letture
            // consistenti all'interno della stessa operazione (no MVCC gap). Un
            // transaction esplicita per READ richiederebbe di esporre BeginTransaction
            // sull'IQtoRepository con un tipo astratto (IDbTransaction) — non ne
            // vale la pena finché il VM non ha scritture concorrenti reali.
            // Le 2 query GetComputoChapters + GetAssignments sono sicure qui.
            var all = _repo.GetComputoChapters(_sessionId);
            var assignments = _repo.GetAssignments(_sessionId);

            // Load SOA categorie (read-only, cache una volta per Reload)
            AvailableSoa.Clear();
            foreach (var soa in _repo.GetSoaCategories())
                AvailableSoa.Add(soa);
            _soaById = AvailableSoa.ToDictionary(s => s.Id, s => s);

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
                {
                    parent.Children.Add(vm);
                    vm.SetParent(parent);
                }
                else if (vm.Model.Level == 1)
                {
                    Roots.Add(vm);
                    vm.SetParent(null);
                }
            }

            // Risolvi EffectiveSoaCode su tutti i nodi (eredità implicita)
            foreach (var root in Roots)
                RefreshEffectiveSoaRecursive(root);

            StatusMessage = $"{all.Count} capitoli · {countByChapter.Sum(kv => kv.Value)} voci assegnate.";
        }

        private Dictionary<int, SoaCategory> _soaById = new Dictionary<int, SoaCategory>();

        /// <summary>
        /// Ricalcola EffectiveSoaCode sull'intero albero Roots. Chiamato dopo
        /// ogni modifica di SoaCategoryId su un nodo per propagare l'eredità ai figli.
        /// </summary>
        private void RefreshEffectiveSoa()
        {
            foreach (var root in Roots)
                RefreshEffectiveSoaRecursive(root);
        }

        private void RefreshEffectiveSoaRecursive(ComputoChapterViewModel node)
        {
            var effectiveId = node.EffectiveSoaCategoryId;
            node.EffectiveSoaCode = effectiveId.HasValue && _soaById.TryGetValue(effectiveId.Value, out var s)
                ? s.Code
                : null;
            node.NotifyDisplayChanged();
            foreach (var child in node.Children)
                RefreshEffectiveSoaRecursive(child);
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

            // LOW-C1: usa TaskDialog Revit invece di MessageBox WPF per coerenza
            // visuale con il resto del plugin (dark theme Revit 2024+).
            var td = new Autodesk.Revit.UI.TaskDialog("Elimina capitolo")
            {
                MainInstruction = $"Eliminare '{SelectedNode.Model.Code} {SelectedNode.Model.Name}'?",
                MainContent = SelectedNode.TotalCount > 0
                    ? $"Il capitolo contiene {SelectedNode.TotalCount} voci che torneranno a '(senza capitolo)'. L'operazione preserva le voci ma rimuove il raggruppamento."
                    : "Il capitolo è vuoto.",
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes
                              | Autodesk.Revit.UI.TaskDialogCommonButtons.No,
                DefaultButton = Autodesk.Revit.UI.TaskDialogResult.No
            };
            if (td.Show() != Autodesk.Revit.UI.TaskDialogResult.Yes) return;

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
                ? $"{prefix}{QtoRevitPlugin.Models.ChapterCodeHelper.ToAlpha(nextIdx)}"
                : $"{prefix}{nextIdx:D2}";
        }
    }
}
