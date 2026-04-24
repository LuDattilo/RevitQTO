namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Template di personalizzazione export (Excel/PDF). Serializzato in JSON
    /// nella cartella %AppData%\QtoPlugin\Templates\. Se nessun template è
    /// scelto, l'exporter usa il proprio default hardcoded (back-compat pre-Sprint10).
    /// </summary>
    public class ExportTemplate
    {
        public string Name { get; set; } = "Standard";
        public string DisplayName { get; set; } = "Standard";
        public string Format { get; set; } = "All";
        public string LogoPath { get; set; } = "";
        public string HeaderColorHex { get; set; } = "#1E6FD9";
        public string HeaderTextColorHex { get; set; } = "#FFFFFF";
        public string SubtotalColorHex { get; set; } = "#F0F0F0";
        public string Footer { get; set; } = "";
        public bool IncludeSubtotalRows { get; set; } = true;
        public string NumberFormat { get; set; } = "#,##0.00";
        public string CurrencyFormat { get; set; } = "#,##0.00 €";
    }
}
