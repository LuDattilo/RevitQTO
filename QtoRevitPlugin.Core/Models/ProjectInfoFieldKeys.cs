namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Identificatori stabili dei campi della scheda Informazioni Progetto.
    /// Usati come FieldKey nella tabella RevitParamMapping del .cme (schema v9).
    /// NON cambiare i valori stringa: eventuali rename richiederebbero una migration.
    /// </summary>
    public static class ProjectInfoFieldKeys
    {
        public const string DenominazioneOpera   = "DenominazioneOpera";
        public const string Committente          = "Committente";
        public const string Impresa              = "Impresa";
        public const string Rup                  = "RUP";
        public const string DirettoreLavori      = "DirettoreLavori";
        public const string Luogo                = "Luogo";
        public const string Comune               = "Comune";
        public const string Provincia            = "Provincia";
        public const string Cig                  = "CIG";
        public const string Cup                  = "CUP";
        public const string RiferimentoPrezzario = "RiferimentoPrezzario";

        /// <summary>Elenco di tutti i FieldKey nell'ordine visivo della scheda.</summary>
        public static readonly string[] All = new[]
        {
            DenominazioneOpera, Committente, Impresa, Rup, DirettoreLavori,
            Luogo, Comune, Provincia, Cig, Cup, RiferimentoPrezzario
        };

        /// <summary>Label UI human-readable per ogni FieldKey.</summary>
        public static string DisplayNameFor(string fieldKey) => fieldKey switch
        {
            DenominazioneOpera   => "Denominazione opera",
            Committente          => "Committente",
            Impresa              => "Impresa appaltatrice",
            Rup                  => "RUP",
            DirettoreLavori      => "Direttore dei Lavori",
            Luogo                => "Luogo (via/piazza)",
            Comune               => "Comune",
            Provincia            => "Provincia",
            Cig                  => "CIG",
            Cup                  => "CUP",
            RiferimentoPrezzario => "Riferimento prezzario",
            _                    => fieldKey
        };

        /// <summary>Nome Shared Parameter suggerito quando l'utente clicca "+ Aggiungi SP".
        /// Prefisso "CME_" per distinguerlo dai parametri nativi Revit.</summary>
        public static string SuggestedSharedParamNameFor(string fieldKey) => "CME_" + fieldKey;
    }
}
