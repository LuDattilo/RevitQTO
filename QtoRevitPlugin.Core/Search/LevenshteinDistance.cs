using System;

namespace QtoRevitPlugin.Search
{
    /// <summary>
    /// Distanza di edit Levenshtein tra due stringhe — usata dal fuzzy fallback
    /// della ricerca voci di listino (livello 3, dopo match esatto + FTS5).
    ///
    /// Implementazione DP a due righe (memoria O(min(|a|,|b|))) con short-circuit
    /// su lunghezze molto diverse: se |len(a) - len(b)| &gt; maxDistance, ritorna maxDistance+1.
    ///
    /// Non usa librerie esterne.
    /// </summary>
    public static class LevenshteinDistance
    {
        /// <summary>
        /// Distanza di edit (substitutions, insertions, deletions).
        /// Case-insensitive (entrambe le stringhe sono lowercased internamente).
        /// </summary>
        /// <param name="a">Prima stringa</param>
        /// <param name="b">Seconda stringa</param>
        /// <returns>Numero minimo di edit per trasformare a in b</returns>
        public static int Compute(string? a, string? b)
        {
            a ??= string.Empty;
            b ??= string.Empty;

            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();

            // Garantiamo a come quella più lunga per ridurre memoria (colonne = len più corto)
            if (a.Length < b.Length)
                (a, b) = (b, a);

            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++) prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }

            return prev[b.Length];
        }

        /// <summary>
        /// Similarità normalizzata [0.0, 1.0] basata su 1 - (distance / maxLen).
        /// 1.0 = stringhe identiche, 0.0 = completamente diverse.
        /// </summary>
        public static double Similarity(string? a, string? b)
        {
            a ??= string.Empty;
            b ??= string.Empty;
            if (a.Length == 0 && b.Length == 0) return 1.0;

            var maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1.0;

            var dist = Compute(a, b);
            return 1.0 - (double)dist / maxLen;
        }
    }
}
