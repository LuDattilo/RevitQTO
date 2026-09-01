using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Formula;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Parsers;
using QtoRevitPlugin.Search;
using QtoRevitPlugin.Xpwe;
using Xunit;
using Xunit.Abstractions;

namespace QtoRevitPlugin.Tests.StressTests
{
    public class StressTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _tempDbPath;
        private readonly QtoRepository _repo;

        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(StressTests).Assembly.Location)!,
                "..", "..", "..", ".."));

        public StressTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDbPath = Path.Combine(Path.GetTempPath(), $"qto_stress_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_tempDbPath);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_tempDbPath))
            {
                try { File.Delete(_tempDbPath); } catch { }
            }
        }

        // ================================================================
        // 1. DcfParser — parse Firenze-2025.xml (70MB, ~50k items)
        // ================================================================
        [Fact]
        public void Stress_DcfParser_Firenze2025_70MB()
        {
            var path = Path.Combine(RepoRoot, "Firenze-2025.xml");
            if (!File.Exists(path))
            {
                _output.WriteLine("SKIP: Firenze-2025.xml non trovato");
                return;
            }

            var fi = new FileInfo(path);
            _output.WriteLine($"File: {fi.Name}, Size: {fi.Length / 1024 / 1024} MB");

            var parser = new DcfParser();
            var sw = Stopwatch.StartNew();

            var result = parser.Parse(path);

            sw.Stop();
            _output.WriteLine($"Parse time: {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"Items parsed: {result.Items.Count}");
            _output.WriteLine($"TotalRowsDetected: {result.TotalRowsDetected}");
            _output.WriteLine($"Warnings: {result.Warnings.Count}");
            _output.WriteLine($"Memory: {GC.GetTotalMemory(true) / 1024 / 1024} MB");

            result.Items.Count.Should().BeGreaterThan(1000, "deve parsare almeno 1000 voci dal Firenze-2025");
            result.TotalRowsDetected.Should().Be(result.Items.Count);
        }

        // ================================================================
        // 2. Xpwe roundtrip — CME_Sample.xpwe (1MB, con RGItem)
        // ================================================================
        [Fact]
        public void Stress_XpweRoundtrip_CMESample()
        {
            var path = Path.Combine(RepoRoot, "CME_Sample.xpwe");
            if (!File.Exists(path))
            {
                _output.WriteLine("SKIP: CME_Sample.xpwe non trovato");
                return;
            }

            var fi = new FileInfo(path);
            _output.WriteLine($"File: {fi.Name}, Size: {fi.Length / 1024} KB");

            var parser = new XpweDeserializer();
            var serializer = new XpweSerializer();
            const string primusPI = "<?mso-application progid=\"PriMus.Document.XPWE\"?>";

            // Parse
            var sw = Stopwatch.StartNew();
            var result1 = parser.ParseFile(path);
            sw.Stop();
            _output.WriteLine($"Parse time: {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"PriceItems: {result1.PriceItems.Count}");
            _output.WriteLine($"MeasurementRows: {result1.MeasurementRows.Count}");
            _output.WriteLine($"Total SubRows (RGItem): {result1.MeasurementRows.Sum(m => m.SubRows.Count)}");
            _output.WriteLine($"Warnings: {result1.Warnings.Count}");

            // Serialize
            sw.Restart();
            var xml = serializer.SaveToString(result1);
            sw.Stop();
            _output.WriteLine($"Serialize time: {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"Output XML size: {xml.Length / 1024} KB");

            // Re-parse
            var cleanXml = xml.Replace(primusPI, "");
            sw.Restart();
            var result2 = parser.ParseString(cleanXml);
            sw.Stop();
            _output.WriteLine($"Re-parse time: {sw.ElapsedMilliseconds} ms");

            // Verify roundtrip
            result2.Document.TipoDocumento.Should().Be(result1.Document.TipoDocumento);
            result2.SuperCapitoli.Count.Should().Be(result1.SuperCapitoli.Count);
            result2.Capitoli.Count.Should().Be(result1.Capitoli.Count);
            result2.SubCapitoli.Count.Should().Be(result1.SubCapitoli.Count);
            result2.SuperCategorie.Count.Should().Be(result1.SuperCategorie.Count);
            result2.Categorie.Count.Should().Be(result1.Categorie.Count);
            result2.SubCategorie.Count.Should().Be(result1.SubCategorie.Count);
            result2.PriceItems.Count.Should().Be(result1.PriceItems.Count);
            result2.MeasurementRows.Count.Should().Be(result1.MeasurementRows.Count);

            int rgTotal1 = 0, rgTotal2 = 0;
            foreach (var m in result1.MeasurementRows) rgTotal1 += m.SubRows.Count;
            foreach (var m in result2.MeasurementRows) rgTotal2 += m.SubRows.Count;
            rgTotal2.Should().Be(rgTotal1, "tutti gli RGItem devono sopravvivere al roundtrip");

            _output.WriteLine("Roundtrip OK: tutti i count preservati");
        }

        // ================================================================
        // 3. SQLite batch insert — 100k PriceItems
        // ================================================================
        [Fact]
        public void Stress_Sqlite_BatchInsert_100k()
        {
            const int itemCount = 100_000;
            const int batchSize = 500;

            // Insert price list
            var priceList = new PriceList
            {
                Name = "StressTest List",
                Source = "Stress",
                Version = "1.0",
                Region = "Test",
                IsActive = true,
                Priority = 1
            };
            var listId = _repo.InsertPriceList(priceList);
            listId.Should().BeGreaterThan(0);

            // Generate items
            _output.WriteLine($"Generating {itemCount} PriceItems...");
            var sw = Stopwatch.StartNew();
            var items = new List<PriceItem>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                items.Add(new PriceItem
                {
                    PriceListId = listId,
                    Code = $"STRESS.{i:D6}",
                    SuperChapter = $"Super{i / 10000}",
                    Chapter = $"{i / 1000:D2}.{i % 1000:D3}",
                    SubChapter = $"Sub{i / 100:D3}",
                    Description = $"Descrizione dell'elemento stress test #{i} con testo sufficientemente lungo per simulare un item realistico",
                    ShortDesc = $"Stress #{i}",
                    Unit = i % 2 == 0 ? "mq" : "mc",
                    UnitPrice = Math.Round(10.0 + (i % 100) * 1.5, 2),
                    IsNP = i % 100 == 0
                });
            }
            sw.Stop();
            _output.WriteLine($"Generation time: {sw.ElapsedMilliseconds} ms");

            // Batch insert
            GC.Collect();
            var memBefore = GC.GetTotalMemory(true);
            _output.WriteLine($"Memory before insert: {memBefore / 1024 / 1024} MB");

            sw.Restart();
            var inserted = _repo.InsertPriceItemsBatch(listId, items, batchSize);
            sw.Stop();

            GC.Collect();
            var memAfter = GC.GetTotalMemory(true);

            _output.WriteLine($"Insert time: {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalSeconds:F2}s)");
            _output.WriteLine($"Inserted: {inserted} items");
            _output.WriteLine($"Memory after insert: {memAfter / 1024 / 1024} MB");
            _output.WriteLine($"Throughput: {itemCount / sw.Elapsed.TotalSeconds:F0} items/sec");

            inserted.Should().Be(itemCount);

            // Verify via FTS5 search
            sw.Restart();
            var ftsResults = _repo.SearchFts("Stress #5000");
            sw.Stop();
            _output.WriteLine($"\nFTS5 search time: {sw.ElapsedMilliseconds} ms");
            ftsResults.Count.Should().BeGreaterThan(0);
            ftsResults.Should().Contain(i => i.Code == "STRESS.005000");

            // Verify via exact code match
            sw.Restart();
            var exactResults = _repo.FindByCodeExact("STRESS.099999");
            sw.Stop();
            _output.WriteLine($"Exact match time: {sw.ElapsedMilliseconds} ms");
            exactResults.Count.Should().Be(1);

            // Verify GetAllActivePriceItems
            sw.Restart();
            var allItems = _repo.GetAllActivePriceItems();
            sw.Stop();
            _output.WriteLine($"\nGetAllActivePriceItems: {allItems.Count} items in {sw.ElapsedMilliseconds} ms");
            allItems.Count.Should().Be(itemCount);
        }

        // ================================================================
        // 4. SQLite batch insert — test vari batch size (200, 500, 1000)
        // ================================================================
        [Fact]
        public void Stress_Sqlite_BatchSize_Comparison()
        {
            const int itemCount = 20_000;
            var results = new Dictionary<int, long>();

            foreach (var batchSize in new[] { 100, 500, 1000, 5000 })
            {
                var pl = new PriceList { Name = $"Batch-{batchSize}", Source = "Stress", IsActive = true, Priority = 1 };
                var plId = _repo.InsertPriceList(pl);

                var items = new List<PriceItem>(itemCount);
                for (int i = 0; i < itemCount; i++)
                {
                    items.Add(new PriceItem
                    {
                        PriceListId = plId,
                        Code = $"B.{batchSize}.{i:D6}",
                        Description = $"Item #{i}",
                        Unit = "mq",
                        UnitPrice = i * 1.1,
                    });
                }

                var sw = Stopwatch.StartNew();
                _repo.InsertPriceItemsBatch(plId, items, batchSize);
                sw.Stop();
                results[batchSize] = sw.ElapsedMilliseconds;
                _output.WriteLine($"BatchSize={batchSize}: {sw.ElapsedMilliseconds} ms ({itemCount / sw.Elapsed.TotalSeconds:F0} items/sec)");
            }

            // The fastest batch size should be reported
            var best = results.OrderBy(r => r.Value).First();
            _output.WriteLine($"\nBest batch size: {best.Key} ({best.Value} ms)");
        }

        // ================================================================
        // 5. FormulaEngine — 10k evaluations
        // ================================================================
        [Fact]
        public void Stress_FormulaEngine_10kEvaluations()
        {
            var engine = new FormulaEngine();
            var resolver = new DictionaryParameterResolver(new Dictionary<string, double>
            {
                ["Lunghezza"] = 5.5,
                ["Larghezza"] = 3.2,
                ["Altezza"] = 2.7,
                ["Area"] = 17.6,
                ["Volume"] = 47.52,
                ["Peso"] = 1250.0,
            });

            var formulas = new[]
            {
                "Lunghezza * Larghezza",
                "Lunghezza * Larghezza * Altezza",
                "Area * Altezza",
                "Peso / Volume",
                "Lunghezza + Larghezza + Altezza",
                "(Lunghezza + Larghezza) * 2",
                "Area * 0.5 + Volume * 0.3",
                "Lunghezza * Larghezza - (Lunghezza - 1) * (Larghezza - 0.5)",
                "IF(Lunghezza > 3, Area * 2, Area / 2)",
                "Peso > 1000 ? Volume : Area",
            };

            const int iterations = 10_000;
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                var formula = formulas[i % formulas.Length];
                var result = engine.Evaluate(formula, resolver);
                result.IsValid.Should().BeTrue($"Formula '{formula}' should be valid at iteration {i}");
            }

            sw.Stop();
            _output.WriteLine($"Evaluated {iterations} formulas in {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"Throughput: {iterations / sw.Elapsed.TotalSeconds:F0} formulas/sec");

            // Test with unresolved parameters (should still work, degraded)
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                var result = engine.Evaluate("Lunghezza * Larghezza * Inesistente * Altro", resolver);
                result.IsValid.Should().BeTrue("formula with unresolved params should still evaluate");
                result.UnresolvedIds.Count.Should().Be(2);
            }
            sw.Stop();
            _output.WriteLine($"\nWith unresolved params: {iterations} in {sw.ElapsedMilliseconds} ms");
        }

        // ================================================================
        // 6. PriceItemSearchService — search su DB con 50k items
        // ================================================================
        [Fact]
        public void Stress_SearchService_50kItems_Fuzzy()
        {
            const int itemCount = 50_000;

            // Seed DB
            var pl = new PriceList { Name = "Fuzzy Test", Source = "Stress", IsActive = true, Priority = 1 };
            var plId = _repo.InsertPriceList(pl);

            var items = new List<PriceItem>(itemCount);
            var rng = new Random(42);
            var words = new[] { "Calcestruzzo", "Acciaio", "Legno", "Muratura", "Scavo", "Riporto", "Fondazione", "Pilastro", "Trave", "Solaio", "Copertura", "Pavimento", "Rivestimento", "Tramezzo", "Isolamento" };
            for (int i = 0; i < itemCount; i++)
            {
                var w1 = words[rng.Next(words.Length)];
                var w2 = words[rng.Next(words.Length)];
                items.Add(new PriceItem
                {
                    PriceListId = plId,
                    Code = $"FZ.{i:D5}",
                    Description = $"{w1} per {w2} - Rck {20 + rng.Next(30)} MPa - spessore {10 + rng.Next(50)} cm",
                    ShortDesc = $"{w1} {w2}",
                    Unit = "mq",
                    UnitPrice = 10.0 + rng.NextDouble() * 500,
                });
            }
            _repo.InsertPriceItemsBatch(plId, items, 500);

            var searchService = new PriceItemSearchService(_repo);

            // Test L1: exact code
            var sw = Stopwatch.StartNew();
            var result = searchService.Search("FZ.25000");
            sw.Stop();
            _output.WriteLine($"L1 Exact code: {result.Count} results in {sw.ElapsedMilliseconds} ms (level={result.Level})");
            result.Level.Should().Be(SearchLevel.Exact);

            // Test L2: FTS5
            sw.Restart();
            result = searchService.Search("Calcestruzzo fondazione");
            sw.Stop();
            _output.WriteLine($"L2 FTS5: {result.Count} results in {sw.ElapsedMilliseconds} ms (level={result.Level})");
            result.Level.Should().Be(SearchLevel.FullText);
            result.Count.Should().BeGreaterThanOrEqualTo(3);

            // Test L3: Fuzzy (typo)
            sw.Restart();
            result = searchService.Search("calcestrusso");
            sw.Stop();
            _output.WriteLine($"L3 Fuzzy (typo 'calcestrusso' → 'Calcestruzzo'): {result.Count} results in {sw.ElapsedMilliseconds} ms (level={result.Level})");
            result.Level.Should().Be(SearchLevel.Fuzzy);
            result.Count.Should().BeGreaterThan(0, "typo should return fuzzy matches");

            // Test L3: very short query (should not crash)
            sw.Restart();
            result = searchService.Search("ac");
            sw.Stop();
            _output.WriteLine($"L3 Short query 'ac': {result.Count} results in {sw.ElapsedMilliseconds} ms (level={result.Level})");

            // Test cache invalidation
            searchService.InvalidateCache();
            sw.Restart();
            result = searchService.Search("acciaio");
            sw.Stop();
            _output.WriteLine($"After cache invalidate: {result.Count} results in {sw.ElapsedMilliseconds} ms");

            // Stress: multiple searches
            sw.Restart();
            int totalResults = 0;
            for (int i = 0; i < 100; i++)
            {
                var q = words[rng.Next(words.Length)];
                totalResults += searchService.Search(q).Count;
            }
            sw.Stop();
            _output.WriteLine($"\n100 random searches: total {totalResults} hits in {sw.ElapsedMilliseconds} ms ({100 / sw.Elapsed.TotalSeconds:F0} searches/sec)");
        }

        // ================================================================
        // 7. LevenshteinDistance — 100k comparisons
        // ================================================================
        [Fact]
        public void Stress_Levenshtein_100kComparisons()
        {
            var rng = new Random(99);
            var strings = new string[1000];
            for (int i = 0; i < strings.Length; i++)
            {
                var len = 5 + rng.Next(40);
                var chars = new char[len];
                for (int j = 0; j < len; j++)
                    chars[j] = (char)('a' + rng.Next(26));
                strings[i] = new string(chars);
            }

            const int comparisons = 100_000;
            var sw = Stopwatch.StartNew();

            double totalSimilarity = 0;
            for (int i = 0; i < comparisons; i++)
            {
                var a = strings[rng.Next(strings.Length)];
                var b = strings[rng.Next(strings.Length)];
                var sim = LevenshteinDistance.Similarity(a, b);
                totalSimilarity += sim;
            }

            sw.Stop();
            _output.WriteLine($"Levenshtein: {comparisons} comparisons in {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"Throughput: {comparisons / sw.Elapsed.TotalSeconds:F0} cmp/sec");
            _output.WriteLine($"Avg similarity: {totalSimilarity / comparisons:F4}");
        }

        // ================================================================
        // 8. DatabaseInitializer — schema creation performance
        // ================================================================
        [Fact]
        public void Stress_DatabaseInitializer_SchemaCreation()
        {
            const int iterations = 10;
            var times = new List<long>(iterations);

            for (int i = 0; i < iterations; i++)
            {
                var dbPath = Path.Combine(Path.GetTempPath(), $"qto_schema_{Guid.NewGuid():N}.db");
                var sw = Stopwatch.StartNew();

                using (var initRepo = new QtoRepository(dbPath))
                {
                    var version = initRepo.GetSchemaVersion();
                    version.Should().Be(13);
                }

                sw.Stop();
                times.Add(sw.ElapsedMilliseconds);
                _output.WriteLine($"Schema creation #{i + 1}: {sw.ElapsedMilliseconds} ms");

                try { File.Delete(dbPath); } catch { }
            }

            _output.WriteLine($"\nAvg: {times.Average():F0} ms, Min: {times.Min()} ms, Max: {times.Max()} ms");
            _output.WriteLine($"StdDev: {Math.Sqrt(times.Average(t => (t - times.Average()) * (t - times.Average()))):F0} ms");
        }

        private class DictionaryParameterResolver : IParameterResolver
        {
            private readonly Dictionary<string, double> _dict;
            public DictionaryParameterResolver(Dictionary<string, double> dict) => _dict = dict;
            public double? TryResolve(string name) =>
                _dict.TryGetValue(name, out var v) ? v : null;
        }
    }
}
