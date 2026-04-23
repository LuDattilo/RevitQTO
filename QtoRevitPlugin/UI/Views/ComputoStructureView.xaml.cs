using QtoRevitPlugin.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class ComputoStructureView : UserControl
    {
        public ComputoStructureView()
        {
            InitializeComponent();
        }

        private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ComputoStructureViewModel vm)
                vm.SelectedNode = e.NewValue as ComputoChapterViewModel;
        }
    }
}
