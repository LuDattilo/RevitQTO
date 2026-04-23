using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.AI
{
    /// <summary>
    /// Gateway di alto livello per ottenere suggerimenti EP basati su AI senza
    /// forzare i chiamanti UI a gestire factory, provider fallback, cache embedding,
    /// timeout e cancellation. Segue il principio "graceful degradation" del doc
    /// AI-Integration: se AI è disabilitata, irraggiungibile o fallisce, ritorna
    /// lista vuota (mai throw al caller, mai null).
    ///
    /// <para>Uso tipico (es. apertura PickEpDialog nel Tagging):</para>
    /// <code>
    /// var suggestions = await AiSuggestionsGateway.GetSuggestionsAsync(
    ///     settings, repo, familyName: "Muro base", category: "Walls", topN: 3);
    /// // suggestions può essere vuota — l'UI mostra la sezione solo se Count > 0
    /// </code>
    /// </summary>
    public static class AiSuggestionsGateway
    {
        /// <summary>
        /// Chiede al provider AI i top-N suggerimenti EP per (familyName, category).
        /// Fallback safe (lista vuota) in caso di: AiEnabled=false, Ollama non
        /// raggiungibile, eccezione nel provider, timeout.
        /// </summary>
        /// <param name="settings">Impostazioni correnti (AiEnabled + URL Ollama).</param>
        /// <param name="repo">Repository della sessione attiva (richiesto dal factory per cache).</param>
        /// <param name="familyName">Nome famiglia Revit (es. "Muro base").</param>
        /// <param name="category">Categoria Revit (es. "Walls" o "Muri").</param>
        /// <param name="topN">Massimo numero di suggerimenti (default 3).</param>
        /// <param name="timeoutMs">
        /// Timeout hard in millisecondi — previene che l'UI resti appesa su
        /// chiamate lente a Ollama. Default 3000ms.
        /// </param>
        /// <param name="logger">Callback log opzionale (es. CrashLogger.Warn).</param>
        public static async Task<IReadOnlyList<MappingSuggestion>> GetSuggestionsAsync(
            CmeSettings settings,
            IQtoRepository repo,
            string familyName,
            string category,
            int topN = 3,
            int timeoutMs = 3000,
            Action<string>? logger = null,
            CancellationToken externalCt = default)
        {
            if (settings == null) return Array.Empty<MappingSuggestion>();
            if (!settings.AiEnabled) return Array.Empty<MappingSuggestion>();
            if (repo == null) return Array.Empty<MappingSuggestion>();
            if (string.IsNullOrWhiteSpace(familyName) && string.IsNullOrWhiteSpace(category))
                return Array.Empty<MappingSuggestion>();

            IQtoAiProvider? provider = null;
            try
            {
                provider = QtoAiFactory.Create(settings, repo, logger);
            }
            catch (Exception ex)
            {
                logger?.Invoke($"AiSuggestionsGateway: factory throw — {ex.Message}");
                return Array.Empty<MappingSuggestion>();
            }

            if (provider == null || !provider.IsAvailable)
            {
                // Factory è già difensiva: NullAiProvider → IsAvailable=false.
                return Array.Empty<MappingSuggestion>();
            }

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
                linkedCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

                var results = await provider
                    .SuggestEpAsync(familyName, category, topN, linkedCts.Token)
                    .ConfigureAwait(false);

                return results ?? Array.Empty<MappingSuggestion>();
            }
            catch (OperationCanceledException)
            {
                logger?.Invoke($"AiSuggestionsGateway: timeout dopo {timeoutMs}ms");
                return Array.Empty<MappingSuggestion>();
            }
            catch (Exception ex)
            {
                logger?.Invoke($"AiSuggestionsGateway: SuggestEpAsync throw — {ex.GetType().Name}: {ex.Message}");
                return Array.Empty<MappingSuggestion>();
            }
            finally
            {
                // Il factory crea un nuovo provider ad ogni chiamata (vedi comment
                // nel Create): smaltiamo HTTP client se IDisposable.
                if (provider is IDisposable d) d.Dispose();
            }
        }

        /// <summary>
        /// UI-9: warmup asincrono della cache embedding per il listino attivo.
        /// Chiamato in background al session load così la prima chiamata a
        /// <see cref="GetSuggestionsAsync"/> è istantanea (niente HTTP embed per-item).
        /// </summary>
        /// <param name="settings">CmeSettings per AI enabled + URL Ollama.</param>
        /// <param name="repo">Repository attivo (richiesto dal factory).</param>
        /// <param name="items">
        /// Voci del listino attivo per cui garantire l'embedding in cache.
        /// Già presenti in DB vengono skippate (idempotente).
        /// </param>
        /// <param name="progress">Callback opzionale per progress UI (index 1-based).</param>
        /// <param name="logger">Callback opzionale warn.</param>
        /// <returns>
        /// true se il warmup è stato eseguito (AI Ready); false se AI disabled
        /// / unreachable / eccezione. Mai throw.
        /// </returns>
        public static async Task<bool> WarmupEmbeddingCacheAsync(
            CmeSettings settings,
            IQtoRepository repo,
            IReadOnlyList<PriceItem> items,
            IProgress<int>? progress = null,
            Action<string>? logger = null,
            CancellationToken externalCt = default)
        {
            if (settings == null || !settings.AiEnabled) return false;
            if (repo == null || items == null || items.Count == 0) return false;

            IQtoAiProvider? provider = null;
            try
            {
                provider = QtoAiFactory.Create(settings, repo, logger);
                if (provider == null || !provider.IsAvailable)
                    return false;

                // EnsureEmbeddingCacheAsync è definito solo su OllamaAiProvider
                // concreto, non sull'interfaccia. Uso pattern type-check per
                // evitare di sporcare l'interfaccia con un metodo runtime-only.
                if (provider is Ollama.OllamaAiProvider ollama)
                {
                    await ollama.EnsureEmbeddingCacheAsync(items, progress, externalCt)
                        .ConfigureAwait(false);
                    return true;
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                logger?.Invoke("AiSuggestionsGateway.Warmup: cancellation");
                return false;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"AiSuggestionsGateway.Warmup throw — {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally
            {
                if (provider is IDisposable d) d.Dispose();
            }
        }
    }
}
