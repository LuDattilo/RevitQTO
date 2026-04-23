using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using Xunit;

namespace QtoRevitPlugin.Tests.SprintUi7
{
    /// <summary>
    /// Test del gateway di alto livello <see cref="AiSuggestionsGateway"/>.
    /// Verifica il contratto "graceful degradation": sempre lista vuota
    /// mai throw mai null quando AI non disponibile.
    ///
    /// Non testa l'happy-path con Ollama reale (sarebbe un test di integrazione
    /// che richiede il servizio locale) — quello resta manuale.
    /// </summary>
    public class AiSuggestionsGatewayTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public AiSuggestionsGatewayTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ai_gw_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _repo.InsertSession(new WorkSession
            {
                ProjectPath = "test.rvt",
                SessionName = "t",
                CreatedAt = DateTime.UtcNow
            });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public async Task GetSuggestionsAsync_AiDisabled_ReturnsEmpty()
        {
            var settings = new CmeSettings { AiEnabled = false };

            var result = await AiSuggestionsGateway.GetSuggestionsAsync(
                settings, _repo, "Muro base", "Walls");

            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetSuggestionsAsync_NullSettings_ReturnsEmpty()
        {
            var result = await AiSuggestionsGateway.GetSuggestionsAsync(
                settings: null!, repo: _repo, familyName: "X", category: "Y");

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetSuggestionsAsync_NullRepo_ReturnsEmpty()
        {
            var settings = new CmeSettings { AiEnabled = true };

            var result = await AiSuggestionsGateway.GetSuggestionsAsync(
                settings, repo: null!, familyName: "X", category: "Y");

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetSuggestionsAsync_BothFamilyAndCategoryEmpty_ReturnsEmpty()
        {
            var settings = new CmeSettings { AiEnabled = true };

            var result = await AiSuggestionsGateway.GetSuggestionsAsync(
                settings, _repo, familyName: "", category: "   ");

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetSuggestionsAsync_AiEnabledButOllamaUnreachable_ReturnsEmpty()
        {
            // AI enabled + URL puntato a host non-routable (TEST-NET-2 RFC 5737).
            // Il factory proverà /api/tags → timeout/errore → NullAiProvider
            // fallback → IsAvailable=false → lista vuota.
            var settings = new CmeSettings
            {
                AiEnabled = true,
                OllamaBaseUrl = "http://198.51.100.1:11434",
                EmbeddingModel = "test-model"
            };

            var result = await AiSuggestionsGateway.GetSuggestionsAsync(
                settings, _repo, familyName: "Muro", category: "Walls",
                timeoutMs: 500); // 500ms cap per test rapido

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetSuggestionsAsync_NeverThrows_EvenOnWeirdInputs()
        {
            // Fuzzy robustness: qualunque input passato, non deve throwear.
            var settings = new CmeSettings { AiEnabled = false };

            var act1 = async () => await AiSuggestionsGateway.GetSuggestionsAsync(
                settings, _repo, familyName: "🤖 strano", category: "μ²", topN: 0);
            var act2 = async () => await AiSuggestionsGateway.GetSuggestionsAsync(
                settings, _repo, familyName: "x", category: "y", topN: -5);
            var act3 = async () => await AiSuggestionsGateway.GetSuggestionsAsync(
                settings, _repo, familyName: new string('a', 1000), category: "Y");

            await act1.Should().NotThrowAsync();
            await act2.Should().NotThrowAsync();
            await act3.Should().NotThrowAsync();
        }
    }
}
