using System;

namespace QtoRevitPlugin.AI
{
    /// <summary>
    /// Record di cache embedding per una voce di listino, legato a un modello specifico.
    /// Persistito nella tabella <c>EmbeddingCache</c> (schema v8+).
    ///
    /// <para>Serializzazione vettoriale: <c>float[]</c> → <c>byte[]</c> via
    /// <see cref="EmbeddingSerializer"/>. Endianness little-endian (standard su x86/x64).</para>
    /// </summary>
    public class EmbeddingEntry
    {
        public int Id { get; set; }
        public int PriceItemId { get; set; }

        /// <summary>Nome del modello embedding usato (es. "nomic-embed-text").
        /// Salvato per permettere invalidazione selettiva quando l'utente cambia modello.</summary>
        public string ModelName { get; set; } = "";

        /// <summary>Vettore serializzato come blob binario. Usare
        /// <see cref="EmbeddingSerializer.Deserialize"/> per ottenere <c>float[]</c>.</summary>
        public byte[] VectorBlob { get; set; } = System.Array.Empty<byte>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Serializzazione/deserializzazione vettori <c>float[]</c> ↔ <c>byte[]</c>.
    /// Usa <c>Buffer.BlockCopy</c> per efficienza (no LINQ, no BitConverter per elemento).
    /// </summary>
    public static class EmbeddingSerializer
    {
        /// <summary>Converte <c>float[]</c> in <c>byte[]</c> (4 byte per float, little-endian).</summary>
        public static byte[] Serialize(float[] vector)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            var bytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>Converte <c>byte[]</c> in <c>float[]</c>. Lunghezza attesa multipla di 4.</summary>
        public static float[] Deserialize(byte[] blob)
        {
            if (blob == null) throw new ArgumentNullException(nameof(blob));
            if (blob.Length % sizeof(float) != 0)
                throw new ArgumentException(
                    $"Lunghezza blob {blob.Length} non multipla di sizeof(float)=4. " +
                    "Probabile corruzione.", nameof(blob));

            var vector = new float[blob.Length / sizeof(float)];
            Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
            return vector;
        }
    }
}
