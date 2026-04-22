using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Interop;

namespace QtoRevitPlugin.UI.Views
{
    public partial class QtoMainWindow : Window
    {
        private readonly UIApplication _uiApp;

        public QtoMainWindow(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();

            // Aggancia la finestra WPF come figlia della finestra principale di Revit
            // per garantire il corretto z-order (la finestra non sparisce dietro Revit al click)
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception)
            {
                // Non critico — la finestra funziona comunque
            }
        }
    }
}
