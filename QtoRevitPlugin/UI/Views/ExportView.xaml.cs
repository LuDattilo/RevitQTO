using QtoRevitPlugin.Application;
using QtoRevitPlugin.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Step finale del workflow CME nel DockablePane: pagina di lancio
    /// che apre l'<see cref="ExportWizardWindow"/> su richiesta.
    /// La logica di esportazione vera e propria (formato, template, path)
    /// vive nel wizard, qui teniamo solo il punto d'ingresso UX.
    /// </summary>
    public partial class ExportView : UserControl
    {
        public ExportView()
        {
            InitializeComponent();
        }

        private void OnOpenWizardClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (QtoApplication.Instance?.SessionManager?.ActiveSession == null)
                {
                    TaskDialog.Show("Esporta Computo",
                        "Apri o crea una sessione (.cme) prima di esportare.");
                    return;
                }

                var window = new ExportWizardWindow();
                window.Owner = Window.GetWindow(this);
                window.Show();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("ExportView.OnOpenWizardClick", ex);
                TaskDialog.Show("Esporta Computo",
                    $"Impossibile aprire il wizard:\n{ex.Message}");
            }
        }
    }
}
