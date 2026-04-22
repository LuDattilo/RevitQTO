using Autodesk.Revit.UI;
using System;
using System.Windows;

namespace QtoRevitPlugin.UI.Panes
{
    /// <summary>
    /// Provider Revit per il DockablePane principale.
    /// Creazione eager del UserControl in OnStartup (main thread Revit, contesto pronto);
    /// VisibleByDefault=false evita il costo di layout iniziale se l'utente non apre il pane.
    /// </summary>
    public class QtoDockablePaneProvider : IDockablePaneProvider
    {
        public static readonly Guid PaneGuid = new("C5F2A4E1-8D3B-4A91-9B72-1F6C3E8D2A55");
        public static readonly DockablePaneId PaneId = new DockablePaneId(PaneGuid);

        private readonly FrameworkElement _pane;

        public QtoDockablePaneProvider(FrameworkElement pane)
        {
            _pane = pane ?? throw new ArgumentNullException(nameof(pane));
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _pane;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Floating
            };
            data.VisibleByDefault = false;
        }
    }
}
