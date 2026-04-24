using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class ChapterNodesView : UserControl
    {
        public ChapterNodesView()
        {
            InitializeComponent();
        }

        private void OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ChapterNodesViewModel vm)
                vm.SelectedNode = e.NewValue as ChapterNodeVm;
        }
    }
}
