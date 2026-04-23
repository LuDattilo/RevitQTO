using FluentAssertions;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.AI
{
    /// <summary>
    /// Test per i nuovi metodi repo <c>HasEmbedding</c>, <c>UpsertEmbedding</c>,
    /// <c>GetEmbeddings</c>, <c>DeleteEmbeddingsForModel</c>,
    /// <c>DeleteEmbeddingsForPriceList</c>. La tabella <c>EmbeddingCache</c>
    /// è già nello schema dal v8; questi sono i primi CRUD.
    /// </summary>
    public class EmbeddingCacheRepositoryTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _priceListId;

        public EmbeddingCacheRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cme_ai_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);

            // Setup: un listino con 3 voci, così possiamo salvare i loro embedding
            var pl = new PriceList { Name = "TestList", IsActive = true };
            _repo.InsertPriceList(pl);
            _priceListId = pl.Id;
            _repo.InsertPriceItemsBatch(pl.Id, new[]
            {
                new PriceItem { Code = "A1", Description = "Muro",      Unit = "m²", UnitPrice = 10 },
                new PriceItem { Code = "A2", Description = "Pavimento", Unit = "m²", UnitPrice = 20 },
                new PriceItem { Code = "A3", Description = "Soffitto",  Unit = "m²", UnitPrice = 30 },
            });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void HasEmbedding_EmptyDb_ReturnsFalse()
        {
            _repo.HasEmbedding(1, "nomic-embed-text").Should().BeFalse();
        }

        [Fact]
        public void Upsert_InsertsThenIsDetected()
        {
            var vec = new float[] { 0.1f, 0.2f, 0.3f };
            var blob = EmbeddingSerializer.Serialize(vec);

            _repo.UpsertEmbedding(1, "nomic-embed-text", blob);
            _repo.HasEmbedding(1, "nomic-embed-text").Should().BeTrue();
            _repo.HasEmbedding(1, "other-model").Should().BeFalse();
        }

        [Fact]
        public void Upsert_ReplacesExistingForSamePriceItemAndModel()
        {
            var v1 = EmbeddingSerializer.Serialize(new[] { 1f, 2f, 3f });
            var v2 = EmbeddingSerializer.Serialize(new[] { 4f, 5f, 6f });

            _repo.UpsertEmbedding(1, "m1", v1);
            _repo.UpsertEmbedding(1, "m1", v2);

            var results = _repo.GetEmbeddings(new[] { 1 }, "m1");
            results.Should().ContainSingle("UNIQUE(PriceItemId, ModelName) deve impedire duplicati");
            EmbeddingSerializer.Deserialize(results[0].VectorBlob)
                .Should().BeEquivalentTo(new[] { 4f, 5f, 6f });
        }

        [Fact]
        public void Upsert_DifferentModelsForSameItem_KeptSeparate()
        {
            var v1 = EmbeddingSerializer.Serialize(new[] { 1f });
            var v2 = EmbeddingSerializer.Serialize(new[] { 2f });

            _repo.UpsertEmbedding(1, "nomic-embed-text", v1);
            _repo.UpsertEmbedding(1, "mxbai-embed-large", v2);

            _repo.HasEmbedding(1, "nomic-embed-text").Should().BeTrue();
            _repo.HasEmbedding(1, "mxbai-embed-large").Should().BeTrue();

            _repo.GetEmbeddings(new[] { 1 }, "nomic-embed-text").Should().ContainSingle();
            _repo.GetEmbeddings(new[] { 1 }, "mxbai-embed-large").Should().ContainSingle();
        }

        [Fact]
        public void GetEmbeddings_FiltersByModel()
        {
            _repo.UpsertEmbedding(1, "m1", EmbeddingSerializer.Serialize(new[] { 1f }));
            _repo.UpsertEmbedding(2, "m1", EmbeddingSerializer.Serialize(new[] { 2f }));
            _repo.UpsertEmbedding(3, "m2", EmbeddingSerializer.Serialize(new[] { 3f }));

            var m1Results = _repo.GetEmbeddings(new[] { 1, 2, 3 }, "m1");
            m1Results.Should().HaveCount(2);
            m1Results.All(r => r.ModelName == "m1").Should().BeTrue();
        }

        [Fact]
        public void GetEmbeddings_EmptyIdList_ReturnsEmpty()
        {
            _repo.UpsertEmbedding(1, "m1", EmbeddingSerializer.Serialize(new[] { 1f }));
            _repo.GetEmbeddings(new List<int>(), "m1").Should().BeEmpty();
        }

        [Fact]
        public void DeleteEmbeddingsForModel_RemovesOnlyMatchingModel()
        {
            _repo.UpsertEmbedding(1, "m1", EmbeddingSerializer.Serialize(new[] { 1f }));
            _repo.UpsertEmbedding(2, "m1", EmbeddingSerializer.Serialize(new[] { 2f }));
            _repo.UpsertEmbedding(3, "m2", EmbeddingSerializer.Serialize(new[] { 3f }));

            var deleted = _repo.DeleteEmbeddingsForModel("m1");
            deleted.Should().Be(2);

            _repo.HasEmbedding(1, "m1").Should().BeFalse();
            _repo.HasEmbedding(2, "m1").Should().BeFalse();
            _repo.HasEmbedding(3, "m2").Should().BeTrue();
        }

        [Fact]
        public void DeleteEmbeddingsForModel_UnknownModel_ZeroRemoved()
        {
            _repo.UpsertEmbedding(1, "m1", EmbeddingSerializer.Serialize(new[] { 1f }));
            _repo.DeleteEmbeddingsForModel("unknown-model").Should().Be(0);
        }

        [Fact]
        public void DeleteEmbeddingsForPriceList_CascadesViaJoin()
        {
            _repo.UpsertEmbedding(1, "m1", EmbeddingSerializer.Serialize(new[] { 1f }));
            _repo.UpsertEmbedding(2, "m1", EmbeddingSerializer.Serialize(new[] { 2f }));
            _repo.UpsertEmbedding(3, "m1", EmbeddingSerializer.Serialize(new[] { 3f }));

            var deleted = _repo.DeleteEmbeddingsForPriceList(_priceListId);
            deleted.Should().Be(3, "tutti i price item del listino hanno embedding → tutti rimossi");

            _repo.HasEmbedding(1, "m1").Should().BeFalse();
            _repo.HasEmbedding(2, "m1").Should().BeFalse();
            _repo.HasEmbedding(3, "m1").Should().BeFalse();
        }

        [Fact]
        public void UpsertEmbedding_EmptyBlob_Throws()
        {
            var act = () => _repo.UpsertEmbedding(1, "m1", Array.Empty<byte>());
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void UpsertEmbedding_NullBlob_Throws()
        {
            var act = () => _repo.UpsertEmbedding(1, "m1", null!);
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
