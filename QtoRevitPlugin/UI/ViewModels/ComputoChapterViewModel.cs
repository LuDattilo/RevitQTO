using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Nodo del TreeView Struttura Computo: rappresenta un ComputoChapter con
    /// figli annidati e assegnazioni dirette (non ricorsive).
    /// </summary>
    public partial class ComputoChapterViewModel : ObservableObject
    {
        public ComputoChapter Model { get; }
        public ObservableCollection<ComputoChapterViewModel> Children { get; } = new ObservableCollection<ComputoChapterViewModel>();
        public int DirectAssignmentsCount { get; set; }

        [ObservableProperty] private bool _isExpanded = true;
        [ObservableProperty] private bool _isSelected;

        public ComputoChapterViewModel(ComputoChapter model) => Model = model;

        public string DisplayLabel => $"{Model.Code}  {Model.Name}  ({TotalCount} voci)";
        public int TotalCount => DirectAssignmentsCount + Children.Sum(c => c.TotalCount);
    }
}
