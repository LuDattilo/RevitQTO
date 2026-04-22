using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Parsers;
using QtoRevitPlugin.Search;
using Xunit;
using Xunit.Abstractions;

namespace QtoRevitPlugin.Tests.Integration
{
    /// <summary>
    /// Test empirico su listino reale Regione Toscana (Firenze 2025, ~70 MB, schema EASY).
    /// Il file NON è nel repo (gitignored). Se non presente, i test escono early con Skip logico
    /// (passano trivialmente in CI). Sul PC dello sviluppatore, se presente, validano il pipeline
    /// completo parse → import SQLite → ricerca FTS5 + Levenshtein.
    ///
    /// Per eseguire:
    /// <code>dotnet test --filter "FullyQualifiedName~FirenzeIntegration" --logger "console;verbosity=detailed"</code>
    /// </summary>
    public class FirenzeIntegrationTests
    {
        // Path relativo al working dir standard `dotnet test` (solution root).
        private const string TestFileName = "Firenze-2025.xml";
        private static readonly string TestFilePath = Path.Combine(
            FindSolutionRoot(), TestFileName);

        private readonly ITestOutputHelper _output;

        public FirenzeIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void DcfParser_Firenze2025_ParsesAndStatsOk()
        {
            if (!File.Exists(TestFilePath))
            {
                _output.WriteLine($"[SKIP] {TestFilePath} non presente — test integration locale only.");
                return;
            }

            var fileSize = new FileInfo(TestFilePath).Length;
            _output.WriteLine($"File: {TestFilePath}");
            _output.WriteLine($"Size: {fileSize / 1024.0 / 1024.0:F2} MB");

            var sw = Stopwatch.StartNew();
            var result = new DcfParser().Parse(TestFilePath);
            sw.Stop();

            _output.WriteLine($"Parse time: {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"Items: {result.Items.Count}");
            _output.WriteLine($"Warnings: {result.Warnings.Count}");
            _output.WriteLine($"TotalRowsDetected: {result.TotalRowsDetected}");
            _output.WriteLine($"Source: {result.Metadata.Source}");

            // Top 5 SuperChapter distinti con count
            var topSuperChapters = result.Items
                .GroupBy(i => i.SuperChapter)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"  {g.Count(),6} · {Truncate(g.Key, 80)}");
            _output.WriteLine("Top 5 SuperChapter:");
            foreach (var line in topSuperChapters) _output.WriteLine(line);

            // Sample: primi 3 items
            _output.WriteLine("Sample items:");
            foreach (var item in result.Items.Take(3))
            {
                _output.WriteLine($"  Code={item.Code}");
                _output.WriteLine($"    Unit={item.Unit}  Price={item.UnitPrice:F5}");
                _output.WriteLine($"    SuperChapter={Truncate(item.SuperChapter, 60)}");
                _output.WriteLine($"    Chapter={Truncate(item.Chapter, 60)}");
                _output.WriteLine($"    SubChapter={Truncate(item.SubChapter, 60)}");
                _output.WriteLine($"    Description[0..80]={Truncate(item.Description, 80)}");
            }

            // Prime 10 warning (se presenti)
            if (result.Warnings.Count > 0)
            {
                _output.WriteLine("First 10 warnings:");
                foreach (var w in result.Warnings.Take(10)) _output.WriteLine($"  {w}");
            }

            // Asserzioni minime di sanità
            result.Items.Count.Should().BeGreaterThan(100, "un listino regionale reale ha migliaia di voci");
            result.Items.All(i => !string.IsNullOrEmpty(i.Code)).Should().BeTrue();
            result.Items.Where(i => i.UnitPrice > 0).Should().HaveCountGreaterThan(result.Items.Count / 2,
                "almeno la maggioranza delle voci ha un prezzo > 0");
        }

        [Fact]
        public void Repository_ImportAndSearch_Firenze2025()
        {
            if (!File.Exists(TestFilePath))
            {
                _output.WriteLine($"[SKIP] {TestFilePath} non presente.");
                return;
            }

            // Parse
            var swParse = Stopwatch.StartNew();
            var parseResult = new DcfParser().Parse(TestFilePath);
            swParse.Stop();
            _output.WriteLine($"Parse: {swParse.ElapsedMilliseconds} ms → {parseResult.Items.Count} items");

            // Import in DB temp
            var dbPath = Path.Combine(Path.GetTempPath(), $"qto-firenze-{Guid.NewGuid():N}.db");
            try
            {
                using var repo = new QtoRepository(dbPath);

                parseResult.Metadata.Name = "Firenze LLPP 2025";
                var listId = repo.InsertPriceList(parseResult.Metadata);

                var swImport = Stopwatch.StartNew();
                var imported = repo.InsertPriceItemsBatch(listId, parseResult.Items);
                swImport.Stop();
                _output.WriteLine($"Import: {swImport.ElapsedMilliseconds} ms → {imported} rows inserted");

                // Query FTS5 test
                var searchService = new PriceItemSearchService(repo);
                RunSearch(searchService, "calcestruzzo");
                RunSearch(searchService, "scavo");
                RunSearch(searchService, "intonaco");
                RunSearch(searchService, "muratura");
                RunSearch(searchService, "acciaio B450C");

                // Fuzzy test: query con typo
                RunSearch(searchService, "calcestruso", label: "[fuzzy]"); // typo intenzionale

                imported.Should().BeGreaterThan(100);
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* ignore */ }
            }
        }

        private void RunSearch(PriceItemSearchService svc, string query, string label = "")
        {
            var sw = Stopwatch.StartNew();
            var result = svc.Search(query, maxResults: 5);
            sw.Stop();
            _output.WriteLine($"  {label} \"{query}\" → {result.Level} ({result.Count} hits) in {sw.ElapsedMilliseconds} ms");
            foreach (var item in result.Items.Take(3))
                _output.WriteLine($"      · {item.Code} {Truncate(item.ShortDesc ?? item.Description, 60)}");
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static string FindSolutionRoot()
        {
            // Risali da bin/Debug/net8.0/ fino al dir che contiene QtoRevitPlugin.sln o Firenze-2025.xml
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetFiles("*.sln").Any() || dir.GetFiles(TestFileName).Any())
                    return dir.FullName;
                dir = dir.Parent;
            }
            // Fallback: working dir
            return Directory.GetCurrentDirectory();
        }
    }
}
