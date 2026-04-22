using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace QtoRevitPlugin.UI.Views
{
    public partial class PreviewView : UserControl
    {
        public PreviewView()
        {
            InitializeComponent();
        }

        private void OnTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked) return;

            // Mutual exclusion tra i due tab
            TabSelection.IsChecked = ReferenceEquals(clicked, TabSelection);
            TabSummary.IsChecked = ReferenceEquals(clicked, TabSummary);

            PanelSelection.Visibility = TabSelection.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelSummary.Visibility = TabSummary.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
