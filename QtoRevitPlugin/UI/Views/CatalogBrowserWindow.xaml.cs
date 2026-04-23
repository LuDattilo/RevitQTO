using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Window "Sfoglia Listino" — embed della SetupListinoView del pannello principale
    /// (UX consistente, VM condiviso).
    ///
    /// Non-modal: l'utente può lasciarla aperta mentre lavora in Revit. Su Close,
    /// nascondiamo invece di distruggere così lo stato preferiti/ricerca rimane
    /// se l'utente riapre la finestra (es. dalla scheda Listino "Sfoglia listino…").
    /// </summary>
    public partial class CatalogBrowserWindow : Window
    {
        public CatalogBrowserWindow()
        {
            InitializeComponent();

            // Owner = Revit main window → minimize/close di Revit trascina via anche la finestra
            var revitHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (revitHandle != System.IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitHandle;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
