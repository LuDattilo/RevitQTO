using QtoRevitPlugin.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QtoRevitPlugin.AI
{
    // ==========================================================================
    // Punti di estensione AI — tutti opzionali, tutti con fallback deterministico
    // ==========================================================================
    //
    // Principi (QTO-AI-Integration.md §1):
    //
    // 1. Graceful degradation — se il provider non è disponibile, il plugin
    //    funziona al 100% senza AI grazie a NullAiProvider / fallback statici.
    //
    // 2. Nessuna azione automatica — i suggerimenti sono SEMPRE informativi.
    //    L'utente deve confermare prima di qualsiasi scrittura Revit.
    //
    // 3. Interfaccia astratta — la logica applicativa usa solo IQtoAiProvider.
    //    Backend (Ollama locale / API cloud / null) intercambiabile.
    //
    // 4. Privacy by default — con Ollama tutto resta in locale, nessun dato esce.
    //
    // ==========================================================================

    /// <summary>
    /// Provider principale delle funzionalità AI del plugin. Unico punto di accesso
    /// per ViewModels e logica applicativa. Le implementazioni concrete sono:
    /// <list type="bullet">
    ///   <item><see cref="NullAiProvider"/> — fallback no-op (AI disabilitata)</item>
    ///   <item><c>OllamaAiProvider</c> — Ollama in locale (raccomandato)</item>
    ///   <item>Futuri: API cloud (OpenRouter / Azure AI / ecc.)</item>
    /// </list>
    /// </summary>
    public interface IQtoAiProvider
    {
        /// <summary>
        /// True se il provider è operativo. Il chiamante DEVE controllare questo flag
        /// prima di usare i metodi; le implementazioni no-op ritornano false.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Data una famiglia/tipo Revit, suggerisce le N voci EP semanticamente più vicine
        /// (cosine similarity su embedding pre-calcolati in <c>EmbeddingCache</c>).
        /// </summary>
        /// <param name="familyName">Nome famiglia Revit (es. "Muro base").</param>
        /// <param name="category">Categoria Revit (es. "Muri").</param>
        /// <param name="topN">Massimo numero di suggerimenti da ritornare.</param>
        /// <param name="ct">Cancellation token per richieste lunghe.</param>
        Task<IReadOnlyList<MappingSuggestion>> SuggestEpAsync(
            string familyName,
            string category,
            int topN = 3,
            CancellationToken ct = default);

        /// <summary>
        /// Analizza le assegnazioni EP esistenti e segnala abbinamenti categoria/EP
        /// semanticamente improbabili (cosine similarity &lt; soglia).
        /// </summary>
        Task<IReadOnlyList<SemanticMismatch>> FindSemanticMismatchesAsync(
            IReadOnlyList<QtoAssignment> assignments,
            CancellationToken ct = default);

        /// <summary>
        /// Genera una descrizione breve (10-15 parole) da una descrizione EP lunga.
        /// Ritorna stringa vuota se il provider non è disponibile.
        /// </summary>
        Task<string> SummarizeDescriptionAsync(string longDescription, CancellationToken ct = default);

        /// <summary>
        /// Ricerca semantica nel listino (alternativa/complemento a FTS5).
        /// Filtra per <paramref name="activeListIds"/>; ritorna top N per cosine similarity.
        /// </summary>
        Task<IReadOnlyList<PriceItem>> SemanticSearchAsync(
            string query,
            IReadOnlyList<int> activeListIds,
            int topN = 10,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Provider di embedding (vettori float[] da testo). Usato da
    /// <c>EmbeddingCacheService</c> per pre-calcolare gli embedding del prezzario
    /// e da <see cref="IQtoAiProvider.SemanticSearchAsync"/> per la query.
    /// </summary>
    public interface IEmbeddingProvider
    {
        bool IsAvailable { get; }

        /// <summary>Nome del modello (es. "nomic-embed-text"). Salvato con l'embedding
        /// per invalidare cache se l'utente cambia modello.</summary>
        string ModelName { get; }

        /// <summary>Dimensione attesa del vettore ritornato (es. 768 per nomic-embed-text).
        /// Usato per validazione. 0 se sconosciuta finché non arriva la prima risposta.</summary>
        int VectorSize { get; }

        Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

        /// <summary>Embedding di più testi in sequenza (Ollama non ha batch nativo).</summary>
        Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Provider di generazione testo (LLM). Usato per descrizioni brevi EP.
    /// </summary>
    public interface ITextModelProvider
    {
        bool IsAvailable { get; }
        string ModelName { get; }
        Task<string> CompleteAsync(string prompt, int maxTokens = 100, CancellationToken ct = default);
    }
}
