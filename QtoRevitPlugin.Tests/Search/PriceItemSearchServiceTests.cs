using System;
using System.IO;
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Search;
using Xunit;

namespace QtoRevitPlugin.Tests.Search
{
    /// <summary>
    /// Test integration: usa DB SQLite temporaneo (file per test) + QtoRepository reale.
    /// Verifica il flow a 3 livelli (Exact → FTS5 → Fuzzy) con dataset seed realistico.
    /// </summary>
    public class PriceItemSearchServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly PriceItemSearchService _service;

        public PriceItemSearchServiceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"qto-test-{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _service = new PriceItemSearchService(_repo);

            SeedListinoDiTest();
        }

        public void Dispose()
        {
            _repo.Dispose();
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }

        // ---------------------------------------------------------------------
        // L1 — Match esatto per codice
        // ---------------------------------------------------------------------

        [Fact]
        public void Search_CodiceEsatto_RitornaLivelloExact()
        {
            var result = _service.Search("A.01.001");

            result.Level.Should().Be(SearchLevel.Exact);
            result.Count.Should().Be(1);
            result.Items[0].Code.Should().Be("A.01.001");
        }

        [Fact]
        public void Search_CodiceCaseInsensitive_TrovaComunque()
        {
            var result = _service.Search("a.01.001");

            result.Level.Should().Be(SearchLevel.Exact);
            result.Items.Should().ContainSingle();
        }

        // ---------------------------------------------------------------------
        // L2 — Full-text search FTS5
        // ---------------------------------------------------------------------

        [Fact]
        public void Search_ParolaChiaveUnivoca_RitornaLivelloFullText()
        {
            // "calcestruzzo" è in 2 voci (B.01.001 e B.01.002) → ≥ 3 risultati non raggiunti, ma ≥ 1 sì
            // Per avere ≥ MinFtsResultsToSkipFuzzy servono 3+ hit → parola presente in 3+ desc
            var result = _service.Search("scavo");

            // 3 voci contengono "Scavo" → L2 skippa fuzzy
            result.Level.Should().Be(SearchLevel.FullText);
            result.Count.Should().BeGreaterOrEqualTo(3);
        }

        [Fact]
        public void Search_DueParole_ProduceMatchAndImplicito()
        {
            // "scavo obbligata" deve trovare "Scavo a sezione obbligata" (AND implicito FTS5)
            var result = _service.Search("scavo obbligata");

            result.Items.Should().Contain(i => i.Code == "A.01.002");
        }

        // ---------------------------------------------------------------------
        // L3 — Fuzzy Levenshtein
        // ---------------------------------------------------------------------

        [Fact]
        public void Search_TypoNelQuery_FuzzyTrovaComunque()
        {
            // "calcestrusso" (typo, -zzo invece di -sso) → FTS5 non matcha, fuzzy sì
            var result = _service.Search("calcestrusso", fuzzyThreshold: 0.7);

            result.Level.Should().Be(SearchLevel.Fuzzy);
            result.Items.Should().Contain(i => i.Code == "B.01.001");
        }

        [Fact]
        public void Search_ParolaCompletamenteDiversa_ZeroRisultati()
        {
            var result = _service.Search("inesistente-zzz", fuzzyThreshold: 0.8);

            result.Count.Should().Be(0);
        }

        // ---------------------------------------------------------------------
        // Edge cases
        // ---------------------------------------------------------------------

        [Fact]
        public void Search_QueryVuota_RitornaEmpty()
        {
            var result = _service.Search("");

            result.Should().BeSameAs(SearchResult.Empty);
            result.Count.Should().Be(0);
        }

        [Fact]
        public void Search_QueryNullOSpazi_RitornaEmpty()
        {
            _service.Search("   ").Count.Should().Be(0);
        }

        [Fact]
        public void Search_InvalidateCache_RiLeggeDaDb()
        {
            // Prima ricerca fuzzy popola cache
            _service.Search("calcestrusso");

            // Aggiungi una voce nuova
            var newList = _repo.InsertPriceList(new PriceList { Name = "Aggiunto", Priority = 5 });
            _repo.InsertPriceItemsBatch(newList, new[]
            {
                new PriceItem { Code = "Z.99.999", Description = "Voce aggiunta dopo cache" }
            });

            // Invalida + ricerca → deve includere la nuova voce
            _service.InvalidateCache();
            var result = _service.Search("aggiunta");

            result.Items.Should().Contain(i => i.Code == "Z.99.999");
        }

        // ---------------------------------------------------------------------
        // Seed dataset
        // ---------------------------------------------------------------------

        private void SeedListinoDiTest()
        {
            var listId = _repo.InsertPriceList(new PriceList
            {
                Name = "Listino Test",
                Source = "MANUAL",
                Priority = 0,
                IsActive = true
            });

            _repo.InsertPriceItemsBatch(listId, new[]
            {
                new PriceItem { Code = "A.01.001", Description = "Scavo di sbancamento", Unit = "mc", UnitPrice = 12.50, SuperChapter = "A", Chapter = "A.01" },
                new PriceItem { Code = "A.01.002", Description = "Scavo a sezione obbligata per fondazioni", Unit = "mc", UnitPrice = 18.30, SuperChapter = "A", Chapter = "A.01" },
                new PriceItem { Code = "A.01.003", Description = "Scavo in roccia con martellone", Unit = "mc", UnitPrice = 28.50, SuperChapter = "A", Chapter = "A.01" },
                new PriceItem { Code = "B.01.001", Description = "Calcestruzzo Rck 25 per fondazioni", Unit = "mc", UnitPrice = 120.00, SuperChapter = "B", Chapter = "B.01" },
                new PriceItem { Code = "B.01.002", Description = "Calcestruzzo Rck 30 per strutture", Unit = "mc", UnitPrice = 135.00, SuperChapter = "B", Chapter = "B.01" },
                new PriceItem { Code = "C.01.001", Description = "Muratura in blocchi laterizio forato", Unit = "mq", UnitPrice = 45.00, SuperChapter = "C", Chapter = "C.01" },
                new PriceItem { Code = "C.02.001", Description = "Intonaco civile premiscelato", Unit = "mq", UnitPrice = 22.00, SuperChapter = "C", Chapter = "C.02" }
            });
        }
    }
}
