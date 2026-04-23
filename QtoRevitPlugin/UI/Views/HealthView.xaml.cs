using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Pannello Health Check (Sprint UI-8). Mostra anomalie quantità (z-score)
    /// e mismatch semantici EP (AI). L'analisi è avviata esplicitamente dall'utente
    /// via bottone "Esegui controllo" sul VM.
    /// </summary>
    public partial class HealthView : UserControl
    {
        public HealthView()
        {
            InitializeComponent();
        }
    }
}
