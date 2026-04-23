using System;

namespace QtoRevitPlugin.AI
{
    /// <summary>
    /// Utility per similarità coseno tra vettori di embedding. Static, thread-safe.
    /// </summary>
    public static class CosineSimilarity
    {
        /// <summary>
        /// Cosine similarity in [-1, 1]. Per embedding normalizzati è in [0, 1].
        /// Valore più alto = più simili. Ritorna 0 se i vettori hanno lunghezze
        /// diverse o se uno dei due è nullo/vuoto.
        /// </summary>
        public static float Compute(float[] a, float[] b)
        {
            if (a == null || b == null) return 0f;
            if (a.Length == 0 || b.Length == 0) return 0f;
            if (a.Length != b.Length) return 0f;

            float dot = 0, magA = 0, magB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            // Epsilon per evitare divisione per zero su vettori nulli
            return dot / ((float)Math.Sqrt(magA) * (float)Math.Sqrt(magB) + 1e-8f);
        }
    }
}
