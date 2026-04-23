namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Stato del modulo AI esposto al livello UI (badge HomeView, status bar).
    /// Calcolato dal VM via probe async a Ollama /api/tags.
    /// </summary>
    public enum AiStatusKind
    {
        /// <summary>AI disabilitata dalle impostazioni (CmeSettings.AiEnabled = false).</summary>
        Disabled,

        /// <summary>AI abilitata ma Ollama non raggiungibile. UI mostra warning/guida.</summary>
        Unavailable,

        /// <summary>Probe in corso (transitorio, prima risposta async). UI mostra spinner/placeholder.</summary>
        Checking,

        /// <summary>Ollama risponde su /api/tags. AI operativa.</summary>
        Ready
    }
}
