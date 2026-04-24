using QtoRevitPlugin.UI.ViewModels;
using System.Windows;
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

        /// <summary>Apre la Redazione CME in finestra separata (workflow multi-monitor).</summary>
        private void OnPopoutClick(object sender, RoutedEventArgs e)
            => PopoutWindow.Popout(new CmeEditorView(), "CME · Redazione CME");
    }
}
