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

        /// <summary>Ultimo file .cme aperto o creato dall'utente, per il resume rapido dalla HomeView.</summary>
        public string LastSessionFilePath { get; set; } = string.Empty;

        /// <summary>Normalizza il valore a un minimo accettabile.</summary>
        public int NormalizedAutoSaveIntervalMinutes =>
            Math.Max(MinAutoSaveIntervalMinutes, AutoSaveIntervalMinutes);

        // ============================================================
        // AI integration (opzionale, Sprint AI)
        // ============================================================

        /// <summary>Abilita il modulo AI. Se false, il plugin usa sempre NullAiProvider.</summary>
        public bool AiEnabled { get; set; } = false;

        /// <summary>URL base di Ollama. Default: servizio locale standard.</summary>
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

        /// <summary>Modello embedding (consigliato nomic-embed-text ~274MB).</summary>
        public string EmbeddingModel { get; set; } = "nomic-embed-text";

        /// <summary>Modello di testo LLM (consigliato llama3.2:3b ~2GB).</summary>
        public string TextModel { get; set; } = "llama3.2:3b";

        /// <summary>Soglia cosine per suggerimenti EP (default 0.65 = abbinamento ragionevole).</summary>
        public double SuggestThreshold { get; set; } = 0.65;

        /// <summary>Soglia cosine per ricerca semantica (default 0.60 = più permissivo per sinonimi).</summary>
        public double SemanticSearchThreshold { get; set; } = 0.60;

        /// <summary>Sotto questa soglia, un abbinamento categoria/EP è segnalato come mismatch.</summary>
        public double MismatchThreshold { get; set; } = 0.45;
    }
}
