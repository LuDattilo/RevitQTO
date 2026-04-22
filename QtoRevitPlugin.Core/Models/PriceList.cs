using System;

namespace QtoRevitPlugin.Models
{
    public class PriceList
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        /// <summary>Priorità per la risoluzione dei conflitti di codice tra listini multipli (0 = più alta).</summary>
        public int Priority { get; set; }
        public DateTime ImportedAt { get; set; }
        public int RowCount { get; set; }
    }
}
