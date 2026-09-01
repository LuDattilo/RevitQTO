using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Vista Verifica pre-consegna. Sostituisce lo stub PreviewView per la chiave
    /// <c>QtoViewKey.Verification</c>: esegue il preflight (Port #3) e mostra i rilievi per classe.
    /// </summary>
    public partial class VerificaView : UserControl
    {
        public VerificaView()
        {
            InitializeComponent();
        }
    }
}
