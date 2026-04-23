using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Metadati di progetto persistiti per-sessione nel file .cme (tabella ProjectInfo,
    /// UNIQUE su SessionId). Valorizzati dall'utente tramite la sezione "Informazioni
    /// Progetto" della SetupView; consumati da ExportWizard/XpweExporter per popolare
    /// l'intestazione del computo conforme a PriMus-net (D.Lgs. 36/2023 Codice Contratti).
    ///
    /// Campi obbligatori per il computo pubblico: <see cref="DenominazioneOpera"/>,
    /// <see cref="Committente"/>, <see cref="RUP"/>, <see cref="DirettoreLavori"/>,
    /// <see cref="CIG"/>, <see cref="CUP"/>.
    /// </summary>
    public class ProjectInfo
    {
        public int Id { get; set; }
        public int SessionId { get; set; }

        // Intestazione base
        public string DenominazioneOpera { get; set; } = "";
        public string Committente { get; set; } = "";
        public string Impresa { get; set; } = "";
        public string RUP { get; set; } = "";
        public string DirettoreLavori { get; set; } = "";
        public string Luogo { get; set; } = "";
        public string Comune { get; set; } = "";
        public string Provincia { get; set; } = "";

        // Date (nullable: l'utente può lasciarle vuote)
        public DateTime? DataComputo { get; set; }
        public DateTime? DataPrezzi { get; set; }

        // Riferimenti normativi / contrattuali
        public string RiferimentoPrezzario { get; set; } = "";
        public string CIG { get; set; } = "";
        public string CUP { get; set; } = "";
        public decimal RibassoPercentuale { get; set; }

        // Logo
        public string LogoPath { get; set; } = "";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
