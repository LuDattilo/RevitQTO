using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class PlaceholderView : UserControl
    {
        public PlaceholderView(string title, string reference, int sprint, string description)
        {
            InitializeComponent();
            TitleText.Text = title;
            ReferenceText.Text = reference;
            ViewNameLarge.Text = title;
            SprintBadge.Text = $"SPRINT {sprint}";
            DescriptionText.Text = description;
        }
    }
}
