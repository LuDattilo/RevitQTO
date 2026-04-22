using QtoRevitPlugin.Models;
using QtoRevitPlugin.UI.ViewModels;
using System.Collections.Generic;
using System.Windows;

namespace QtoRevitPlugin.UI.Views
{
    public enum SessionDialogResult
    {
        Cancel,
        NewSession,
        Resume
    }

    public partial class SessionListWindow : Window
    {
        private readonly SessionListViewModel _vm;

        public SessionListWindow(string projectName, IEnumerable<WorkSession> sessions)
        {
            InitializeComponent();
            _vm = (SessionListViewModel)DataContext;
            _vm.Load(projectName, sessions);

            NewSessionButton.Click += (_, _) =>
            {
                Result = SessionDialogResult.NewSession;
                DialogResult = true;
            };
            ResumeButton.Click += (_, _) =>
            {
                if (_vm.SelectedSession == null) return;
                Result = SessionDialogResult.Resume;
                SelectedSessionId = _vm.SelectedSession.Id;
                DialogResult = true;
            };
            CancelButton.Click += (_, _) =>
            {
                Result = SessionDialogResult.Cancel;
                DialogResult = false;
            };
        }

        public SessionDialogResult Result { get; private set; } = SessionDialogResult.Cancel;
        public int SelectedSessionId { get; private set; }
    }
}
