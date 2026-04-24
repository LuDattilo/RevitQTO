using System;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    /// <summary>
    /// Plan C-4: verifica che il modello PriceItem C# mappi correttamente
    /// le nuove colonne XPWE aggiunte in schema v12 (Prezzo2..5, Articolo, Tariffa,
    /// SpCap/Cap/SbCap FK, IncMDO/MAT/SIC, TipoRisorsa, Flags, CnfQt, AdrInternet, DataEP).
    /// </summary>
    public class PriceItemXpweFieldsTests
    {
        private static string UniquePath() =>
            Path.Combine(Path.GetTempPath(), $"c4_test_{Guid.NewGuid():N}.db");

        private static void SafeDelete(string p)
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        [Fact]
        public void PriceItem_XpweFields_RoundtripViaRawSql()
        {
            var path = UniquePath();
            try
            {
                // Init schema v12 via QtoRepository
                using (var _ = new QtoRepository(path)) { }
                SqliteConnection.ClearAllPools();

                // Insert con tutti i campi XPWE via SQL raw
                int priceItemId;
                using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO PriceLists (Name, IsActive, Priority, RowCount)
                                            VALUES ('L', 1, 0, 0);";
                        cmd.ExecuteNonQuery();
                    }
                    int listId;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT last_insert_rowid();";
                        listId = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO PriceItems
                            (PriceListId, Code, Description, Unit, UnitPrice,
                             Articolo, Tariffa,
                             Prezzo1, Prezzo2, Prezzo3, Prezzo4, Prezzo5,
                             IncMDO, IncMAT, IncSIC,
                             TipoRisorsa, Flags, CnfQt, AdrInternet, DataEP)
                            VALUES
                            (@l, 'T-01', 'desc test', 'mc', 50.0,
                             'ART-A', 'TAR-B',
                             50.0, 45.0, 40.0, 35.0, 30.0,
                             30.5, 55.0, 2.5,
                             1, 512, 'cnfQt1', 'http://test.it', '15/01/2026');";
                        cmd.Parameters.AddWithValue("@l", listId);
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT last_insert_rowid();";
                        priceItemId = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
                SqliteConnection.ClearAllPools();

                // Read via repository (Dapper)
                using var repo = new QtoRepository(path);
                var items = repo.GetPriceItems(new[] { priceItemId });
                items.Should().ContainSingle();
                var pi = items[0];

                pi.Code.Should().Be("T-01");
                pi.UnitPrice.Should().Be(50.0);

                // Campi v12 nuovi
                pi.Articolo.Should().Be("ART-A");
                pi.Tariffa.Should().Be("TAR-B");
                pi.Prezzo1.Should().BeApproximately(50.0, 0.001);
                pi.Prezzo2.Should().BeApproximately(45.0, 0.001);
                pi.Prezzo3.Should().BeApproximately(40.0, 0.001);
                pi.Prezzo4.Should().BeApproximately(35.0, 0.001);
                pi.Prezzo5.Should().BeApproximately(30.0, 0.001);
                pi.IncMDO.Should().BeApproximately(30.5, 0.001);
                pi.IncMAT.Should().BeApproximately(55.0, 0.001);
                pi.IncSIC.Should().BeApproximately(2.5, 0.001);
                pi.TipoRisorsa.Should().Be(1);
                pi.Flags.Should().Be(512);
                pi.CnfQt.Should().Be("cnfQt1");
                pi.AdrInternet.Should().Be("http://test.it");
                pi.DataEP.Should().Be("15/01/2026");
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void PriceItem_XpweFields_DefaultsOnPlainInsert()
        {
            var path = UniquePath();
            try
            {
                using (var _ = new QtoRepository(path)) { }
                SqliteConnection.ClearAllPools();

                int piId;
                using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO PriceLists (Name, IsActive, Priority, RowCount) VALUES ('L', 1, 0, 0);";
                        cmd.ExecuteNonQuery();
                    }
                    int listId;
                    using (var cmd = conn.CreateCommand()) { cmd.CommandText = "SELECT last_insert_rowid();"; listId = Convert.ToInt32(cmd.ExecuteScalar()); }
                    // Insert "classica" v11 senza i nuovi campi
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO PriceItems (PriceListId, Code, Description, Unit, UnitPrice)
                                            VALUES (@l, 'OLD-01', 'vecchio import', 'mc', 100.0);";
                        cmd.Parameters.AddWithValue("@l", listId);
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand()) { cmd.CommandText = "SELECT last_insert_rowid();"; piId = Convert.ToInt32(cmd.ExecuteScalar()); }
                }
                SqliteConnection.ClearAllPools();

                using var repo = new QtoRepository(path);
                var pi = repo.GetPriceItems(new[] { piId })[0];

                // Campi v11 preservati
                pi.Code.Should().Be("OLD-01");
                pi.UnitPrice.Should().Be(100.0);

                // Campi v12 con default SQL (0 per REAL NOT NULL DEFAULT 0, null per nullable)
                pi.Prezzo1.Should().Be(0.0);
                pi.Prezzo2.Should().Be(0.0);
                pi.IncMDO.Should().Be(0.0);
                pi.Flags.Should().Be(512, "DEFAULT 512 in schema v12");
                pi.Articolo.Should().BeNull();
                pi.Tariffa.Should().BeNull();
                pi.CnfQt.Should().BeNull();
                pi.DataEP.Should().BeNull();
                pi.SpCapId.Should().BeNull();
            }
            finally { SafeDelete(path); }
        }
    }
}
