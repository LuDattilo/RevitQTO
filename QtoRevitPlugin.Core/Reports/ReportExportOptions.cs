using System;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Opzioni di export raccolte dall'utente nel wizard + metadati intestazione
    /// da <see cref="QtoRevitPlugin.Models.ProjectInfo"/>. Gli exporter (XPWE, PDF,
    /// Excel, CSV) leggono da qui tutto il necessario per l'output.
    /// </summary>
    public class ReportExportOptions
    {
        // Flag di export
        public bool IncludeAuditFields { get; set; }
        public bool IncludeDeletedAndSuperseded { get; set; } = false;
        public bool GroupByChapter { get; set; } = true;
        public string? CompanyLogoPath { get; set; }

        // Intestazione base
        public string Titolo { get; set; } = "";
        public string Committente { get; set; } = "";
        public string DirettoreLavori { get; set; } = "";

        // Sprint 10 (CRIT-E2): campi aggiuntivi per compatibilità XPWE / PriMus-net.
        // Popolati dall'ExportWizard leggendo ProjectInfo persistito nel .cme.
        public string Impresa { get; set; } = "";
        public string RUP { get; set; } = "";
        public DateTime? DataComputo { get; set; }
        public DateTime? DataPrezzi { get; set; }
        public string RiferimentoPrezzario { get; set; } = "";
        public string CIG { get; set; } = "";
        public string CUP { get; set; } = "";
        public decimal RibassoPercentuale { get; set; }
        public string Luogo { get; set; } = "";
        public string Comune { get; set; } = "";
        public string Provincia { get; set; } = "";
    }
}
