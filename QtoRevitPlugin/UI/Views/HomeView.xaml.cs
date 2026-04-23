using System;
using System.Windows;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class HomeView : UserControl
    {
        public event EventHandler? NewSessionRequested;
        public event EventHandler? OpenSessionRequested;
        public event EventHandler? ResumeLastSessionRequested;

        public HomeView()
        {
            InitializeComponent();
        }

        private void OnNewSessionClick(object sender, RoutedEventArgs e)
        {
            NewSessionRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnOpenSessionClick(object sender, RoutedEventArgs e)
        {
            OpenSessionRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnResumeLastClick(object sender, RoutedEventArgs e)
        {
            ResumeLastSessionRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
