using Autodesk.Revit.UI;
using System;
using System.Windows;
using RevitRect = Autodesk.Revit.DB.Rectangle;

namespace QtoRevitPlugin.UI.Panes
{
    /// <summary>
    /// Provider Revit per il DockablePane principale — §I15 "pattern corretto":
    /// - Guid STABILE (mai rigenerarlo: Revit persiste lo stato per Guid)
    /// - Floating by default, coordinate top-right dello schermo primario
    /// - MinimumWidth/Height esplicite per evitare il fallback 150x100 di Revit
    /// - NON impostare VisibleByDefault: Revit gestisce la visibilità da solo
    ///   (persistenza in %AppData%\Autodesk\Revit\2025\UIState.dat)
    /// </summary>
    public class QtoDockablePaneProvider : IDockablePaneProvider
    {
        // Guid STABILE — mai cambiare, altrimenti Revit perde lo stato persistito utente
        public static readonly Guid PaneGuid = new("A4B2C1D0-E5F6-7890-ABCD-EF1234567891");
        public static readonly DockablePaneId PaneId = new DockablePaneId(PaneGuid);

        public const string PaneTitle = "CME · Computo";

        // Dimensione iniziale flottante
        private const int InitialWidth = 520;
        private const int InitialHeight = 760;
        private const int MarginRight = 40;
        private const int MarginTop = 80;

        private readonly FrameworkElement _pane;

        public QtoDockablePaneProvider(FrameworkElement pane)
        {
            _pane = pane ?? throw new ArgumentNullException(nameof(pane));
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _pane;

            var state = new DockablePaneState
            {
                // Multi-monitor workflow: default flottante (non Right/Tabbed)
                DockPosition = DockPosition.Floating
            };

            // FloatingRectangle e Minimum* possono essere read-only in alcune build
            // della Revit API. Tentiamo via reflection per massima compatibilità.
            TrySet(state, "FloatingRectangle", ComputeInitialFloatingRect());
            TrySet(state, "MinimumWidth", 420);
            TrySet(state, "MinimumHeight", 600);

            data.InitialState = state;
            // ❌ NON impostare data.VisibleByDefault: la visibilità è gestita da Revit
        }

        private static RevitRect ComputeInitialFloatingRect()
        {
            // System.Windows.SystemParameters è WPF-pure, no dipendenza WinForms
            var workArea = SystemParameters.WorkArea;
            int left = (int)workArea.Right - InitialWidth - MarginRight;
            int top = (int)workArea.Top + MarginTop;
            // Autodesk.Revit.DB.Rectangle usa (Left, Top, Right, Bottom)
            return new RevitRect(left, top, left + InitialWidth, top + InitialHeight);
        }

        private static void TrySet(object target, string propertyName, object value)
        {
            try
            {
                var prop = target.GetType().GetProperty(propertyName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop == null || !prop.CanWrite)
                {
                    // Fallback: prova campo backing o setter interno
                    var field = target.GetType().GetField($"<{propertyName}>k__BackingField",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    field?.SetValue(target, value);
                    return;
                }
                prop.SetValue(target, value);
            }
            catch
            {
                // Se neanche la reflection riesce, accettiamo il default di Revit
            }
        }
    }
}
