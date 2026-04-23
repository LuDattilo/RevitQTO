using QtoRevitPlugin.Models;
using System.Windows;

namespace QtoRevitPlugin.UI.Views
{
    public partial class ChapterEditorPopup : Window
    {
        private readonly ComputoChapter _model;

        public ChapterEditorPopup(ComputoChapter model)
        {
            InitializeComponent();
            _model = model;
            CodeBox.Text = model.Code;
            NameBox.Text = model.Name;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CodeBox.Text) || string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Codice e Nome sono obbligatori.", "Validazione",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _model.Code = CodeBox.Text.Trim();
            _model.Name = NameBox.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
