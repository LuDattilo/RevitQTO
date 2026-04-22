using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Window generica per estrarre una scheda del DockablePane in finestra separata.
    /// Supporta minimize/maximize/close standard WPF + Revit MainWindow come owner.
    ///
    /// Pattern uso:
    /// <code>
    /// PopoutWindow.Popout(new SetupView(), "CME · Setup");
    /// </code>
    ///
    /// Il ViewModel della UserControl popped-out è indipendente da quello del pane.
    /// Entrambi accedono alla stessa UserLibrary/DB quindi i dati restano sincronizzati
    /// senza bisogno di meccanismi custom.
    /// </summary>
    public partial class PopoutWindow : Window
    {
        public PopoutWindow(UserControl content, string title)
        {
            InitializeComponent();

            Title = title;
            TitleLabel.Text = title;
            ContentHost.Content = content;

            // Owner = Revit main window (segue minimize/close di Revit, non crea app separata in Alt+Tab)
            var revitHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (revitHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitHandle;
            }
        }

        /// <summary>
        /// Shortcut per aprire una scheda in una nuova PopoutWindow non-modale.
        /// Tipicamente chiamato dall'header della scheda al click del bottone ⤢.
        /// </summary>
        public static PopoutWindow Popout(UserControl content, string title)
        {
            var win = new PopoutWindow(content, title);
            win.Show();
            return win;
        }
    }
}
