using System;

namespace QtoRevitPlugin.Models
{
    public class PriceList
    {
        /// <summary>Id interno SQLite (auto-increment, stabile solo all'interno dello stesso DB).</summary>
        public int Id { get; set; }

        /// <summary>
        /// Id PORTABILE (GUID) — usato dal <c>ProjectPriceListSnapshot</c> nel DataStorage del .rvt
        /// per identificare il listino sorgente anche su PC senza la stessa UserLibrary.
        /// Generato automaticamente al primo insert se vuoto.
        /// </summary>
        public string PublicId { get; set; } = string.Empty;

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
