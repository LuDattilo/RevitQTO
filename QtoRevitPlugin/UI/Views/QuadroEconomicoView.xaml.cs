using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Vista Quadro Economico: sintesi monetaria del computo (4 livelli + CAM + incidenza manodopera)
    /// tramite i motori portati da Pulse, esposti da <c>ComputoAnalysisService</c>.
    /// </summary>
    public partial class QuadroEconomicoView : UserControl
    {
        public QuadroEconomicoView()
        {
            InitializeComponent();
        }
    }
}
