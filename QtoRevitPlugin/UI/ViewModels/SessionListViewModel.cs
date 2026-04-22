using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace QtoRevitPlugin.UI.ViewModels
{
    public partial class SessionListViewModel : ViewModelBase
    {
        public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();

        [ObservableProperty]
        private SessionRowViewModel? _selectedSession;

        [ObservableProperty]
        private string _projectName = string.Empty;

        public bool HasSessions => Sessions.Count > 0;

        public void Load(string projectName, IEnumerable<WorkSession> sessions)
        {
            ProjectName = projectName;
            Sessions.Clear();
            foreach (var s in sessions)
            {
                Sessions.Add(new SessionRowViewModel(s));
            }
            OnPropertyChanged(nameof(HasSessions));
            SelectedSession = Sessions.Count > 0 ? Sessions[0] : null;
        }
    }

    public class SessionRowViewModel
    {
        public WorkSession Session { get; }

        public SessionRowViewModel(WorkSession session)
        {
            Session = session;
        }

        public int Id => Session.Id;
        public string Name => string.IsNullOrWhiteSpace(Session.SessionName) ? "(senza nome)" : Session.SessionName;
        public string Status => Session.Status.ToString();
        public int TotalElements => Session.TotalElements;
        public int TaggedElements => Session.TaggedElements;
        public double TaggedPercent => Session.TaggedPercent;
        public string Progress => $"{TaggedElements}/{TotalElements} ({TaggedPercent:F1}%)";
        public string Amount => $"€ {Session.TotalAmount:N2}";
        public string LastEpCode => string.IsNullOrEmpty(Session.LastEpCode) ? "—" : Session.LastEpCode;
        public string LastSaved => Session.LastSavedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";
        public bool IsCompleted => Session.Status == SessionStatus.Completed;
    }
}
