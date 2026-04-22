using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Search
{
    /// <summary>
    /// Ricerca voci di listino a 3 livelli:
    /// <list type="number">
    ///   <item><b>Livello 1 — Codice esatto</b>: match case-insensitive per <c>PriceItem.Code</c>.</item>
    ///   <item><b>Livello 2 — FTS5</b>: ricerca full-text su Description/ShortDesc/Chapter via <c>PriceItems_FTS</c>.
    ///   Se il livello 2 restituisce ≥ <see cref="MinFtsResultsToSkipFuzzy"/> risultati, il fuzzy viene saltato.</item>
    ///   <item><b>Livello 3 — Fuzzy Levenshtein</b>: fallback se FTS5 ritorna pochi risultati.
    ///   Ranking per similarity ≥ <paramref name="fuzzyThreshold"/> (default 0.6).</item>
    /// </list>
    /// La cache <c>_allItemsCache</c> evita di ricaricare gli items ad ogni fuzzy; invalidare con
    /// <see cref="InvalidateCache"/> dopo import di un nuovo listino.
    /// </summary>
    public class PriceItemSearchService
    {
        /// <summary>Soglia oltre cui si considerano FTS5 sufficienti (niente fuzzy L3).</summary>
        public const int MinFtsResultsToSkipFuzzy = 3;

        private readonly QtoRepository _repo;
        private List<PriceItem>? _allItemsCache;

        public PriceItemSearchService(QtoRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        /// <summary>
        /// Invalida la cache in-memory usata dal fuzzy L3. Da chiamare dopo
        /// <c>QtoRepository.InsertPriceItemsBatch</c> o toggle IsActive su un listino.
        /// </summary>
        public void InvalidateCache()
        {
            _allItemsCache = null;
        }

        /// <summary>
        /// Esegue la ricerca a 3 livelli. Ritorna sempre un <see cref="SearchResult"/>
        /// (mai null) con l'indicazione del livello effettivamente usato.
        /// </summary>
        /// <param name="query">Testo di ricerca (code esatto, parole libere o typo).</param>
        /// <param name="maxResults">Numero massimo di risultati cumulativi (default 20).</param>
        /// <param name="fuzzyThreshold">Similarity minima [0..1] per accettare un match fuzzy (default 0.6).</param>
        public SearchResult Search(string query, int maxResults = 20, double fuzzyThreshold = 0.6)
        {
            if (string.IsNullOrWhiteSpace(query))
                return SearchResult.Empty;

            // L1: codice esatto — se trovato, basta questo (rank superiore a tutto)
            var exact = _repo.FindByCodeExact(query);
            if (exact.Count > 0)
                return new SearchResult(SearchLevel.Exact, exact);

            // L2: FTS5
            var fts = _repo.SearchFts(query, maxResults);
            if (fts.Count >= MinFtsResultsToSkipFuzzy)
                return new SearchResult(SearchLevel.FullText, fts);

            // L3: Fuzzy fallback (concatena a FTS se presenti, dedup per Id)
            var combined = CombineWithFuzzy(query, fts, maxResults, fuzzyThreshold);
            var effectiveLevel = combined.Count > fts.Count ? SearchLevel.Fuzzy : SearchLevel.FullText;

            return new SearchResult(effectiveLevel, combined);
        }

        private IReadOnlyList<PriceItem> CombineWithFuzzy(
            string query,
            IReadOnlyList<PriceItem> ftsHits,
            int maxResults,
            double threshold)
        {
            var slotsLeft = maxResults - ftsHits.Count;
            if (slotsLeft <= 0) return ftsHits;

            _allItemsCache ??= _repo.GetAllActivePriceItems().ToList();

            var seenIds = new HashSet<int>(ftsHits.Select(p => p.Id));

            var fuzzyMatches = _allItemsCache
                .Where(p => !seenIds.Contains(p.Id))
                .Select(p => new
                {
                    Item = p,
                    Score = Score(query, p)
                })
                .Where(x => x.Score >= threshold)
                .OrderByDescending(x => x.Score)
                .Take(slotsLeft)
                .Select(x => x.Item)
                .ToList();

            var combined = new List<PriceItem>(ftsHits.Count + fuzzyMatches.Count);
            combined.AddRange(ftsHits);
            combined.AddRange(fuzzyMatches);
            return combined;
        }

        private static readonly char[] TokenSeparators = { ' ', '\t', '\n', '.', ',', ';', ':', '(', ')', '-', '/' };

        /// <summary>
        /// Score di similarità tra query e item. Per Description lunghe usiamo
        /// tokenizzazione per word (match max contro singole parole), così una query corta
        /// come "calcestrusso" contro "Calcestruzzo Rck 25 per fondazioni" non viene penalizzata
        /// dalla lunghezza globale.
        /// </summary>
        private static double Score(string query, PriceItem item)
        {
            return Math.Max(
                LevenshteinDistance.Similarity(query, item.Code),
                Math.Max(
                    ScoreTokenized(query, item.ShortDesc),
                    ScoreTokenized(query, item.Description)));
        }

        private static double ScoreTokenized(string query, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            // Similarity globale (utile per text corti tipo "mc" o ShortDesc)
            var globalScore = LevenshteinDistance.Similarity(query, text);

            // Similarity migliore contro token singoli (word), per evitare penalizzazione da testo lungo
            var tokens = text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
            double tokenMax = 0;
            foreach (var tok in tokens)
            {
                if (tok.Length < 3) continue; // skip noise (es. "25", "di", "a")
                var s = LevenshteinDistance.Similarity(query, tok);
                if (s > tokenMax) tokenMax = s;
            }

            return Math.Max(globalScore, tokenMax);
        }
    }

    /// <summary>Livello di ricerca effettivo usato per produrre i risultati.</summary>
    public enum SearchLevel
    {
        /// <summary>Nessun match trovato.</summary>
        None = 0,
        /// <summary>Match esatto per codice (L1).</summary>
        Exact = 1,
        /// <summary>Match FTS5 full-text (L2).</summary>
        FullText = 2,
        /// <summary>Match fuzzy Levenshtein (L3) — eventualmente combinato con FTS5 parziale.</summary>
        Fuzzy = 3,
    }

    /// <summary>Risultato immutabile di <see cref="PriceItemSearchService.Search"/>.</summary>
    public class SearchResult
    {
        public static readonly SearchResult Empty = new SearchResult(SearchLevel.None, Array.Empty<PriceItem>());

        public SearchResult(SearchLevel level, IReadOnlyList<PriceItem> items)
        {
            Level = level;
            Items = items ?? Array.Empty<PriceItem>();
        }

        public SearchLevel Level { get; }
        public IReadOnlyList<PriceItem> Items { get; }
        public int Count => Items.Count;
    }
}
