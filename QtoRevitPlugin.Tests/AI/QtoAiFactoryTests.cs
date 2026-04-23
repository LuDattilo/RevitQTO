using FluentAssertions;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.AI
{
    /// <summary>
    /// Test della factory che seleziona tra NullAiProvider e OllamaAiProvider.
    /// Non testa il path "Ollama disponibile" perché richiede Ollama reale in esecuzione;
    /// testa il path di fallback (AI disabilitata o Ollama offline → Null).
    /// </summary>
    public class QtoAiFactoryTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public QtoAiFactoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"factory_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void Create_AiDisabled_ReturnsNullProvider()
        {
            var settings = new CmeSettings { AiEnabled = false };
            var provider = QtoAiFactory.Create(settings, _repo);

            provider.Should().BeSameAs(NullAiProvider.Instance);
            provider.IsAvailable.Should().BeFalse();
        }

        [Fact]
        public void Create_OllamaUnreachable_ReturnsNullProviderWithLogWarning()
        {
            // URL improbabile che garantisce fail del probe IsAvailable
            var settings = new CmeSettings
            {
                AiEnabled = true,
                OllamaBaseUrl = "http://localhost:9999", // porta random non in uso
                EmbeddingModel = "nomic-embed-text"
            };

            string? warningLogged = null;
            var provider = QtoAiFactory.Create(settings, _repo,
                logger: msg => warningLogged = msg);

            provider.Should().BeSameAs(NullAiProvider.Instance);
            warningLogged.Should().NotBeNullOrEmpty();
            warningLogged.Should().Contain("Ollama non raggiungibile");
        }

        [Fact]
        public void Create_NullSettings_Throws()
        {
            var act = () => QtoAiFactory.Create(null!, _repo);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Create_NullRepo_Throws()
        {
            var settings = new CmeSettings { AiEnabled = true };
            var act = () => QtoAiFactory.Create(settings, null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Create_NullLogger_WorksSilently()
        {
            // logger è Action<string>? — null va bene, non deve throw
            var settings = new CmeSettings { AiEnabled = false };
            var provider = QtoAiFactory.Create(settings, _repo, logger: null);
            provider.Should().BeSameAs(NullAiProvider.Instance);
        }
    }
}
