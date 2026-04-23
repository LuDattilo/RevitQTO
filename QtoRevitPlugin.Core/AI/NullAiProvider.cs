using QtoRevitPlugin.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QtoRevitPlugin.AI
{
    /// <summary>
    /// Provider AI no-op: usato quando il backend (Ollama) non è disponibile o l'utente
    /// ha disabilitato le funzioni AI. Ritorna sempre risultati vuoti senza errori.
    /// Permette al resto del plugin di chiamare <see cref="IQtoAiProvider"/> senza
    /// null-check continui e senza branching <c>if (aiEnabled) ...</c> ovunque.
    ///
    /// <para>Istanziabile in qualsiasi contesto: non ha dipendenze esterne (HTTP/DB).</para>
    /// </summary>
    public sealed class NullAiProvider : IQtoAiProvider
    {
        /// <summary>Istanza singleton pronta all'uso. Lo stato non cambia mai (sempre
        /// non-disponibile), quindi un'unica istanza statica è più efficiente di
        /// crearne una nuova ogni volta.</summary>
        public static readonly NullAiProvider Instance = new NullAiProvider();

        public bool IsAvailable => false;

        public Task<IReadOnlyList<MappingSuggestion>> SuggestEpAsync(
            string familyName, string category, int topN = 3, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MappingSuggestion>>(EmptySuggestions);

        public Task<IReadOnlyList<SemanticMismatch>> FindSemanticMismatchesAsync(
            IReadOnlyList<QtoAssignment> assignments, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SemanticMismatch>>(EmptyMismatches);

        public Task<string> SummarizeDescriptionAsync(
            string longDescription, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<PriceItem>> SemanticSearchAsync(
            string query, IReadOnlyList<int> activeListIds, int topN = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PriceItem>>(EmptyItems);

        // Array statici per evitare allocazioni ripetute per ogni call a un provider no-op
        private static readonly IReadOnlyList<MappingSuggestion> EmptySuggestions = new List<MappingSuggestion>(0);
        private static readonly IReadOnlyList<SemanticMismatch> EmptyMismatches = new List<SemanticMismatch>(0);
        private static readonly IReadOnlyList<PriceItem> EmptyItems = new List<PriceItem>(0);
    }

    /// <summary>
    /// Provider di embedding no-op. Ritorna vettori vuoti.
    /// Da usare come fallback quando Ollama non è raggiungibile.
    /// </summary>
    public sealed class NullEmbeddingProvider : IEmbeddingProvider
    {
        public static readonly NullEmbeddingProvider Instance = new NullEmbeddingProvider();

        public bool IsAvailable => false;
        public string ModelName => "(none)";
        public int VectorSize => 0;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => Task.FromResult(System.Array.Empty<float>());

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(new List<float[]>(0));
    }

    /// <summary>Provider di testo no-op. Ritorna stringa vuota.</summary>
    public sealed class NullTextModelProvider : ITextModelProvider
    {
        public static readonly NullTextModelProvider Instance = new NullTextModelProvider();

        public bool IsAvailable => false;
        public string ModelName => "(none)";

        public Task<string> CompleteAsync(string prompt, int maxTokens = 100, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
