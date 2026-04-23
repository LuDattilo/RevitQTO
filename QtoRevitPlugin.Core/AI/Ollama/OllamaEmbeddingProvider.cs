using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QtoRevitPlugin.AI.Ollama
{
    /// <summary>
    /// Provider embedding che chiama Ollama in locale su
    /// <c>POST {baseUrl}/api/embeddings</c>.
    ///
    /// <para>Prerequisiti utente (QTO-AI-Integration.md §5.1):</para>
    /// <list type="bullet">
    ///   <item>Ollama installato (https://ollama.com/download)</item>
    ///   <item>Modello scaricato: <c>ollama pull nomic-embed-text</c></item>
    /// </list>
    ///
    /// <para><b>Inezione HttpClient</b>: il costruttore accetta un <see cref="HttpClient"/>
    /// opzionale per testabilità (mock con <see cref="HttpMessageHandler"/> custom).
    /// Il default istanzia un HttpClient nuovo con BaseAddress impostato.</para>
    /// </summary>
    public sealed class OllamaEmbeddingProvider : IEmbeddingProvider, IDisposable
    {
        private readonly HttpClient _client;
        private readonly bool _ownsClient;
        private int _vectorSize; // 0 finché non arriva la prima risposta

        public string ModelName { get; }
        public int VectorSize => _vectorSize;

        /// <summary>
        /// Costruttore standard: crea un HttpClient interno con BaseAddress = baseUrl
        /// e timeout 30s (lungo per lasciare spazio a Ollama che può caricare il modello
        /// la prima volta).
        /// </summary>
        public OllamaEmbeddingProvider(
            string baseUrl = "http://localhost:11434",
            string modelName = "nomic-embed-text")
            : this(CreateDefaultClient(baseUrl), modelName, ownsClient: true)
        {
        }

        /// <summary>
        /// Costruttore per test: riceve un HttpClient già configurato (possibilmente con
        /// <see cref="HttpMessageHandler"/> mock). Non dispose il client in questo caso.
        /// </summary>
        public OllamaEmbeddingProvider(HttpClient client, string modelName, bool ownsClient = false)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _ownsClient = ownsClient;
            ModelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        }

        private static HttpClient CreateDefaultClient(string baseUrl)
        {
            return new HttpClient
            {
                BaseAddress = new Uri(baseUrl ?? "http://localhost:11434"),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// True se Ollama risponde sul /api/tags entro 2s. Check leggero per probe
        /// all'avvio del plugin. Non verifica che il modello sia disponibile.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    // Sync wait: questo è un quick probe, va bene bloccare per 2s max.
                    // Se si usa in UI thread meglio chiamare IsAvailableAsync dedicato.
                    var resp = _client.GetAsync("/api/tags", cts.Token).GetAwaiter().GetResult();
                    return resp.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<float>();

            var payload = new { model = ModelName, prompt = text };
            var json = JsonSerializer.Serialize(payload);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _client.PostAsync("/api/embeddings", content, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var arr = doc.RootElement.GetProperty("embedding");

            var vec = new float[arr.GetArrayLength()];
            int i = 0;
            foreach (var el in arr.EnumerateArray())
                vec[i++] = el.GetSingle();

            // Memorizza la dimensione al primo vettore per successive validazioni
            if (_vectorSize == 0) _vectorSize = vec.Length;

            return vec;
        }

        public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            if (texts == null || texts.Count == 0) return new List<float[]>(0);

            var results = new List<float[]>(texts.Count);
            foreach (var t in texts)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await EmbedAsync(t, ct).ConfigureAwait(false));
            }
            return results;
        }

        public void Dispose()
        {
            if (_ownsClient) _client.Dispose();
        }
    }
}
