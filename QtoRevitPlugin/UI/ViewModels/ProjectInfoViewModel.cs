using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// ViewModel per la sezione "Informazioni Progetto" della SetupView.
    /// Bind bidirezionale a <see cref="ProjectInfo"/> persistito nel .cme via
    /// <see cref="IQtoRepository.UpsertProjectInfo"/>. Auto-load all'istanziazione
    /// leggendo la sessione attiva da <see cref="QtoApplication.Instance"/>.
    /// Il Save è esplicito (RelayCommand) — no auto-save ad ogni keystroke
    /// per evitare churn su file .cme.
    /// </summary>
    public partial class ProjectInfoViewModel : ViewModelBase
    {
        private int _sessionId;
        private int _projectInfoId;  // 0 = insert, >0 = update

        [ObservableProperty] private string _denominazioneOpera = "";
        [ObservableProperty] private string _committente = "";
        [ObservableProperty] private string _impresa = "";
        [ObservableProperty] private string _rup = "";
        [ObservableProperty] private string _direttoreLavori = "";
        [ObservableProperty] private string _luogo = "";
        [ObservableProperty] private string _comune = "";
        [ObservableProperty] private string _provincia = "";
        [ObservableProperty] private DateTime? _dataComputo;
        [ObservableProperty] private DateTime? _dataPrezzi;
        [ObservableProperty] private string _riferimentoPrezzario = "";
        [ObservableProperty] private string _cig = "";
        [ObservableProperty] private string _cup = "";
        [ObservableProperty] private decimal _ribassoPercentuale;
        [ObservableProperty] private string _logoPath = "";

        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private bool _canSave;

        public ProjectInfoViewModel()
        {
            LoadFromActiveSession();
        }

        /// <summary>
        /// Carica ProjectInfo dalla sessione attiva. Chiamabile anche esternamente
        /// per refresh dopo open/close file .cme.
        /// </summary>
        public void LoadFromActiveSession()
        {
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || session == null)
            {
                StatusMessage = "Nessun computo aperto — le informazioni sono disabilitate.";
                CanSave = false;
                _sessionId = 0;
                return;
            }

            _sessionId = session.Id;
            var info = repo.GetProjectInfo(_sessionId);
            if (info == null)
            {
                // Nuovo: pre-popola con quello che sappiamo
                _projectInfoId = 0;
                DenominazioneOpera = session.ProjectName ?? "";
                Committente = "";
                Impresa = "";
                Rup = "";
                DirettoreLavori = "";
                Luogo = "";
                Comune = "";
                Provincia = "";
                DataComputo = DateTime.Today;
                DataPrezzi = null;
                RiferimentoPrezzario = "";
                Cig = "";
                Cup = "";
                RibassoPercentuale = 0;
                LogoPath = "";
                StatusMessage = "Nuova scheda progetto — compila e salva.";
            }
            else
            {
                _projectInfoId = info.Id;
                DenominazioneOpera = info.DenominazioneOpera;
                Committente = info.Committente;
                Impresa = info.Impresa;
                Rup = info.RUP;
                DirettoreLavori = info.DirettoreLavori;
                Luogo = info.Luogo;
                Comune = info.Comune;
                Provincia = info.Provincia;
                DataComputo = info.DataComputo;
                DataPrezzi = info.DataPrezzi;
                RiferimentoPrezzario = info.RiferimentoPrezzario;
                Cig = info.CIG;
                Cup = info.CUP;
                RibassoPercentuale = info.RibassoPercentuale;
                LogoPath = info.LogoPath;
                StatusMessage = $"Ultimo salvataggio: {info.UpdatedAt.ToLocalTime():dd/MM/yyyy HH:mm}";
            }
            CanSave = true;
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void Save()
        {
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            if (repo == null || _sessionId == 0)
            {
                StatusMessage = "Nessun computo aperto.";
                return;
            }

            var info = new ProjectInfo
            {
                Id = _projectInfoId,
                SessionId = _sessionId,
                DenominazioneOpera = DenominazioneOpera ?? "",
                Committente = Committente ?? "",
                Impresa = Impresa ?? "",
                RUP = Rup ?? "",
                DirettoreLavori = DirettoreLavori ?? "",
                Luogo = Luogo ?? "",
                Comune = Comune ?? "",
                Provincia = Provincia ?? "",
                DataComputo = DataComputo,
                DataPrezzi = DataPrezzi,
                RiferimentoPrezzario = RiferimentoPrezzario ?? "",
                CIG = Cig ?? "",
                CUP = Cup ?? "",
                RibassoPercentuale = RibassoPercentuale,
                LogoPath = LogoPath ?? ""
            };

            try
            {
                repo.UpsertProjectInfo(info);
                StatusMessage = $"Salvato: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                QtoRevitPlugin.Services.CrashLogger.WriteException("ProjectInfoViewModel.Save", ex);
                StatusMessage = $"Errore salvataggio: {ex.Message}";
            }
        }

        partial void OnCanSaveChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
    }
}
