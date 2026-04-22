using Autodesk.Revit.UI;
using System;
using System.Windows;

namespace QtoRevitPlugin.UI.Panes
{
    /// <summary>
    /// Provider Revit per il DockablePane principale. L'istanza del UserControl viene
    /// creata da OnStartup una sola volta e riusata: questo provider fornisce solo il
    /// riferimento al pane.
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
                // Flottante di default — l'utente lo trascina dove vuole.
                // Revit ricorda la posizione tra sessioni.
                DockPosition = DockPosition.Floating
            };
            data.VisibleByDefault = true;
        }
    }
}
