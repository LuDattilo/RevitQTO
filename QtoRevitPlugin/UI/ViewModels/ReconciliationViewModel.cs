using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    public partial class ReconciliationViewModel : ObservableObject
    {
        private readonly IQtoRepository _repo;
        private readonly IUserContext _userContext;
        private readonly ModelDiffResult _diffResult;

        [ObservableProperty] private ObservableCollection<DiffEntryViewModel> _deletedItems = new ObservableCollection<DiffEntryViewModel>();
        [ObservableProperty] private ObservableCollection<DiffEntryViewModel> _modifiedItems = new ObservableCollection<DiffEntryViewModel>();
        [ObservableProperty] private ObservableCollection<Autodesk.Revit.DB.Element> _addedItems = new ObservableCollection<Autodesk.Revit.DB.Element>();

        public int DeletedCount => DeletedItems.Count;
        public int ModifiedCount => ModifiedItems.Count;
        public int AddedCount => AddedItems.Count;

        [ObservableProperty] private int _acceptedCount;
        [ObservableProperty] private bool _isApplying;

        public ReconciliationViewModel(ModelDiffResult diff, IQtoRepository repo, IUserContext userContext)
        {
            _diffResult = diff;
            _repo = repo;
            _userContext = userContext;

            foreach (var d in diff.Deleted)
                DeletedItems.Add(new DiffEntryViewModel(d));
            foreach (var m in diff.Modified)
                ModifiedItems.Add(new DiffEntryViewModel(m));
            foreach (var a in diff.Added)
                AddedItems.Add(a);

            HookItemAcceptedChanges(DeletedItems);
            HookItemAcceptedChanges(ModifiedItems);
            RecalculateAcceptedCount();
        }

        partial void OnDeletedItemsChanged(ObservableCollection<DiffEntryViewModel> value)
        {
            OnPropertyChanged(nameof(DeletedCount));
            RecalculateAcceptedCount();
            HookItemAcceptedChanges(value);
        }

        partial void OnModifiedItemsChanged(ObservableCollection<DiffEntryViewModel> value)
        {
            OnPropertyChanged(nameof(ModifiedCount));
            RecalculateAcceptedCount();
            HookItemAcceptedChanges(value);
        }

        partial void OnAddedItemsChanged(ObservableCollection<Autodesk.Revit.DB.Element> value)
            => OnPropertyChanged(nameof(AddedCount));

        private void HookItemAcceptedChanges(ObservableCollection<DiffEntryViewModel> items)
        {
            foreach (var item in items)
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DiffEntryViewModel.Accepted))
                        RecalculateAcceptedCount();
                };
        }

        private void RecalculateAcceptedCount()
        {
            AcceptedCount = DeletedItems.Count(d => d.Accepted) + ModifiedItems.Count(d => d.Accepted);
            ApplyBatchCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void AcceptAll()
        {
            foreach (var item in DeletedItems.ToList())
                item.AcceptCommand.Execute(null);
            foreach (var item in ModifiedItems.ToList())
                item.AcceptCommand.Execute(null);
        }

        [RelayCommand]
        private void IgnoreAll()
        {
            DeletedItems.Clear();
            ModifiedItems.Clear();
            OnPropertyChanged(nameof(DeletedCount));
            OnPropertyChanged(nameof(ModifiedCount));
        }

        private bool CanApply() => AcceptedCount > 0 && !IsApplying;

        [RelayCommand(CanExecute = nameof(CanApply))]
        private async System.Threading.Tasks.Task ApplyBatchAsync()
        {
            IsApplying = true;
            try
            {
                var ops = DeletedItems.Where(d => d.Accepted).Select(BuildDeletedOp)
                    .Concat(ModifiedItems.Where(d => d.Accepted).Select(BuildModifiedOp))
                    .ToList();

                await System.Threading.Tasks.Task.Run(() => _repo.AcceptDiffBatch(ops));

                System.Windows.MessageBox.Show(
                    $"{ops.Count} modifiche applicate.", "Riconciliazione",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                QtoRevitPlugin.Services.CrashLogger.WriteException("AcceptDiffBatch", ex);
                System.Windows.MessageBox.Show(
                    $"Errore durante l'applicazione: {ex.Message}", "Errore",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally { IsApplying = false; }
        }

        private SupersedeOp BuildDeletedOp(DiffEntryViewModel vm)
        {
            return new SupersedeOp
            {
                OldAssignmentId = vm.Entry.Snapshot.Id,
                NewVersion = new QtoAssignment { Id = vm.Entry.Snapshot.Id },  // placeholder, non usato per Deleted
                NewSnapshot = vm.Entry.Snapshot,
                Kind = SupersedeKind.Deleted,
                Log = new ChangeLogEntry
                {
                    SessionId = vm.Entry.Snapshot.SessionId,
                    ElementUniqueId = vm.Entry.Snapshot.UniqueId,
                    PriceItemCode = vm.Entry.Snapshot.AssignedEP.FirstOrDefault() ?? "",
                    ChangeType = "Deleted",
                    OldValueJson = System.Text.Json.JsonSerializer.Serialize(new { qty = vm.Entry.OldQty }),
                    UserId = _userContext.UserId,
                    Timestamp = System.DateTime.UtcNow
                }
            };
        }

        private SupersedeOp BuildModifiedOp(DiffEntryViewModel vm)
        {
            var snap = vm.Entry.Snapshot;
            return new SupersedeOp
            {
                OldAssignmentId = snap.Id,
                NewVersion = new QtoAssignment
                {
                    SessionId = snap.SessionId,
                    ElementId = snap.ElementId,
                    UniqueId = snap.UniqueId,
                    EpCode = snap.AssignedEP.FirstOrDefault() ?? "",
                    Quantity = vm.Entry.NewQty,
                    Unit = "",
                    UnitPrice = 0,
                    Source = QtoSource.RevitElement,
                    CreatedBy = _userContext.UserId,
                    CreatedAt = System.DateTime.UtcNow,
                    AuditStatus = AssignmentStatus.Active,
                    Version = 2  // il vero Version+1 dovrebbe leggere il max corrente; per ora è sufficiente
                },
                NewSnapshot = new ElementSnapshot
                {
                    SessionId = snap.SessionId,
                    ElementId = snap.ElementId,
                    UniqueId = snap.UniqueId,
                    SnapshotHash = "",  // ricalcolo in runtime — MVP
                    SnapshotQty = vm.Entry.NewQty,
                    AssignedEP = snap.AssignedEP,
                    LastUpdated = System.DateTime.UtcNow
                },
                Kind = SupersedeKind.Modified,
                Log = new ChangeLogEntry
                {
                    SessionId = snap.SessionId,
                    ElementUniqueId = snap.UniqueId,
                    PriceItemCode = snap.AssignedEP.FirstOrDefault() ?? "",
                    ChangeType = "Superseded",
                    OldValueJson = System.Text.Json.JsonSerializer.Serialize(new { qty = vm.Entry.OldQty, hash = snap.SnapshotHash }),
                    NewValueJson = System.Text.Json.JsonSerializer.Serialize(new { qty = vm.Entry.NewQty }),
                    UserId = _userContext.UserId,
                    Timestamp = System.DateTime.UtcNow
                }
            };
        }
    }

    public partial class DiffEntryViewModel : ObservableObject
    {
        public DiffEntry Entry { get; }
        public string ElementLabel => $"Elem. {Entry.Snapshot.UniqueId.Substring(0, System.Math.Min(8, Entry.Snapshot.UniqueId.Length))}\u2026";
        public string EpCode => Entry.Snapshot.AssignedEP.FirstOrDefault() ?? "";
        public string Delta => Entry.Delta;
        public string OldQtyLabel => $"{Entry.OldQty:N2}";
        public string NewQtyLabel => $"{Entry.NewQty:N2}";

        [ObservableProperty] private bool _accepted;

        public DiffEntryViewModel(DiffEntry entry) => Entry = entry;

        [RelayCommand]
        private void Accept()
        {
            Accepted = true;
        }
    }
}
