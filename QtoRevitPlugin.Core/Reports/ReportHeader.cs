using System;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Intestazione del report. Popolata dal <see cref="ReportDataSetBuilder"/>
    /// leggendo le <see cref="ReportExportOptions"/> passate al build. Gli exporter
    /// (XPWE/PDF/Excel) la usano per generare l'header del documento.
    /// </summary>
    public class ReportHeader
    {
        // Base
        public string Titolo { get; set; } = "";
        public string Committente { get; set; } = "";
        public string DirettoreLavori { get; set; } = "";
        public DateTime DataCreazione { get; set; }

        // Sprint 10 (CRIT-E2): campi aggiuntivi per compatibilità XPWE / PriMus-net
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
