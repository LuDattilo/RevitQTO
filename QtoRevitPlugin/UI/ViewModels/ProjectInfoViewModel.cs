using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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

        /// <summary>
        /// Righe editoriali dei campi inline-selector (UI-6). Ciascuna row
        /// espone un dropdown sorgente Revit + TextBox valore. Le stesse
        /// proprietà string sopra (DenominazioneOpera, Committente, ecc.)
        /// fanno da backing store canonico e sono tenute sincronizzate via
        /// handler OnRowValueChanged.
        /// </summary>
        public ObservableCollection<ProjectInfoFieldRowVm> FieldRows { get; } = new();

        public ProjectInfoViewModel()
        {
            BuildFieldRows();
            LoadFromActiveSession();
        }

        /// <summary>
        /// Popola <see cref="FieldRows"/> con una row per ciascun FieldKey in
        /// <see cref="ProjectInfoFieldKeys.All"/>. Le row sono stabilmente
        /// associate al backing store via <see cref="GetFieldValue"/> /
        /// <see cref="SetFieldValue"/>, e subscribe per handlerare:
        ///   - cambio TextBox → sincronizza il backing string
        ///   - cambio dropdown → persiste il mapping + rilegge da Revit
        /// </summary>
        private void BuildFieldRows()
        {
            foreach (var key in ProjectInfoFieldKeys.All)
            {
                var row = new ProjectInfoFieldRowVm(key);
                row.Value = GetFieldValue(key);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ProjectInfoFieldRowVm.Value))
                        SetFieldValue(row.FieldKey, row.Value);
                };
                row.SourceChanged += OnRowSourceChanged;
                FieldRows.Add(row);
            }
        }

        /// <summary>
        /// Bridge row → backing string. Permette al refactor di lasciare
        /// invariate le 11 proprietà esistenti (Save le serializza).
        /// </summary>
        private string GetFieldValue(string fieldKey) => fieldKey switch
        {
            ProjectInfoFieldKeys.DenominazioneOpera   => DenominazioneOpera ?? "",
            ProjectInfoFieldKeys.Committente          => Committente ?? "",
            ProjectInfoFieldKeys.Impresa              => Impresa ?? "",
            ProjectInfoFieldKeys.Rup                  => Rup ?? "",
            ProjectInfoFieldKeys.DirettoreLavori      => DirettoreLavori ?? "",
            ProjectInfoFieldKeys.Luogo                => Luogo ?? "",
            ProjectInfoFieldKeys.Comune               => Comune ?? "",
            ProjectInfoFieldKeys.Provincia            => Provincia ?? "",
            ProjectInfoFieldKeys.Cig                  => Cig ?? "",
            ProjectInfoFieldKeys.Cup                  => Cup ?? "",
            ProjectInfoFieldKeys.RiferimentoPrezzario => RiferimentoPrezzario ?? "",
            _ => string.Empty
        };

        private void SetFieldValue(string fieldKey, string value)
        {
            switch (fieldKey)
            {
                case ProjectInfoFieldKeys.DenominazioneOpera: DenominazioneOpera = value; break;
                case ProjectInfoFieldKeys.Committente: Committente = value; break;
                case ProjectInfoFieldKeys.Impresa: Impresa = value; break;
                case ProjectInfoFieldKeys.Rup: Rup = value; break;
                case ProjectInfoFieldKeys.DirettoreLavori: DirettoreLavori = value; break;
                case ProjectInfoFieldKeys.Luogo: Luogo = value; break;
                case ProjectInfoFieldKeys.Comune: Comune = value; break;
                case ProjectInfoFieldKeys.Provincia: Provincia = value; break;
                case ProjectInfoFieldKeys.Cig: Cig = value; break;
                case ProjectInfoFieldKeys.Cup: Cup = value; break;
                case ProjectInfoFieldKeys.RiferimentoPrezzario: RiferimentoPrezzario = value; break;
            }
        }

        /// <summary>
        /// Handler cambio dropdown "sorgente" sulla row.
        /// - Manual → cancella il mapping persistito (se esiste)
        /// - Param → upsert mapping + rilegge valore da Revit (se vuoto)
        /// - AddShared → delegato all'evento <see cref="AddSharedParameterRequested"/>:
        ///   la View mostra il dialog e, su successo, reimposta la SelectedSource.
        /// </summary>
        private void OnRowSourceChanged(object? sender, ParamSourceOption? option)
        {
            if (sender is not ProjectInfoFieldRowVm row || option == null) return;
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            if (repo == null || _sessionId == 0) return;

            switch (option.Kind)
            {
                case ParamSourceOption.SourceKind.AddShared:
                    // La vera creazione avviene dal code-behind (ha accesso a UI thread
                    // + Window owner). Il VM solleva l'evento e aspetta che il chiamante
                    // reimposti SelectedSource con il nuovo ParamEntry post-creazione.
                    AddSharedParameterRequested?.Invoke(this, row);
                    break;

                case ParamSourceOption.SourceKind.Manual:
                    try { repo.DeleteRevitParamMapping(_sessionId, row.FieldKey); }
                    catch (Exception ex)
                    {
                        CrashLogger.WriteException("ProjectInfoViewModel.DeleteMapping", ex);
                    }
                    break;

                case ParamSourceOption.SourceKind.Param:
                    try
                    {
                        repo.UpsertRevitParamMapping(new RevitParamMapping
                        {
                            SessionId = _sessionId,
                            FieldKey = row.FieldKey,
                            ParamName = option.ParamName,
                            IsBuiltIn = option.IsBuiltIn,
                            SkipIfFilled = true,
                        });
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.WriteException("ProjectInfoViewModel.UpsertMapping", ex);
                    }

                    // Popola il TextBox con il valore corrente Revit se il campo è vuoto
                    if (string.IsNullOrWhiteSpace(row.Value) && !string.IsNullOrWhiteSpace(option.CurrentValue))
                        row.Value = option.CurrentValue!;
                    break;
            }
        }

        /// <summary>
        /// Evento sollevato quando l'utente seleziona "+ Aggiungi parametro condiviso…"
        /// in un dropdown. Il code-behind della View lo gestisce aprendo
        /// <c>AddSharedParameterDialog</c>. Dopo creazione successo, il chiamante
        /// deve invocare <see cref="RefreshParamSources"/> e impostare la nuova
        /// SelectedSource sulla row argomento.
        /// </summary>
        public event EventHandler<ProjectInfoFieldRowVm>? AddSharedParameterRequested;

        /// <summary>
        /// Ri-popola <see cref="ProjectInfoFieldRowVm.ParamSources"/> per ogni row:
        /// Manual + parametri enumerati dal documento Revit attivo + AddShared.
        /// Chiamato al load della sessione e dopo la creazione di un SP.
        /// </summary>
        public void RefreshParamSources()
        {
            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            IReadOnlyList<RevitParamEnumeratorService.ParamEntry> entries =
                doc != null
                    ? RevitParamEnumeratorService.GetAllParams(doc)
                    : Array.Empty<RevitParamEnumeratorService.ParamEntry>();

            // Snapshot mapping corrente (FieldKey → ParamName) per ripristinare
            // la selezione dopo il rebuild.
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var mappingByKey = new Dictionary<string, RevitParamMapping>(StringComparer.Ordinal);
            if (repo != null && _sessionId > 0)
            {
                foreach (var m in repo.GetRevitParamMappings(_sessionId))
                    mappingByKey[m.FieldKey] = m;
            }

            foreach (var row in FieldRows)
            {
                row.ParamSources.Clear();
                row.ParamSources.Add(ParamSourceOption.Manual());
                foreach (var e in entries)
                    row.ParamSources.Add(ParamSourceOption.Param(
                        paramName: e.ParamName,
                        displayName: e.DisplayName,
                        isBuiltIn: e.IsBuiltIn,
                        currentValue: e.CurrentValue));
                row.ParamSources.Add(ParamSourceOption.AddShared());

                // Ripristina selezione corrente da mapping salvato (senza
                // scatenare OnRowSourceChanged → usiamo un flag).
                ParamSourceOption selected = row.ParamSources[0]; // Manual
                if (mappingByKey.TryGetValue(row.FieldKey, out var m) && !string.IsNullOrEmpty(m.ParamName))
                {
                    foreach (var opt in row.ParamSources)
                    {
                        if (opt.Kind == ParamSourceOption.SourceKind.Param &&
                            string.Equals(opt.ParamName, m.ParamName, StringComparison.Ordinal))
                        {
                            selected = opt;
                            break;
                        }
                    }
                }

                // Imposta senza side-effect: assegno il campo privato via
                // partial handler bypass (ComboBox rispetta il binding immediato).
                // Accettiamo il side-effect se la row è "Manual": equivale a no-op
                // nel repo (DeleteRevitParamMapping su FieldKey vuoto è idempotente).
                row.SelectedSource = selected;
            }
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

            // Sincronizza le row con il nuovo backing store
            foreach (var row in FieldRows)
                row.Value = GetFieldValue(row.FieldKey);

            // Popola dropdown sorgente + ripristina mapping salvato
            RefreshParamSources();
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

        // Sync backing string → row quando un consumer esterno (es. OnImportFromRevitClick)
        // scrive direttamente sulle observable properties. Previene che la row resti
        // "indietro" rispetto al TextBox visibile.
        partial void OnDenominazioneOperaChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.DenominazioneOpera, value);
        partial void OnCommittenteChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.Committente, value);
        partial void OnImpresaChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.Impresa, value);
        partial void OnRupChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.Rup, value);
        partial void OnDirettoreLavoriChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.DirettoreLavori, value);
        partial void OnLuogoChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.Luogo, value);
        partial void OnComuneChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.Comune, value);
        partial void OnProvinciaChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.Provincia, value);
        partial void OnCigChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.Cig, value);
        partial void OnCupChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.Cup, value);
        partial void OnRiferimentoPrezzarioChanged(string value) => SyncRowValue(ProjectInfoFieldKeys.RiferimentoPrezzario, value);

        private void SyncRowValue(string fieldKey, string value)
        {
            foreach (var row in FieldRows)
            {
                if (row.FieldKey == fieldKey && row.Value != value)
                {
                    row.Value = value ?? string.Empty;
                    break;
                }
            }
        }
    }
}
