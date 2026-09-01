using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services.Computi;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    /// <summary>
    /// v13: backfill una-tantum di categoria/famiglia sulle sotto-righe legacy (Category NULL).
    /// </summary>
    public class CategoryBackfillTests
    {
        private static string UniquePath() =>
            Path.Combine(Path.GetTempPath(), $"bf_test_{Guid.NewGuid():N}.db");

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

        [Fact]
        public void Backfill_findsLegacySubRows_applies_andIsIdempotent()
        {
            var path = UniquePath();
            try
            {
                int sid, docId, piId;
                using (var repo = new QtoRepository(path))
                {
                    sid = repo.InsertSession(new WorkSession
                    {
                        ProjectPath = "t.rvt", SessionName = "T",
                        CreatedAt = DateTime.UtcNow, LastSavedAt = DateTime.UtcNow
                    });
                    docId = new ComputoDocumentService(repo).GetOrCreate(sid).Id;
                }
                SqliteConnection.ClearAllPools();
                piId = InsertPriceItem(path);
                SqliteConnection.ClearAllPools();

                int subId1, subId2;
                using (var repo = new QtoRepository(path))
                {
                    var svc = new MeasurementService(repo);
                    var row = svc.CreateRow(docId, piId);
                    // Due sotto-righe SENZA categoria (legacy pre-v13): idvv > 0.
                    subId1 = svc.AddOrUpdateSubRow(row.Id, idvv: 111, descrizione: "a", larghezza: 2.0).Id;
                    subId2 = svc.AddOrUpdateSubRow(row.Id, idvv: 222, descrizione: "b", larghezza: 3.0).Id;
                    // Una sotto-riga manuale (idvv < 0): NON deve essere candidata.
                    svc.AddOrUpdateSubRow(row.Id, idvv: -1, descrizione: "manuale", partiUguali: 1.0);
                }
                SqliteConnection.ClearAllPools();

                using (var repo = new QtoRepository(path))
                {
                    var bf = new ComputoCategoryBackfillService(repo);
                    var pending = bf.GetPending(sid);
                    pending.Should().HaveCount(2);
                    pending.Select(p => p.ElementId).Should().BeEquivalentTo(new[] { 111, 222 });

                    // Simula la risoluzione Revit: 111 risolto, 222 elemento cancellato (nessun valore).
                    var resolutions = new List<CategoryBackfillResolution>
                    {
                        new CategoryBackfillResolution { SubRowId = subId1, Category = "Muri", FamilyName = "Muro di base" },
                        new CategoryBackfillResolution { SubRowId = subId2, Category = null, FamilyName = null },
                    };
                    var applied = bf.Apply(resolutions);
                    applied.Should().Be(1);   // solo 111 (222 senza valori: saltato, non azzerato)

                    var subs = repo.GetMeasurementSubRows(repo.GetMeasurementRows(docId).Single().Id);
                    subs.Single(s => s.Id == subId1).Category.Should().Be("Muri");

                    // Idempotenza: 111 non è più candidato; 222 (ancora NULL) resta pendente.
                    var pending2 = bf.GetPending(sid);
                    pending2.Select(p => p.ElementId).Should().BeEquivalentTo(new[] { 222 });
                }
            }
            finally { SafeDelete(path); }
        }
    }
}
