namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Voce di elenco prezzi (da DCF, Excel o CSV). Può essere una voce standard o un Nuovo Prezzo (IsNP).
    /// </summary>
    public class PriceItem
    {
        public int Id { get; set; }
        public int PriceListId { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Chapter { get; set; } = string.Empty;
        public string SubChapter { get; set; } = string.Empty;
        public string SuperChapter { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ShortDesc { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double UnitPrice { get; set; }
        public string Notes { get; set; } = string.Empty;

        /// <summary>True se la voce è un Nuovo Prezzo (non nel contratto originale).</summary>
        public bool IsNP { get; set; }

        /// <summary>Nome del listino di provenienza — popolato in join con PriceLists.</summary>
        public string ListName { get; set; } = string.Empty;

        // ============================================================
        // Plan C-4 (schema v12): campi XPWE per compliance PriMus.
        // Backward-compat: UnitPrice resta primario, Prezzo1 lo replica.
        // Dapper popola queste property dalle colonne v12 esistenti.
        // ============================================================

        /// <summary>Articolo XPWE (secondo livello codice, opzionale).</summary>
        public string? Articolo { get; set; }

        /// <summary>Tariffa XPWE (terzo livello codice, opzionale).</summary>
        public string? Tariffa { get; set; }

        /// <summary>Prezzo listino 1 (lordo). Alias di UnitPrice per compat XPWE.</summary>
        public double Prezzo1 { get; set; }

        /// <summary>Prezzo listino 2 (tipicamente netto ribassato).</summary>
        public double Prezzo2 { get; set; }

        /// <summary>Prezzo listino 3.</summary>
        public double Prezzo3 { get; set; }

        /// <summary>Prezzo listino 4.</summary>
        public double Prezzo4 { get; set; }

        /// <summary>Prezzo listino 5.</summary>
        public double Prezzo5 { get; set; }

        /// <summary>FK → ChapterNode.Id (Level=SpCap).</summary>
        public int? SpCapId { get; set; }

        /// <summary>FK → ChapterNode.Id (Level=Cap).</summary>
        public int? CapId { get; set; }

        /// <summary>FK → ChapterNode.Id (Level=SbCap).</summary>
        public int? SbCapId { get; set; }

        /// <summary>FK → WbsNode.Id (Kind=WbsCap).</summary>
        public int? WbsCapNodeId { get; set; }

        /// <summary>Incidenza manodopera (%).</summary>
        public double IncMDO { get; set; }

        /// <summary>Incidenza materiali (%).</summary>
        public double IncMAT { get; set; }

        /// <summary>Incidenza sicurezza (%).</summary>
        public double IncSIC { get; set; }

        /// <summary>Tipo risorsa XPWE (0=default, 1-5 = MDO/MAT/ATT/ecc.).</summary>
        public int TipoRisorsa { get; set; }

        /// <summary>Flags XPWE (bitmask). Default 512 = voce standard.</summary>
        public int Flags { get; set; } = 512;

        /// <summary>Configurazione quantità XPWE.</summary>
        public string? CnfQt { get; set; }

        /// <summary>Indirizzo internet (riferimento esterno).</summary>
        public string? AdrInternet { get; set; }

        /// <summary>Data EP XPWE (DD/MM/YYYY, null se Excel zero).</summary>
        public string? DataEP { get; set; }

        public override string ToString() =>
            $"{Code} – {(string.IsNullOrEmpty(ShortDesc) ? Description : ShortDesc)}";
    }
}
