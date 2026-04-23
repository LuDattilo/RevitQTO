using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QtoRevitPlugin.AI.Ollama
{
    /// <summary>
    /// Provider AI principale che usa Ollama per embedding e LLM, e una cache in-memory
    /// di vettori pre-calcolati per il listino (caricata lazy da <see cref="LoadEmbeddingCacheAsync"/>).
    ///
    /// <para><b>Contratto con il chiamante</b> (Sprint AI):</para>
    /// <list type="number">
    ///   <item>Factory (o startup) verifica <see cref="IEmbeddingProvider.IsAvailable"/>
    ///   — se false, istanzia <see cref="NullAiProvider"/> al suo posto.</item>
    ///   <item>Dopo il load del listino attivo, chiamare <see cref="EnsureEmbeddingCacheAsync"/>
    ///   che pre-calcola gli embedding mancanti e li persiste in DB.</item>
    ///   <item>Prima di usare <see cref="SuggestEpAsync"/> / <see cref="SemanticSearchAsync"/>,
    ///   chiamare <see cref="LoadEmbeddingCacheAsync"/> che riempie la cache in memoria.</item>
    /// </list>
    /// </summary>
    public sealed class OllamaAiProvider : IQtoAiProvider
    {
        private readonly IEmbeddingProvider _embedding;
        private readonly ITextModelProvider _text;
        private readonly IQtoRepository _repo;

        /// <summary>Soglia minima cosine per <see cref="SuggestEpAsync"/> (default 0.65).</summary>
        public float SuggestThreshold { get; set; } = 0.65f;

        /// <summary>Soglia minima cosine per <see cref="SemanticSearchAsync"/> (default 0.60).</summary>
        public float SemanticSearchThreshold { get; set; } = 0.60f;

        /// <summary>Soglia sotto la quale <see cref="FindSemanticMismatchesAsync"/>
        /// segnala mismatch (default 0.45).</summary>
        public float MismatchThreshold { get; set; } = 0.45f;

        /// <summary>Cache in-memory degli embedding del listino attivo: PriceItemId → vettore.</summary>
        private Dictionary<int, float[]> _cache = new Dictionary<int, float[]>();

        /// <summary>Mapping Code → PriceItemId per <see cref="FindSemanticMismatchesAsync"/>
        /// che risolve via EpCode di <see cref="QtoAssignment"/>. Popolato da
        /// <see cref="LoadEmbeddingCache"/>. Case-insensitive (codici prezzario mescolati).</summary>
        private Dictionary<string, int> _codeToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public OllamaAiProvider(
            IEmbeddingProvider embedding,
            ITextModelProvider text,
            IQtoRepository repo)
        {
            _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        public bool IsAvailable => _embedding.IsAvailable;

        /// <summary>
        /// Pre-calcola (una volta sola) gli embedding per tutte le voci passate,
        /// salvandoli in <c>EmbeddingCache</c>. Skippa quelle già presenti in DB.
        /// </summary>
        public async Task EnsureEmbeddingCacheAsync(
            IReadOnlyList<PriceItem> items,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            if (items == null || items.Count == 0) return;

            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[i];

                if (_repo.HasEmbedding(item.Id, _embedding.ModelName))
                {
                    progress?.Report(i + 1);
                    continue;
                }

                var text = BuildItemText(item);
                var vec = await _embedding.EmbedAsync(text, ct).ConfigureAwait(false);
                if (vec.Length == 0) continue;

                _repo.UpsertEmbedding(item.Id, _embedding.ModelName, EmbeddingSerializer.Serialize(vec));
                progress?.Report(i + 1);
            }
        }

        /// <summary>
        /// Carica in memoria gli embedding già in cache per i PriceItemId specificati,
        /// per abilitare ricerca in-memory veloce (no round-trip DB per ogni query).
        /// Costruisce anche <see cref="_codeToId"/> per lookup da EpCode → PriceItemId.
        /// </summary>
        public void LoadEmbeddingCache(IReadOnlyList<int> priceItemIds)
        {
            if (priceItemIds == null || priceItemIds.Count == 0)
            {
                _cache = new Dictionary<int, float[]>();
                _codeToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var entries = _repo.GetEmbeddings(priceItemIds, _embedding.ModelName);
            _cache = entries.ToDictionary(
                e => e.PriceItemId,
                e => EmbeddingSerializer.Deserialize(e.VectorBlob));

            // Popola Code→Id via batch GetPriceItems (aggiunto per supporto mismatch)
            var items = _repo.GetPriceItems(priceItemIds);
            _codeToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
            {
                if (!string.IsNullOrEmpty(it.Code) && !_codeToId.ContainsKey(it.Code))
                    _codeToId[it.Code] = it.Id;
            }
        }

        // --------------------------------------------------------------------
        // IQtoAiProvider implementation
        // --------------------------------------------------------------------

        public async Task<IReadOnlyList<MappingSuggestion>> SuggestEpAsync(
            string familyName, string category, int topN = 3, CancellationToken ct = default)
        {
            if (!IsAvailable || _cache.Count == 0) return new List<MappingSuggestion>();

            var queryText = $"{category} {familyName}".Trim();
            if (string.IsNullOrEmpty(queryText)) return new List<MappingSuggestion>();

            var queryVec = await _embedding.EmbedAsync(queryText, ct).ConfigureAwait(false);
            if (queryVec.Length == 0) return new List<MappingSuggestion>();

            // 1. Trova top-N (PriceItemId, Score)
            var scored = _cache
                .Select(kv => new { Id = kv.Key, Score = CosineSimilarity.Compute(queryVec, kv.Value) })
                .Where(x => x.Score > SuggestThreshold)
                .OrderByDescending(x => x.Score)
                .Take(topN)
                .ToList();

            if (scored.Count == 0) return new List<MappingSuggestion>();

            // 2. Batch-load PriceItem completi per popolare il DTO
            var items = _repo.GetPriceItems(scored.Select(x => x.Id).ToList());
            var byId = items.ToDictionary(i => i.Id, i => i);

            return scored
                .Select(x => new MappingSuggestion
                {
                    PriceItem = byId.TryGetValue(x.Id, out var pi) ? pi : null,
                    Score = x.Score,
                    Label = $"{x.Score:P0} match"
                })
                .ToList();
        }

        public async Task<IReadOnlyList<SemanticMismatch>> FindSemanticMismatchesAsync(
            IReadOnlyList<QtoAssignment> assignments, CancellationToken ct = default)
        {
            if (!IsAvailable || _cache.Count == 0 || assignments == null || assignments.Count == 0)
                return new List<SemanticMismatch>();

            var mismatches = new List<SemanticMismatch>();

            // Cache locale degli embedding di categoria per evitare di ricalcolarli per
            // assignments con stessa category+family (Revit tipicamente ha molti elementi
            // della stessa famiglia → risparmio consistente sulle chiamate Ollama).
            var categoryVecCache = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var a in assignments)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(a.EpCode)) continue;

                // 1. Risolvi PriceItemId dal Code dell'assegnazione
                if (!_codeToId.TryGetValue(a.EpCode, out var priceItemId)) continue;

                // 2. Embedding dell'EP deve essere in cache (precaricato)
                if (!_cache.TryGetValue(priceItemId, out var epVec)) continue;

                // 3. Embedding della categoria+famiglia Revit (cache per ridurre call)
                var catKey = $"{a.Category}|{a.FamilyName}";
                if (!categoryVecCache.TryGetValue(catKey, out var catVec))
                {
                    catVec = await _embedding
                        .EmbedAsync($"{a.Category} {a.FamilyName}".Trim(), ct)
                        .ConfigureAwait(false);
                    categoryVecCache[catKey] = catVec;
                }
                if (catVec.Length == 0) continue;

                // 4. Similarity check
                float similarity = CosineSimilarity.Compute(catVec, epVec);
                if (similarity >= MismatchThreshold) continue;

                // 5. Mismatch: suggerisci alternative migliori per quello family+category
                var suggestions = await SuggestEpAsync(a.FamilyName, a.Category, topN: 3, ct)
                    .ConfigureAwait(false);

                mismatches.Add(new SemanticMismatch
                {
                    UniqueId = a.UniqueId,
                    Category = a.Category,
                    FamilyName = a.FamilyName,
                    EpCode = a.EpCode,
                    EpDescription = a.EpDescription,
                    Similarity = similarity,
                    Suggestions = suggestions
                });
            }

            return mismatches;
        }

        public async Task<string> SummarizeDescriptionAsync(
            string longDescription, CancellationToken ct = default)
        {
            if (!_text.IsAvailable || string.IsNullOrWhiteSpace(longDescription))
                return string.Empty;

            // Prompt calibrato per ottenere output brevissimo e senza punteggiatura finale
            var prompt =
                "Riassumi questa descrizione tecnica di una voce di computo in massimo 12 parole.\n" +
                "Rispondi solo con la descrizione breve, senza punteggiatura finale, senza virgolette, senza prefissi.\n\n" +
                "Descrizione: " + longDescription + "\n" +
                "Risposta:";

            var result = await _text.CompleteAsync(prompt, maxTokens: 40, ct).ConfigureAwait(false);
            return SanitizeShortDesc(result);
        }

        public async Task<IReadOnlyList<PriceItem>> SemanticSearchAsync(
            string query, IReadOnlyList<int> activeListIds, int topN = 10, CancellationToken ct = default)
        {
            if (!IsAvailable || _cache.Count == 0 || string.IsNullOrWhiteSpace(query))
                return new List<PriceItem>();

            // 1. Embed query
            var queryVec = await _embedding.EmbedAsync(query, ct).ConfigureAwait(false);
            if (queryVec.Length == 0) return new List<PriceItem>();

            // 2. Top-N dei PriceItemId della cache per cosine, filtrato per soglia
            var topIds = _cache
                .Select(kv => new { Id = kv.Key, Score = CosineSimilarity.Compute(queryVec, kv.Value) })
                .Where(x => x.Score > SemanticSearchThreshold)
                .OrderByDescending(x => x.Score)
                .Take(topN)
                .Select(x => x.Id)
                .ToList();

            if (topIds.Count == 0) return new List<PriceItem>();

            // 3. Batch-load PriceItem completi
            var items = _repo.GetPriceItems(topIds);

            // 4. Filtra per listini attivi (se specificato); mantiene l'ordine di topIds per score
            var activeSet = activeListIds != null && activeListIds.Count > 0
                ? new HashSet<int>(activeListIds)
                : null;

            var byId = items.ToDictionary(i => i.Id, i => i);
            var result = new List<PriceItem>(topIds.Count);
            foreach (var id in topIds)
            {
                if (!byId.TryGetValue(id, out var item)) continue;
                if (activeSet != null && !activeSet.Contains(item.PriceListId)) continue;
                result.Add(item);
            }
            return result;
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        /// <summary>
        /// Costruisce il testo da embeddare per una voce di listino.
        /// Concatena Code + Description + contesto capitolare per massimizzare
        /// il match con la query dell'utente ("muratura in laterizio" → matchera
        /// meglio sia il code "A01.01.001" sia la descrizione).
        /// </summary>
        public static string BuildItemText(PriceItem item)
        {
            if (item == null) return string.Empty;
            var parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(item.Code)) parts.Add(item.Code);
            if (!string.IsNullOrWhiteSpace(item.Description)) parts.Add(item.Description);
            if (!string.IsNullOrWhiteSpace(item.Chapter)) parts.Add(item.Chapter);
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Pulisce la risposta LLM: rimuove quote, punteggiatura finale eccessiva,
        /// escape residui. Limite a 200 char per sicurezza.
        /// Esposto come public per test; chiamato internamente da <see cref="SummarizeDescriptionAsync"/>.
        /// </summary>
        public static string SanitizeShortDesc(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = raw.Trim().Trim('"', '\'', '«', '»', '`');
            while (s.Length > 0 && (s[s.Length - 1] == '.' || s[s.Length - 1] == ',' || s[s.Length - 1] == ';'))
                s = s.Substring(0, s.Length - 1);
            if (s.Length > 200) s = s.Substring(0, 200);
            return s.Trim();
        }
    }
}
