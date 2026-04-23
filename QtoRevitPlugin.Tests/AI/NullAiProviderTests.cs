using FluentAssertions;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace QtoRevitPlugin.Tests.AI
{
    /// <summary>
    /// Test per il fallback no-op. Verifica che sia sempre ritornabile empty/empty
    /// string senza throw anche con input nulli/limite.
    /// </summary>
    public class NullAiProviderTests
    {
        [Fact]
        public void IsAvailable_IsFalse()
        {
            NullAiProvider.Instance.IsAvailable.Should().BeFalse();
            NullEmbeddingProvider.Instance.IsAvailable.Should().BeFalse();
            NullTextModelProvider.Instance.IsAvailable.Should().BeFalse();
        }

        [Fact]
        public async Task SuggestEp_ReturnsEmpty()
        {
            var r = await NullAiProvider.Instance.SuggestEpAsync("Muro base", "Muri");
            r.Should().BeEmpty();
        }

        [Fact]
        public async Task FindSemanticMismatches_ReturnsEmpty()
        {
            var assignments = new List<QtoAssignment>
            {
                new QtoAssignment { UniqueId = "x", EpCode = "EP1", Category = "Muri" }
            };
            var r = await NullAiProvider.Instance.FindSemanticMismatchesAsync(assignments);
            r.Should().BeEmpty();
        }

        [Fact]
        public async Task SummarizeDescription_ReturnsEmptyString()
        {
            var r = await NullAiProvider.Instance.SummarizeDescriptionAsync(
                "Descrizione lunga di esempio con molte parole tecniche");
            r.Should().BeEmpty();
        }

        [Fact]
        public async Task SemanticSearch_ReturnsEmpty()
        {
            var r = await NullAiProvider.Instance.SemanticSearchAsync(
                "muratura",
                new List<int> { 1, 2, 3 });
            r.Should().BeEmpty();
        }

        [Fact]
        public async Task EmbeddingProvider_EmbedAsync_ReturnsEmpty()
        {
            var r = await NullEmbeddingProvider.Instance.EmbedAsync("test");
            r.Should().BeEmpty();
        }

        [Fact]
        public async Task TextModelProvider_CompleteAsync_ReturnsEmptyString()
        {
            var r = await NullTextModelProvider.Instance.CompleteAsync("ciao");
            r.Should().BeEmpty();
        }

        [Fact]
        public void Instance_IsSingleton()
        {
            // Le implementazioni no-op non hanno stato → conviene singleton
            NullAiProvider.Instance.Should().BeSameAs(NullAiProvider.Instance);
            NullEmbeddingProvider.Instance.Should().BeSameAs(NullEmbeddingProvider.Instance);
            NullTextModelProvider.Instance.Should().BeSameAs(NullTextModelProvider.Instance);
        }
    }
}
