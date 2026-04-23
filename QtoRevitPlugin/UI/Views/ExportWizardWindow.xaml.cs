using System.Windows;

namespace QtoRevitPlugin.UI.Views
{
    public partial class ExportWizardWindow : Window
    {
        public ExportWizardWindow()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
