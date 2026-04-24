using System;
using System.IO;
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
    /// Plan C-2: test dei 5 domain services (Document, Chapter, Category, Wbs, Measurement).
    /// Ogni test istanzia un DB temporaneo via QtoRepository(path) che applica lo schema v12.
    /// </summary>
    public class DomainServicesTests
    {
        private static string UniquePath() =>
            Path.Combine(Path.GetTempPath(), $"ds_test_{Guid.NewGuid():N}.db");

        private static void SafeDelete(string p)
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        private static (QtoRepository repo, int sessId) NewRepoWithSession(string path)
        {
            var repo = new QtoRepository(path);
            var sid = repo.InsertSession(new WorkSession
            {
                ProjectPath = "t.rvt", SessionName = "T",
                CreatedAt = DateTime.UtcNow, LastSavedAt = DateTime.UtcNow
            });
            return (repo, sid);
        }

        private static (QtoRepository repo, int docId) NewRepoWithDoc(string path)
        {
            var (repo, sid) = NewRepoWithSession(path);
            var doc = new ComputoDocumentService(repo).GetOrCreate(sid);
            return (repo, doc.Id);
        }

        // =================== ComputoDocumentService ===================

        [Fact]
        public void DocumentService_GetOrCreate_FirstCall_CreatesNew()
        {
            var path = UniquePath();
            try
            {
                var (repo, sid) = NewRepoWithSession(path);
                var svc = new ComputoDocumentService(repo);
                var doc = svc.GetOrCreate(sid);
                doc.Id.Should().BeGreaterThan(0);
                doc.TipoDocumento.Should().Be(1);
                doc.Versione.Should().Be("5.04");
                doc.Currency.Should().Be("EUR");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void DocumentService_GetOrCreate_Idempotent()
        {
            var path = UniquePath();
            try
            {
                var (repo, sid) = NewRepoWithSession(path);
                var svc = new ComputoDocumentService(repo);
                var first = svc.GetOrCreate(sid);
                var second = svc.GetOrCreate(sid);
                second.Id.Should().Be(first.Id);
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void DocumentService_Update_NoId_Throws()
        {
            var path = UniquePath();
            try
            {
                var (repo, _) = NewRepoWithSession(path);
                var svc = new ComputoDocumentService(repo);
                var doc = new ComputoDocument { Id = 0 };
                Action act = () => svc.Update(doc);
                act.Should().Throw<DomainValidationException>()
                   .Which.RuleCode.Should().Be("NO_ID");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        // =================== ChapterService ===================

        [Fact]
        public void ChapterService_AddSuperChapter_Ok()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new ChapterService(repo);
                var sp = svc.AddSuperChapter(docId, "01", "Demolizioni");
                sp.Level.Should().Be("SpCap");
                sp.ParentId.Should().BeNull();
                sp.SortOrder.Should().Be(1);
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void ChapterService_FullHierarchy()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new ChapterService(repo);
                var sp = svc.AddSuperChapter(docId, "01", "Demolizioni");
                var cap = svc.AddChapter(docId, sp.Id, "01.01", "Murature");
                var sb = svc.AddSubChapter(docId, cap.Id, "01.01.01", "Esterni");
                sb.Level.Should().Be("SbCap");
                sb.ParentId.Should().Be(cap.Id);
                svc.GetAll(docId).Should().HaveCount(3);
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void ChapterService_AddChapter_WithWrongParentLevel_Throws()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new ChapterService(repo);
                var sp = svc.AddSuperChapter(docId, "01", "X");
                // AddSubChapter richiede parent di livello Cap, non SpCap
                Action act = () => svc.AddSubChapter(docId, sp.Id, "xx", "Y");
                act.Should().Throw<DomainValidationException>()
                   .Which.RuleCode.Should().Be("PARENT_WRONG_LEVEL");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void ChapterService_DuplicateCode_Throws()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new ChapterService(repo);
                svc.AddSuperChapter(docId, "01", "A");
                Action act = () => svc.AddSuperChapter(docId, "01", "B");
                act.Should().Throw<DomainValidationException>()
                   .Which.RuleCode.Should().Be("DUPLICATE_CODICE");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void ChapterService_EmptyCode_Throws()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new ChapterService(repo);
                Action act = () => svc.AddSuperChapter(docId, "", "X");
                act.Should().Throw<DomainValidationException>()
                   .Which.RuleCode.Should().Be("EMPTY_CODICE");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        // =================== CategoryService ===================

        [Fact]
        public void CategoryService_FullHierarchy()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new CategoryService(repo);
                var sp = svc.AddSuperCategory(docId, "1", "Opere strutturali");
                var c = svc.AddCategory(docId, sp.Id, "1.1", "Fondazioni");
                var sb = svc.AddSubCategory(docId, c.Id, "1.1.1", "Dirette");
                sb.Level.Should().Be("SbCat");
                sb.ParentId.Should().Be(c.Id);
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void CategoryService_WrongParent_Throws()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new CategoryService(repo);
                var sp = svc.AddSuperCategory(docId, "1", "X");
                Action act = () => svc.AddSubCategory(docId, sp.Id, "xx", "Y");
                act.Should().Throw<DomainValidationException>()
                   .Which.RuleCode.Should().Be("PARENT_WRONG_LEVEL");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        // =================== WbsService ===================

        [Fact]
        public void WbsService_Add_ComputesPathAndLevel()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new WbsService(repo);
                var root = svc.Add(docId, "WbsComputo", null, "Opere");
                root.Codice.Should().Be("1");
                root.Level.Should().Be(1);

                var c1 = svc.Add(docId, "WbsComputo", root.Id, "Edificio A");
                c1.Codice.Should().Be("1.1");
                c1.Level.Should().Be(2);

                var c2 = svc.Add(docId, "WbsComputo", root.Id, "Edificio B");
                c2.Codice.Should().Be("1.2");

                var grand = svc.Add(docId, "WbsComputo", c1.Id, "Piano 1");
                grand.Codice.Should().Be("1.1.1");
                grand.Level.Should().Be(3);
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void WbsService_InvalidKind_Throws()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new WbsService(repo);
                Action act = () => svc.Add(docId, "WbsInvalid", null, "X");
                act.Should().Throw<DomainValidationException>()
                   .Which.RuleCode.Should().Be("INVALID_KIND");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void WbsService_SeparateKinds_IndependentNumbering()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new WbsService(repo);
                var cap = svc.Add(docId, "WbsCap", null, "Cap");
                var cmp = svc.Add(docId, "WbsComputo", null, "Computo");
                cap.Codice.Should().Be("1");
                cmp.Codice.Should().Be("1");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        // =================== MeasurementService ===================

        /// <summary>Crea un PriceItem necessario come FK per i MeasurementRow.</summary>
        private static int InsertPriceItem(string path)
        {
            using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO PriceLists (Name, IsActive, Priority, RowCount) VALUES ('L', 1, 0, 0);";
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
            int piId;
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "SELECT last_insert_rowid();"; piId = Convert.ToInt32(cmd.ExecuteScalar()); }
            return piId;
        }

        [Fact]
        public void MeasurementService_ComputeQuantita_NullsAsOnes()
        {
            MeasurementService.ComputeQuantita(2, 3, 4, 5).Should().Be(120);
            MeasurementService.ComputeQuantita(2, null, null, null).Should().Be(2);
            MeasurementService.ComputeQuantita(1, 5, null, null).Should().Be(5);
            MeasurementService.ComputeQuantita(1, 0, 3, null).Should().Be(3, "0 = non valorizzato = 1");
        }

        [Fact]
        public void MeasurementService_CreateRow_AddSubRows_RecomputesQuantita()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                SqliteConnection.ClearAllPools();  // rilascia lock prima di riaprire conn dentro InsertPriceItem
                var piId = InsertPriceItem(path);
                SqliteConnection.ClearAllPools();

                // Re-apri il repo sullo stesso path (pooling pulito)
                using var repo2 = new QtoRepository(path);
                var svc = new MeasurementService(repo2);

                var row = svc.CreateRow(docId, piId);
                row.Quantita.Should().Be(0);

                svc.AddOrUpdateSubRow(row.Id, idvv: 100, descrizione: "a", partiUguali: 2, lunghezza: 3, larghezza: 4);
                svc.AddOrUpdateSubRow(row.Id, idvv: 101, descrizione: "b", partiUguali: 5);

                var rows = svc.GetRows(docId);
                rows.Should().ContainSingle();
                rows[0].Quantita.Should().BeApproximately(24 + 5, 0.001);

                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void MeasurementService_UpsertIdvvPositive_UpdatesSameRow()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                SqliteConnection.ClearAllPools();
                var piId = InsertPriceItem(path);
                SqliteConnection.ClearAllPools();

                using var repo2 = new QtoRepository(path);
                var svc = new MeasurementService(repo2);
                var row = svc.CreateRow(docId, piId);

                svc.AddOrUpdateSubRow(row.Id, idvv: 200, descrizione: "first", partiUguali: 1, lunghezza: 10);
                svc.AddOrUpdateSubRow(row.Id, idvv: 200, descrizione: "updated", partiUguali: 2, lunghezza: 10);

                var subs = svc.GetSubRows(row.Id);
                subs.Should().ContainSingle("IDVV=200 upsert, niente duplicazioni");
                subs[0].Descrizione.Should().Be("updated");
                subs[0].Quantita.Should().BeApproximately(20, 0.001);

                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void MeasurementService_NegativeIdvv_AlwaysInserts()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                SqliteConnection.ClearAllPools();
                var piId = InsertPriceItem(path);
                SqliteConnection.ClearAllPools();

                using var repo2 = new QtoRepository(path);
                var svc = new MeasurementService(repo2);
                var row = svc.CreateRow(docId, piId);

                svc.AddOrUpdateSubRow(row.Id, idvv: -1, descrizione: "a", partiUguali: 1);
                svc.AddOrUpdateSubRow(row.Id, idvv: -1, descrizione: "b", partiUguali: 2);

                svc.GetSubRows(row.Id).Should().HaveCount(2, "IDVV<0 manuali, niente upsert");

                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void MeasurementService_InvalidPriceItem_Throws()
        {
            var path = UniquePath();
            try
            {
                var (repo, docId) = NewRepoWithDoc(path);
                var svc = new MeasurementService(repo);
                Action act = () => svc.CreateRow(docId, 0);
                act.Should().Throw<DomainValidationException>()
                   .Which.RuleCode.Should().Be("INVALID_PRICE_ITEM");
                repo.Dispose();
            }
            finally { SafeDelete(path); }
        }
    }
}
