using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class CategoryNodesView : UserControl
    {
        public CategoryNodesView() { InitializeComponent(); }

        private void OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is CategoryNodesViewModel vm)
                vm.SelectedNode = e.NewValue as CategoryNodeVm;
        }
    }
}
