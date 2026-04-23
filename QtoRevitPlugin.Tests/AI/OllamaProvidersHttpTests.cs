using FluentAssertions;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.AI.Ollama;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace QtoRevitPlugin.Tests.AI
{
    /// <summary>
    /// Test per i provider Ollama con HttpMessageHandler mock (no Ollama reale richiesto).
    /// Verifica: serializzazione JSON request, parsing JSON response, gestione errori HTTP.
    /// </summary>
    public class OllamaProvidersHttpTests
    {
        // ============================================================
        // OllamaEmbeddingProvider
        // ============================================================

        [Fact]
        public async Task EmbedAsync_ParsesValidResponse_ReturnsVector()
        {
            // Mock server che risponde con JSON valido di Ollama
            var handler = new MockHandler((req, ct) =>
            {
                req.RequestUri!.PathAndQuery.Should().Be("/api/embeddings");
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"embedding\":[0.1,0.2,0.3,-0.4]}",
                        Encoding.UTF8,
                        "application/json")
                };
                return Task.FromResult(resp);
            });

            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
            using var provider = new OllamaEmbeddingProvider(client, "test-model");

            var vec = await provider.EmbedAsync("qualche testo");

            vec.Should().Equal(0.1f, 0.2f, 0.3f, -0.4f);
            provider.VectorSize.Should().Be(4, "deve memorizzare la dimensione alla prima risposta");
        }

        [Fact]
        public async Task EmbedAsync_EmptyInput_ReturnsEmpty()
        {
            using var client = new HttpClient(new MockHandler((r, c) =>
                throw new InvalidOperationException("Non deve chiamare HTTP per input vuoto")));
            using var provider = new OllamaEmbeddingProvider(client, "test");

            var vec = await provider.EmbedAsync("");
            vec.Should().BeEmpty();
        }

        [Fact]
        public async Task EmbedAsync_HttpError_Throws()
        {
            var handler = new MockHandler((r, c) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)));

            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
            using var provider = new OllamaEmbeddingProvider(client, "test");

            var act = async () => await provider.EmbedAsync("text");
            await act.Should().ThrowAsync<HttpRequestException>();
        }

        [Fact]
        public async Task EmbedBatch_IteratesAndCombinesResults()
        {
            int callCount = 0;
            var handler = new MockHandler((r, c) =>
            {
                callCount++;
                // InvariantCulture per JSON (evita virgola decimale italiana su CultureInfo.CurrentCulture).
                var value = (callCount * 0.1).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"embedding\":[{value}]}}",
                        Encoding.UTF8, "application/json")
                };
                return Task.FromResult(resp);
            });

            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
            using var provider = new OllamaEmbeddingProvider(client, "test");

            var batch = await provider.EmbedBatchAsync(new[] { "a", "b", "c" });

            batch.Should().HaveCount(3);
            callCount.Should().Be(3);
        }

        [Fact]
        public async Task EmbedAsync_RespectsCancellation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var handler = new MockHandler((r, c) =>
            {
                c.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
            using var provider = new OllamaEmbeddingProvider(client, "test");

            var act = async () => await provider.EmbedAsync("text", cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // ============================================================
        // OllamaTextModelProvider
        // ============================================================

        [Fact]
        public async Task CompleteAsync_ParsesResponseField()
        {
            var handler = new MockHandler((r, c) =>
            {
                r.RequestUri!.PathAndQuery.Should().Be("/api/generate");
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"response\":\"Muratura laterizio portante classe C25/30\",\"done\":true}",
                        Encoding.UTF8, "application/json")
                };
                return Task.FromResult(resp);
            });

            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
            using var provider = new OllamaTextModelProvider(client, "test-llm");

            var text = await provider.CompleteAsync("riassumi questa voce");
            text.Should().Be("Muratura laterizio portante classe C25/30");
        }

        [Fact]
        public async Task CompleteAsync_EmptyPrompt_ReturnsEmpty()
        {
            using var client = new HttpClient(new MockHandler((r, c) =>
                throw new InvalidOperationException("Non deve chiamare HTTP per prompt vuoto")));
            using var provider = new OllamaTextModelProvider(client, "test");

            var r = await provider.CompleteAsync("");
            r.Should().BeEmpty();
        }

        [Fact]
        public async Task CompleteAsync_MissingResponseField_ReturnsEmpty()
        {
            var handler = new MockHandler((r, c) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"done\":true}", Encoding.UTF8, "application/json")
                }));

            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
            using var provider = new OllamaTextModelProvider(client, "test");

            var r = await provider.CompleteAsync("prompt");
            r.Should().BeEmpty();
        }

        // ============================================================
        // OllamaAiProvider — helper interni testabili
        // ============================================================

        [Theory]
        [InlineData("Muratura. ", "Muratura")]
        [InlineData("\"quota centimetrica\"", "quota centimetrica")]
        [InlineData("Rinforzo con CFRP;;", "Rinforzo con CFRP")]
        [InlineData("  trim spaces  ", "trim spaces")]
        [InlineData("", "")]
        public void SanitizeShortDesc_RemovesQuotesAndTrailingPunctuation(string input, string expected)
        {
            OllamaAiProvider.SanitizeShortDesc(input).Should().Be(expected);
        }

        [Fact]
        public void SanitizeShortDesc_TruncatesLongInput()
        {
            var veryLong = new string('x', 500);
            var result = OllamaAiProvider.SanitizeShortDesc(veryLong);
            result.Length.Should().BeLessOrEqualTo(200);
        }

        // ============================================================
        // Mock HttpMessageHandler helper
        // ============================================================

        private sealed class MockHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;
            public MockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) => _fn = fn;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => _fn(request, cancellationToken);
        }
    }
}
