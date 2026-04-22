using FluentAssertions;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using System;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Data
{
    public class DatabaseInitializerTests : IDisposable
    {
        private readonly string _tempPath;

        public DatabaseInitializerTests()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), $"qto_init_test_{Guid.NewGuid():N}.db");
        }

        public void Dispose()
        {
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
        }

        [Fact]
        public void OpenOrCreate_CreatesAllTables()
        {
            var init = new DatabaseInitializer(_tempPath);
            using var conn = init.OpenOrCreate();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            using var reader = cmd.ExecuteReader();

            var tables = new System.Collections.Generic.List<string>();
            while (reader.Read()) tables.Add(reader.GetString(0));

            tables.Should().Contain(new[]
            {
                "SchemaInfo",
                "Sessions",
                "PriceLists",
                "PriceItems",
                "QtoAssignments",
                "ManualItems",
                "RoomMappings",
                "NuoviPrezzi",
                "SelectionRules",
                "MeasurementRules",
                "ModelDiffLog",
                "EmbeddingCache"
            });
        }

        [Fact]
        public void OpenOrCreate_Twice_DoesNotThrow_Idempotent()
        {
            var init = new DatabaseInitializer(_tempPath);

            using (var conn1 = init.OpenOrCreate()) { /* prima apertura crea schema */ }

            using var conn2 = init.OpenOrCreate();
            using var cmd = conn2.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Sessions;";
            cmd.ExecuteScalar().Should().Be(0L);
        }

        [Fact]
        public void GetDefaultDbPath_UsesAppData()
        {
            var path = DatabaseInitializer.GetDefaultDbPath("Lotto A");
            path.Should().EndWith("Lotto A.db");
            path.Should().Contain("QtoPlugin");
        }

        [Fact]
        public void GetDefaultDbPath_SanitizesInvalidChars()
        {
            // Usa caratteri sicuramente invalidi su Windows
            var path = DatabaseInitializer.GetDefaultDbPath("Lotto<A>:\"1\"");
            Path.GetFileName(path).Should().NotContain("<");
            Path.GetFileName(path).Should().NotContain(">");
            Path.GetFileName(path).Should().NotContain(":");
        }
    }
}
