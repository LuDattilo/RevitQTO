using System;

namespace QtoRevitPlugin.Models
{
    public enum NpStatus
    {
        Bozza,
        Concordato,
        Approvato
    }

    /// <summary>
    /// Nuovo Prezzo (NP) — voce per lavorazioni non presenti nell'EP contrattuale.
    /// Riferimento normativo: art. 5 comma 7 All. II.14 e art. 120 D.Lgs. 36/2023.
    /// </summary>
    public class NuovoPrezzo
    {
        public int Id { get; set; }
        public int SessionId { get; set; }

        public string Code { get; set; } = string.Empty;      // es. "NP.001"
        public string Description { get; set; } = string.Empty;
        public string ShortDesc { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;

        // Componenti analisi prezzi
        public double Manodopera { get; set; }
        public double Materiali { get; set; }
        public double Noli { get; set; }
        public double Trasporti { get; set; }

        /// <summary>Spese generali in % (range 13–17% per D.Lgs. 36/2023 All. II.14).</summary>
        public double SpGenerali { get; set; } = 15.0;

        /// <summary>Utile impresa in % (10% per D.Lgs. 36/2023 All. II.14).</summary>
        public double UtileImpresa { get; set; } = 10.0;

        /// <summary>Ribasso d'asta opzionale (Parere MIT n. 3545/2025).</summary>
        public double RibassoAsta { get; set; }

        /// <summary>CT = Manodopera + Materiali + Noli + Trasporti</summary>
        public double CostoTotale => Manodopera + Materiali + Noli + Trasporti;

        /// <summary>NP = CT × (1 + SG%) × (1 + Utile%) × (1 − Ribasso%)</summary>
        public double UnitPrice =>
            CostoTotale
            * (1.0 + SpGenerali / 100.0)
            * (1.0 + UtileImpresa / 100.0)
            * (1.0 - RibassoAsta / 100.0);

        public NpStatus Status { get; set; } = NpStatus.Bozza;
        public string NoteAnalisi { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
