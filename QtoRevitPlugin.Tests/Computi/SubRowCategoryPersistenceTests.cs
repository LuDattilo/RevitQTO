using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    /// <summary>
    /// v13: le sotto-righe di misura persistono Category/FamilyName dell'elemento Revit, che
    /// alimentano il mismatch semantico AI di Health sul modello Computi.
    /// </summary>
    public class SubRowCategoryPersistenceTests
    {
        private static string UniquePath() =>
            Path.Combine(Path.GetTempPath(), $"v13_test_{Guid.NewGuid():N}.db");

        private static void SafeDelete(string p)
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        private static int InsertPriceItem(string path)
        {
            using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO PriceLists (Name, IsActive, Priority, RowCount) VALUES ('L', 1, 0, 0);";
                cmd.ExecuteNonQuery();
            }
            int listId;
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "SELECT last_insert_rowid();"; listId = Convert.ToInt32(cmd.ExecuteScalar()); }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO PriceItems (PriceListId, Code, Description, Unit, UnitPrice)
                                    VALUES (@l, 'X', 'x', 'mc', 50.0);";
                cmd.Parameters.AddWithValue("@l", listId);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "SELECT last_insert_rowid();"; return Convert.ToInt32(cmd.ExecuteScalar()); }
        }

        private static (int docId, int piId) Seed(string path)
        {
            int docId;
            using (var repo = new QtoRepository(path))
            {
                repo.GetSchemaVersion().Should().Be(13);
                var sid = repo.InsertSession(new WorkSession
                {
                    ProjectPath = "t.rvt", SessionName = "T",
                    CreatedAt = DateTime.UtcNow, LastSavedAt = DateTime.UtcNow
                });
                docId = new ComputoDocumentService(repo).GetOrCreate(sid).Id;
            }
            SqliteConnection.ClearAllPools();
            var piId = InsertPriceItem(path);
            SqliteConnection.ClearAllPools();
            return (docId, piId);
        }

        [Fact]
        public void SubRow_persistsCategoryAndFamily_roundTrip()
        {
            var path = UniquePath();
            try
            {
                var (docId, piId) = Seed(path);
                using var repo = new QtoRepository(path);
                var svc = new MeasurementService(repo);

                var row = svc.CreateRow(docId, piId);
                svc.AddOrUpdateSubRow(row.Id, idvv: 12345, descrizione: "el",
                    larghezza: 4.0, category: "Muri", familyName: "Muro di base");

                var subs = repo.GetMeasurementSubRows(row.Id);
                subs.Should().HaveCount(1);
                subs[0].Category.Should().Be("Muri");
                subs[0].FamilyName.Should().Be("Muro di base");
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void SubRow_reMeasureWithoutContext_doesNotWipeStoredCategory()
        {
            var path = UniquePath();
            try
            {
                var (docId, piId) = Seed(path);
                using var repo = new QtoRepository(path);
                var svc = new MeasurementService(repo);

                var row = svc.CreateRow(docId, piId);
                svc.AddOrUpdateSubRow(row.Id, idvv: 999, descrizione: "el",
                    larghezza: 4.0, category: "Pavimenti", familyName: "Solaio");

                // Re-measure dello stesso elemento senza contesto categoria/famiglia (null):
                // il valore già persistito NON deve essere azzerato.
                svc.AddOrUpdateSubRow(row.Id, idvv: 999, descrizione: "el", larghezza: 5.0);

                var s = repo.GetMeasurementSubRows(row.Id).Single();
                s.Larghezza.Should().Be(5.0);
                s.Category.Should().Be("Pavimenti");
                s.FamilyName.Should().Be("Solaio");
            }
            finally { SafeDelete(path); }
        }
    }
}
