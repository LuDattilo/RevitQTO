using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class WbsNodesView : UserControl
    {
        public WbsNodesView() { InitializeComponent(); }

        private void OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is WbsNodesViewModel vm)
                vm.SelectedNode = e.NewValue as WbsNodeVm;
        }
    }
}
