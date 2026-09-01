using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Computo;
using QtoRevitPlugin.Services.Computi;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// ViewModel del Quadro Economico: sintesi monetaria del computo tramite i motori portati da Pulse
    /// (<see cref="ComputoAnalysisService"/>). Mostra i 4 livelli (Diretto → Maggiorato → Imponibile →
    /// +IVA), la quota CAM e l'incidenza manodopera (art. 41 D.Lgs 36/2023). Maggiorazione e IVA sono
    /// editabili; l'importo è ricalcolato dal motore unico. Le omissioni (prezzo/IVA mancante) sono
    /// dichiarate, mai nascoste (disciplina H7).
    /// </summary>
    public partial class QuadroEconomicoViewModel : ViewModelBase
    {
        // Formato monetario italiano (virgola decimale, punto migliaia) costruito a mano per evitare
        // dipendenze dal culture del SO.
        private static readonly NumberFormatInfo Nfi = new NumberFormatInfo
        {
            NumberDecimalSeparator = ",",
            NumberGroupSeparator = ".",
            NumberDecimalDigits = 2,
        };

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _hasReport;

        [ObservableProperty] private double? _markupPercent;
        [ObservableProperty] private double? _defaultVatPercent = 22.0;

        // Totali (etichette formattate)
        [ObservableProperty] private string _directCostLabel = "—";
        [ObservableProperty] private string _markupLabel = "—";
        [ObservableProperty] private bool _showMarkupLine;
        [ObservableProperty] private string _taxableLabel = "—";
        [ObservableProperty] private string _vatLabel = "—";
        [ObservableProperty] private bool _vatComputable;
        [ObservableProperty] private string _grandTotalLabel = "—";

        // CAM
        [ObservableProperty] private string _camImportoLabel = "—";
        [ObservableProperty] private string _quotaCamLabel = "—";
        [ObservableProperty] private string _camUnclassifiedLabel = "—";

        // Manodopera
        [ObservableProperty] private string _manodoperaImportoLabel = "—";
        [ObservableProperty] private string _manodoperaPercentLabel = "—";
        [ObservableProperty] private string _manodoperaCoverageLabel = "—";

        // Avvisi qualità dati
        [ObservableProperty] private string _warnings = "";
        [ObservableProperty] private bool _hasWarnings;

        [ObservableProperty] private string _statusMessage =
            "Premi «Calcola» per il quadro economico del computo.";

        [RelayCommand]
        private void Run()
        {
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || session == null)
            {
                StatusMessage = "Nessun computo aperto. Apri o crea un file .cme dalla Home.";
                return;
            }

            IsRunning = true;
            HasReport = false;
            try
            {
                var svc = new ComputoAnalysisService(repo);
                var totals = svc.GetTotals(session.Id, MarkupPercent, DefaultVatPercent);
                var cam = svc.GetCam(session.Id);
                var mdo = svc.GetManodopera(session.Id, "codice");

                DirectCostLabel = Money(totals.DirectCostTotal);
                ShowMarkupLine = totals.ShowMarkupLine;
                MarkupLabel = totals.ShowMarkupLine
                    ? $"+ {Money(totals.MarkupAmount)}  ({Pct(totals.MarkupPercentApplied)})"
                    : "— (nessuna maggiorazione)";
                TaxableLabel = Money(totals.TaxableTotal);
                VatComputable = totals.VatComputable;
                VatLabel = totals.VatComputable
                    ? $"+ {Money(totals.VatAmount)}  ({Pct(DefaultVatPercent)})"
                    : "non calcolabile (IVA non impostata)";
                GrandTotalLabel = totals.VatComputable ? Money(totals.GrandTotalWithVat) : "—";

                CamImportoLabel = Money(cam.CamImporto);
                QuotaCamLabel = cam.QuotaCamSuTotale.HasValue
                    ? (cam.QuotaCamSuTotale.Value * 100.0).ToString("N1", Nfi) + " %"
                    : "non calcolabile";
                CamUnclassifiedLabel = cam.UnclassifiedItemCount > 0
                    ? $"{Money(cam.UnclassifiedImporto)} · {cam.UnclassifiedItemCount} voce/i non classificabili"
                    : "tutte le voci classificate";

                ManodoperaImportoLabel = Money(mdo.Totals.IncidenzaManodoperaTotale);
                ManodoperaPercentLabel = mdo.Totals.IncidenzaManodoperaPercentSulTotale.HasValue
                    ? mdo.Totals.IncidenzaManodoperaPercentSulTotale.Value.ToString("N1", Nfi) + " %"
                    : "non calcolabile";
                ManodoperaCoverageLabel =
                    $"{mdo.Coverage.VociConMdoNota}/{mdo.Coverage.VociTotali} voci con IncMDO dichiarata";

                BuildWarnings(totals, cam);

                HasReport = true;
                StatusMessage = "Quadro economico aggiornato.";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void BuildWarnings(ComputoTotalsResult totals, CamQuotaResult cam)
        {
            var parts = new List<string>();
            if (!totals.UnitPriceComputable)
                parts.Add($"{totals.RowsMissingUnitPriceTotalCount} voce/i senza prezzo unitario risolto: importo incompleto.");
            if (!totals.VatComputable)
                parts.Add("IVA non impostata su alcune voci: Livello 4 non pronto (Livelli 1-3 validi).");
            if (cam.OrphanMeasureCodes.Count > 0)
                parts.Add($"{cam.OrphanMeasureCodes.Count} voce/i con prezzo orfano escluse dai totali CAM.");

            Warnings = string.Join("\n", parts);
            HasWarnings = parts.Count > 0;
        }

        private static string Money(double v) => "€ " + v.ToString("N2", Nfi);

        private static string Pct(double? v) => v.HasValue ? v.Value.ToString("N1", Nfi) + " %" : "0 %";
    }
}
