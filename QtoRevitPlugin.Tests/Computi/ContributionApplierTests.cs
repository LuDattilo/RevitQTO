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
    /// Test dell'applicatore dei contributi dell'estrazione avanzata (strati/derivate):
    /// raggruppamento per codice (una voce, N righe), codici non risolti riportati, contributi non
    /// computabili flaggati.
    /// </summary>
    public class ContributionApplierTests
    {
        private static string UniquePath() =>
            Path.Combine(Path.GetTempPath(), $"ca_test_{Guid.NewGuid():N}.db");

        private static void SafeDelete(string p)
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        private static int InsertPriceItem(string path, string code)
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
                                    VALUES (@l, @c, 'x', 'mc', 50.0);";
                cmd.Parameters.AddWithValue("@l", listId);
                cmd.Parameters.AddWithValue("@c", code);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "SELECT last_insert_rowid();"; return Convert.ToInt32(cmd.ExecuteScalar()); }
        }

        private static ComputoContribution C(int elementId, string code, double qty, bool computed = true, string note = "") =>
            new ComputoContribution
            {
                ElementId = elementId, Code = code, Um = "mc", Quantity = qty,
                Descrizione = "strato " + code, Computed = computed, Note = note,
                Category = "Muri", FamilyName = "Muro",
            };

        [Fact]
        public void Apply_groupsByCode_oneVoce_manyRows_andReportsUnresolved()
        {
            var path = UniquePath();
            try
            {
                int sid, docId, pidCls, pidIso;
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
                pidCls = InsertPriceItem(path, "CLS");
                pidIso = InsertPriceItem(path, "ISO");
                SqliteConnection.ClearAllPools();

                using (var repo = new QtoRepository(path))
                {
                    var applier = new ComputoContributionApplier(new MeasurementService(repo));
                    var resolver = new Func<string, int?>(code => code switch
                    {
                        "CLS" => pidCls,
                        "ISO" => pidIso,
                        _ => (int?)null,   // "SCONOSCIUTO" non risolvibile
                    });

                    var contributions = new List<ComputoContribution>
                    {
                        C(101, "CLS", 2.0),   // due elementi condividono CLS -> 1 voce, 2 righe
                        C(102, "CLS", 3.0),
                        C(101, "ISO", 0.5),   // stesso elemento, strato diverso -> altra voce
                        C(103, "SCONOSCIUTO", 1.0),  // codice non risolvibile
                    };

                    var res = applier.Apply(docId, contributions, resolver);

                    res.VociCreate.Should().Be(2);           // CLS + ISO
                    res.SubRowsAggiunte.Should().Be(3);      // 2 CLS + 1 ISO
                    res.CodiciNonRisolti.Should().ContainSingle().And.Contain("SCONOSCIUTO");

                    var rows = repo.GetMeasurementRows(docId);
                    rows.Should().HaveCount(2);
                    // La voce CLS ha quantità 2+3 = 5 (somma dei RGItem).
                    var cls = rows.Single(r => r.PriceItemId == pidCls);
                    cls.Quantita.Should().BeApproximately(5.0, 0.001);
                }
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void Apply_nonComputedContribution_isFlagged_stillVisible()
        {
            var path = UniquePath();
            try
            {
                int sid, docId, pid;
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
                pid = InsertPriceItem(path, "ACC");
                SqliteConnection.ClearAllPools();

                using (var repo = new QtoRepository(path))
                {
                    var applier = new ComputoContributionApplier(new MeasurementService(repo));
                    var res = applier.Apply(docId,
                        new List<ComputoContribution> { C(200, "ACC", 0.0, computed: false, note: "densità mancante") },
                        _ => pid);

                    res.DaCompletareAMano.Should().Be(1);
                    res.SubRowsAggiunte.Should().Be(1);

                    var rowId = repo.GetMeasurementRows(docId).Single().Id;
                    var sub = repo.GetMeasurementSubRows(rowId).Single();
                    sub.Quantita.Should().Be(0.0);
                    sub.Descrizione.Should().Contain("densità mancante");
                }
            }
            finally { SafeDelete(path); }
        }
    }
}
