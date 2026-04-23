using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QtoRevitPlugin.AI.Ollama
{
    /// <summary>
    /// Provider di generazione testo che chiama Ollama in locale su
    /// <c>POST {baseUrl}/api/generate</c> con <c>stream = false</c>.
    ///
    /// <para>Usato per:</para>
    /// <list type="bullet">
    ///   <item>Generazione <c>ShortDesc</c> sintetica da descrizioni EP lunghe (§7.4)</item>
    ///   <item>Spiegazioni contestuali opzionali in UI</item>
    /// </list>
    ///
    /// <para>Modello raccomandato: <c>llama3.2:3b</c> (~2GB, gira su 8GB RAM senza GPU).</para>
    /// </summary>
    public sealed class OllamaTextModelProvider : ITextModelProvider, IDisposable
    {
        private readonly HttpClient _client;
        private readonly bool _ownsClient;

        public string ModelName { get; }

        public OllamaTextModelProvider(
            string baseUrl = "http://localhost:11434",
            string modelName = "llama3.2:3b")
            : this(CreateDefaultClient(baseUrl), modelName, ownsClient: true)
        {
        }

        public OllamaTextModelProvider(HttpClient client, string modelName, bool ownsClient = false)
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
                // Timeout più lungo perché LLM generation può essere lenta
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        /// <summary>
        /// Assumiamo disponibile se stesso host di embedding (check leggero).
        /// In pratica l'utente che ha Ollama per embedding ha anche LLM installati.
        /// Se il modello specifico non c'è, Ollama torna 404 su generate e catchamo lì.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var resp = _client.GetAsync("/api/tags", cts.Token).GetAwaiter().GetResult();
                    return resp.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<string> CompleteAsync(
            string prompt, int maxTokens = 100, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;

            var payload = new
            {
                model = ModelName,
                prompt = prompt,
                stream = false,
                options = new { num_predict = maxTokens }
            };
            var json = JsonSerializer.Serialize(payload);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _client.PostAsync("/api/generate", content, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("response", out var respElem))
                return string.Empty;

            return respElem.GetString()?.Trim() ?? string.Empty;
        }

        public void Dispose()
        {
            if (_ownsClient) _client.Dispose();
        }
    }
}
