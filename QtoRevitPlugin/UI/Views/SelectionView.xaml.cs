using QtoRevitPlugin.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Sprint 4 Task 2 v1 (§I3): selezione elementi per categoria + ricerca nome +
    /// comandi isola/nascondi/reset vista Revit + doppio click = seleziona in Revit.
    /// Il FilterBuilder dinamico con regole parametriche composte è v2 (sprint futuro).
    /// </summary>
    public partial class SelectionView : UserControl
    {
        private readonly SelectionViewModel _vm;

        public SelectionView()
        {
            _vm = new SelectionViewModel();
            DataContext = _vm;
            InitializeComponent();
        }

        private void OnPopoutClick(object sender, RoutedEventArgs e)
            => PopoutWindow.Popout(new SelectionView(), "CME · Selezione Elementi");

        private void OnIsolateClick(object sender, RoutedEventArgs e) => _vm.IsolateCurrent();
        private void OnHideClick(object sender, RoutedEventArgs e)    => _vm.HideCurrent();
        private void OnResetClick(object sender, RoutedEventArgs e)   => _vm.ResetView();
        private void OnRefreshClick(object sender, RoutedEventArgs e) => _vm.Search();

        /// <summary>Double-click su riga → seleziona elemento in Revit (mostra nei Properties).</summary>
        private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GridElements.SelectedItem is ElementRowVm row)
                _vm.SelectInRevit(row.ElementId);
        }
    }
}
