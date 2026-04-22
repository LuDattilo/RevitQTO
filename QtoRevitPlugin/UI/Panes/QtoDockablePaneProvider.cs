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
        // Forwarding a QtoConstants (fonte unica di verità per GUID e identificativi).
        // Mantenuti per backward compat con i consumer di QtoDockablePaneProvider.{PaneGuid,PaneId,PaneTitle}.
        public static Guid PaneGuid => QtoConstants.MainPaneGuid;
        public static DockablePaneId PaneId => QtoConstants.MainPaneId;

        public const string PaneTitle = QtoConstants.MainPaneTitle;

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
