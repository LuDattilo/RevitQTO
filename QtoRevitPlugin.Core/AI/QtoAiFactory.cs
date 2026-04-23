using QtoRevitPlugin.AI.Ollama;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;

namespace QtoRevitPlugin.AI
{
    /// <summary>
    /// Factory per istanziare il provider AI corretto in base alle impostazioni
    /// utente e alla disponibilità di Ollama.
    ///
    /// <para>Algoritmo di selezione (QTO-AI-Integration.md §9):</para>
    /// <list type="number">
    ///   <item>Se <c>AiEnabled = false</c> → <see cref="NullAiProvider"/>.</item>
    ///   <item>Prova ad istanziare <see cref="OllamaEmbeddingProvider"/> e verifica
    ///   <see cref="IEmbeddingProvider.IsAvailable"/>.</item>
    ///   <item>Se Ollama non risponde → log warning e fallback <see cref="NullAiProvider"/>.</item>
    ///   <item>Se risponde → istanzia <see cref="OllamaTextModelProvider"/> (anche se il modello
    ///   LLM specifico non c'è, il provider ritorna stringhe vuote) e compone
    ///   <see cref="OllamaAiProvider"/>.</item>
    /// </list>
    ///
    /// <para>Thread safety: metodo statico puro, safe per chiamate concorrenti.
    /// La factory crea istanze nuove ad ogni chiamata — il chiamante gestisce lifetime.</para>
    /// </summary>
    public static class QtoAiFactory
    {
        /// <summary>
        /// Istanzia il provider AI appropriato. Ritorna sempre un <see cref="IQtoAiProvider"/>
        /// non-null (Null fallback in caso di errore).
        /// </summary>
        /// <param name="settings">Impostazioni utente (AiEnabled, URLs, modelli, soglie).</param>
        /// <param name="repo">Repository della sessione attiva (per cache embedding).</param>
        /// <param name="logger">Callback opzionale per log warning (es. CrashLogger). Se null, errori silenti.</param>
        public static IQtoAiProvider Create(
            CmeSettings settings,
            IQtoRepository repo,
            Action<string>? logger = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            if (!settings.AiEnabled)
            {
                logger?.Invoke("AI disabled in settings — using NullAiProvider.");
                return NullAiProvider.Instance;
            }

            try
            {
                var embedding = new OllamaEmbeddingProvider(
                    settings.OllamaBaseUrl,
                    settings.EmbeddingModel);

                if (!embedding.IsAvailable)
                {
                    logger?.Invoke(
                        $"Ollama non raggiungibile su {settings.OllamaBaseUrl}. AI disabilitata per questa sessione. " +
                        "Verifica che Ollama sia in esecuzione (ollama serve) e che il modello embedding sia scaricato " +
                        $"(ollama pull {settings.EmbeddingModel}).");
                    embedding.Dispose();
                    return NullAiProvider.Instance;
                }

                var text = new OllamaTextModelProvider(
                    settings.OllamaBaseUrl,
                    settings.TextModel);

                var provider = new OllamaAiProvider(embedding, text, repo)
                {
                    SuggestThreshold = (float)settings.SuggestThreshold,
                    SemanticSearchThreshold = (float)settings.SemanticSearchThreshold,
                    MismatchThreshold = (float)settings.MismatchThreshold
                };

                logger?.Invoke($"AI provider attivo: Ollama ({settings.EmbeddingModel} / {settings.TextModel}).");
                return provider;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Errore durante l'inizializzazione AI: {ex.Message}. Fallback su NullAiProvider.");
                return NullAiProvider.Instance;
            }
        }
    }
}
