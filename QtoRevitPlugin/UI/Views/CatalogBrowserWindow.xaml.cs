using QtoRevitPlugin.UI.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Window standalone per sfogliare un listino in modalità albero gerarchico
    /// (Super &gt; Chapter &gt; SubChapter &gt; Voce). Non-modal: l'utente può lasciarla
    /// aperta mentre lavora in Revit.
    /// </summary>
    public partial class CatalogBrowserWindow : Window
    {
        private readonly CatalogBrowserViewModel _vm;

        public CatalogBrowserWindow()
        {
            _vm = new CatalogBrowserViewModel();
            DataContext = _vm;
            InitializeComponent();

            // Owner = Revit main window, così la finestra segue il minimize/close di Revit
            // e non appare come app separata nel taskbar (ShowInTaskbar="True" la tiene visibile comunque)
            var revitHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (revitHandle != System.IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitHandle;
            }
        }

        private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _vm.SelectedNode = e.NewValue as CatalogNode;
        }

        private void OnCopyCodeClick(object sender, RoutedEventArgs e)
        {
            var leaf = _vm.SelectedNode?.Leaf;
            if (leaf == null)
            {
                TaskDialog.Show("CME – Browser listino", "Seleziona una voce (foglia) prima di copiare.");
                return;
            }
            try
            {
                Clipboard.SetText(leaf.Code);
                _vm.StatusMessage = $"Copiato negli appunti: {leaf.Code}";
            }
            catch
            {
                // Clipboard può fallire se owned da altro processo
                _vm.StatusMessage = "Impossibile copiare negli appunti — riprovare.";
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
