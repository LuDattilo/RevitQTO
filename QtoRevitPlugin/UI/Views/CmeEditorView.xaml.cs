using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class CmeEditorView : UserControl
    {
        public CmeEditorView()
        {
            InitializeComponent();
        }

        private void OnNavSelectedChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is CmeEditorViewModel vm)
                vm.SelectedNavNode = e.NewValue as CmeNavNode;
        }
    }
}
