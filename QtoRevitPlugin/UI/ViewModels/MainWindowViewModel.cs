using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QtoRevitPlugin.UI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _sessionTitle = "Nessuna sessione attiva";

        [ObservableProperty]
        private string _statusMessage = "Pronto";

        [ObservableProperty]
        private int _totalElements;

        [ObservableProperty]
        private int _taggedElements;

        [ObservableProperty]
        private double _totalAmount;

        public double TaggedPercent => TotalElements > 0
            ? (double)TaggedElements / TotalElements * 100.0
            : 0.0;

        public string ProgressText => $"{TaggedElements}/{TotalElements} ({TaggedPercent:F1}%)";

        partial void OnTaggedElementsChanged(int value) => OnPropertyChanged(nameof(ProgressText));
        partial void OnTotalElementsChanged(int value) => OnPropertyChanged(nameof(ProgressText));

        [RelayCommand]
        private void NewSession()
        {
            StatusMessage = "Nuova sessione — funzionalità disponibile allo Sprint 1";
        }
    }
}
