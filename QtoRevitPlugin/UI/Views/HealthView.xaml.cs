using System.Windows.Controls;
using System.Windows.Input;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.ViewModels;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Pannello Health Check (Sprint UI-8). Mostra anomalie quantità (z-score)
    /// e mismatch semantici EP (AI). UI-10: doppio click su riga → seleziona
    /// l'elemento in Revit via <see cref="RevitNavigationHelper"/>.
    /// </summary>
    public partial class HealthView : UserControl
    {
        public HealthView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handler condiviso per doppio click su righe AnomaliesGrid e
        /// MismatchesGrid. Entrambe le row VM espongono la property
        /// <c>UniqueId</c>; risolviamo via reflection leggera (no interface
        /// condivisa per mantenere il VM semplice).
        /// </summary>
        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            var row = grid.SelectedItem;
            if (row == null) return;

            var uniqueId = row switch
            {
                AnomalyRowVm a => a.UniqueId,
                MismatchRowVm m => m.UniqueId,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(uniqueId)) return;

            var result = RevitNavigationHelper.SelectByUniqueId(uniqueId!);
            var message = RevitNavigationHelper.DescribeResult(result, uniqueId!);

            if (DataContext is HealthViewModel vm)
                vm.StatusMessage = message;
        }
    }
}
