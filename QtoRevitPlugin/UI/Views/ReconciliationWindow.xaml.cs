using QtoRevitPlugin.UI.ViewModels;
using System.Windows;

namespace QtoRevitPlugin.UI.Views
{
    public partial class ReconciliationWindow : Window
    {
        public ReconciliationWindow(ReconciliationViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
