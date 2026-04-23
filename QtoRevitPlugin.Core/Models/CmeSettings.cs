using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Impostazioni utente persistenti del plugin CME.
    /// Serializzate in %AppData%\CmePlugin\settings.json (vedi SettingsService).
    /// </summary>
    public class CmeSettings
    {
        public const int MinAutoSaveIntervalMinutes = 30;
        public const int DefaultAutoSaveIntervalMinutes = 30;

        /// <summary>Abilita il salvataggio automatico periodico della sessione attiva.</summary>
        public bool AutoSaveEnabled { get; set; } = true;

        /// <summary>Intervallo in minuti tra salvataggi automatici. Minimo 30 minuti.</summary>
        public int AutoSaveIntervalMinutes { get; set; } = DefaultAutoSaveIntervalMinutes;

        /// <summary>Ultimo file .cme aperto o creato dall'utente, per il resume rapido dalla home.</summary>
        public string LastSessionFilePath { get; set; } = string.Empty;

        /// <summary>Normalizza il valore a un minimo accettabile.</summary>
        public int NormalizedAutoSaveIntervalMinutes =>
            Math.Max(MinAutoSaveIntervalMinutes, AutoSaveIntervalMinutes);
    }
}
