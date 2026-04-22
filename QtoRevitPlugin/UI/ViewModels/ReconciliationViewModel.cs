using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Services;
using System.Collections.ObjectModel;
using System.Linq;
using ModelDiffResult = QtoRevitPlugin.Services.ModelDiffResult;

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
        }
    }

    public partial class DiffEntryViewModel : ObservableObject
    {
        public DiffEntry Entry { get; }
        public string ElementLabel => $"Elem. {Entry.Snapshot.UniqueId.Substring(0, 8)}\u2026";
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
