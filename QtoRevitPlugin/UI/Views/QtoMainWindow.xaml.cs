using Autodesk.Revit.UI;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.ViewModels;
using System;
using System.Windows;
using System.Windows.Interop;

namespace QtoRevitPlugin.UI.Views
{
    public partial class QtoMainWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly SessionManager _sessionManager;
        private readonly MainWindowViewModel _vm;

        public QtoMainWindow(UIApplication uiApp, SessionManager sessionManager)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            InitializeComponent();

            _vm = (MainWindowViewModel)DataContext;
            RefreshFromSession();
            _sessionManager.SessionChanged += (_, _) => Dispatcher.Invoke(RefreshFromSession);

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch
            {
                // Non critico
            }
        }

        private void RefreshFromSession()
        {
            var session = _sessionManager.ActiveSession;
            if (session == null)
            {
                _vm.SessionTitle = "Nessuna sessione attiva";
                _vm.TotalElements = 0;
                _vm.TaggedElements = 0;
                _vm.TotalAmount = 0;
                _vm.StatusMessage = "Pronto";
                return;
            }

            _vm.SessionTitle = $"{session.ProjectName} · {session.SessionName}";
            _vm.TotalElements = session.TotalElements;
            _vm.TaggedElements = session.TaggedElements;
            _vm.TotalAmount = session.TotalAmount;
            _vm.StatusMessage = session.LastSavedAt.HasValue
                ? $"Salvato {session.LastSavedAt.Value.ToLocalTime():HH:mm}"
                : "Sessione creata";
        }
    }
}
