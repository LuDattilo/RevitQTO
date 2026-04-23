using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Container del tab "Setup" nel DockablePane. Ospita 4 sub-tab dedicati:
    /// Informazioni Progetto, Listino, Struttura Computo, Nuovi Prezzi.
    /// Ogni sub-tab è una UserControl indipendente con proprio ViewModel.
    /// </summary>
    public partial class SetupView : UserControl
    {
        public SetupView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Ctor con selezione iniziale tab (usato da navigazione Home → Setup · Listino).
        /// </summary>
        public SetupView(int initialTabIndex)
        {
            InitializeComponent();
            if (initialTabIndex >= 0 && initialTabIndex < SetupTabs.Items.Count)
                SetupTabs.SelectedIndex = initialTabIndex;
        }
    }
}
