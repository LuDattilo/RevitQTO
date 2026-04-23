using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint7
{
    public class FavoritesRepositoryTests
    {
        [Fact]
        public void SaveAndLoadGlobal_RoundTrips()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var repo = new FileFavoritesRepository(tempDir);
                var set = new FavoriteSet
                {
                    Name = "Test",
                    Items = new System.Collections.Generic.List<FavoriteItem>
                    {
                        new FavoriteItem { Code = "A.01.001", ShortDesc = "Test item", Unit = "m\u00b2", UnitPrice = 42.0 }
                    }
                };
                repo.SaveGlobal(set);
                var loaded = repo.LoadGlobal();
                Assert.Equal("Test", loaded.Name);
                Assert.Single(loaded.Items);
                Assert.Equal("A.01.001", loaded.Items[0].Code);
            }
            finally { Directory.Delete(tempDir, recursive: true); }
        }

        [Fact]
        public void LoadGlobal_WhenMissing_ReturnsEmpty()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var repo = new FileFavoritesRepository(tempDir);
                var loaded = repo.LoadGlobal();
                Assert.NotNull(loaded);
                Assert.Empty(loaded.Items);
            }
            finally { Directory.Delete(tempDir, recursive: true); }
        }

        [Fact]
        public void LoadForProject_OverridesGlobal_WhenBothPresent()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var projectDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(projectDir);
            var cmePath = Path.Combine(projectDir, "test.cme");
            try
            {
                var repo = new FileFavoritesRepository(tempDir);
                var globalSet = new FavoriteSet { Name = "Globale" };
                var projectSet = new FavoriteSet { Name = "Progetto" };
                repo.SaveGlobal(globalSet);
                repo.SaveForProject(cmePath, projectSet);

                var loaded = repo.LoadForProject(cmePath);
                Assert.Equal("Progetto", loaded?.Name);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
                Directory.Delete(projectDir, recursive: true);
            }
        }

        [Fact]
        public void SaveForProject_StoresProjectFavoritesInProjectScopedFile()
        {
            var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var projectDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(globalDir);
            Directory.CreateDirectory(projectDir);
            var cmePath = Path.Combine(projectDir, "demo.cme");

            try
            {
                var repo = new FileFavoritesRepository(globalDir);
                repo.SaveForProject(cmePath, new FavoriteSet { Name = "Progetto" });

                Assert.True(File.Exists(Path.Combine(projectDir, "favorites.project.json")));
            }
            finally
            {
                Directory.Delete(globalDir, recursive: true);
                Directory.Delete(projectDir, recursive: true);
            }
        }

        [Fact]
        public void SaveGlobal_StoresPersonalFavoritesInPersonalFile()
        {
            var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(globalDir);

            try
            {
                var repo = new FileFavoritesRepository(globalDir);
                repo.SaveGlobal(new FavoriteSet { Name = "Personali" });

                Assert.True(File.Exists(Path.Combine(globalDir, "favorites.personal.json")));
            }
            finally
            {
                Directory.Delete(globalDir, recursive: true);
            }
        }

        // ==================================================================
        // Scope-aware invariants (Fase 4 · D1)
        // ==================================================================

        [Fact]
        public void SaveGlobal_AlwaysSetsScopePersonal_EvenIfCallerPassedProject()
        {
            // D1 invariante: il repository globale è il canale "personali".
            // Qualunque Scope sul set in ingresso viene normalizzato a Personal.
            var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(globalDir);
            try
            {
                var repo = new FileFavoritesRepository(globalDir);
                var set = new FavoriteSet { Name = "X", Scope = FavoriteScope.Project };

                repo.SaveGlobal(set);
                var loaded = repo.LoadGlobal();

                Assert.Equal(FavoriteScope.Personal, loaded.Scope);
            }
            finally { Directory.Delete(globalDir, recursive: true); }
        }

        [Fact]
        public void SaveForProject_AlwaysSetsScopeProject_EvenIfCallerPassedPersonal()
        {
            // D1 invariante simmetrica: il canale project-side normalizza a Project.
            var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var projectDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(globalDir);
            Directory.CreateDirectory(projectDir);
            var cmePath = Path.Combine(projectDir, "demo.cme");
            try
            {
                var repo = new FileFavoritesRepository(globalDir);
                var set = new FavoriteSet { Name = "Y", Scope = FavoriteScope.Personal };

                repo.SaveForProject(cmePath, set);
                var loaded = repo.LoadForProject(cmePath);

                Assert.NotNull(loaded);
                Assert.Equal(FavoriteScope.Project, loaded!.Scope);
            }
            finally
            {
                Directory.Delete(globalDir, recursive: true);
                Directory.Delete(projectDir, recursive: true);
            }
        }

        [Fact]
        public void LoadForProject_WhenFileMissing_ReturnsNull()
        {
            // Differenza semantica con LoadGlobal: il project-level è opzionale,
            // quindi "nessun file" = null (la UI sa distinguere tra "non esiste" e "vuoto").
            var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var projectDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(globalDir);
            Directory.CreateDirectory(projectDir);
            var cmePath = Path.Combine(projectDir, "empty.cme");
            try
            {
                var repo = new FileFavoritesRepository(globalDir);

                var loaded = repo.LoadForProject(cmePath);

                Assert.Null(loaded);
            }
            finally
            {
                Directory.Delete(globalDir, recursive: true);
                Directory.Delete(projectDir, recursive: true);
            }
        }

        [Fact]
        public void FavoriteItem_ExtendedFields_RoundTrip()
        {
            // Campi estesi (Description, ListName, ListId, AddedAt, Note) introdotti
            // da Fase 4 devono sopravvivere il round-trip JSON.
            var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(globalDir);
            try
            {
                var repo = new FileFavoritesRepository(globalDir);
                var added = new System.DateTime(2026, 4, 23, 10, 0, 0, System.DateTimeKind.Utc);
                var set = new FavoriteSet
                {
                    Name = "Full",
                    Items = new System.Collections.Generic.List<FavoriteItem>
                    {
                        new FavoriteItem
                        {
                            Code = "F.01.002",
                            ShortDesc = "Voce full",
                            Description = "Descrizione completa multi-line.\nSeconda riga.",
                            Unit = "m",
                            UnitPrice = 12.5,
                            ListName = "Firenze 2025",
                            ListId = 7,
                            AddedAt = added,
                            Note = "Controllare misura"
                        }
                    }
                };

                repo.SaveGlobal(set);
                var loaded = repo.LoadGlobal();

                Assert.Single(loaded.Items);
                var item = loaded.Items[0];
                Assert.Equal("F.01.002", item.Code);
                Assert.Contains("Seconda riga", item.Description);
                Assert.Equal("Firenze 2025", item.ListName);
                Assert.Equal(7, item.ListId);
                Assert.Equal(added, item.AddedAt.ToUniversalTime());
                Assert.Equal("Controllare misura", item.Note);
            }
            finally { Directory.Delete(globalDir, recursive: true); }
        }

        [Fact]
        public void LoadGlobal_WithLegacyJsonMissingExtendedFields_DefaultsGracefully()
        {
            // Retrocompatibilità: file JSON scritti prima di Fase 4 non hanno
            // i campi estesi. Il caricamento non deve fallire né crashare.
            var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(globalDir);
            try
            {
                // JSON legacy: solo Code/ShortDesc/Unit/UnitPrice, niente Scope
                var legacyJson = @"{
                    ""Name"": ""Legacy"",
                    ""Items"": [
                        { ""Code"": ""L.01"", ""ShortDesc"": ""Legacy item"", ""Unit"": ""m"", ""UnitPrice"": 10.0 }
                    ]
                }";
                File.WriteAllText(Path.Combine(globalDir, "favorites.personal.json"), legacyJson);

                var repo = new FileFavoritesRepository(globalDir);
                var loaded = repo.LoadGlobal();

                Assert.Equal("Legacy", loaded.Name);
                Assert.Single(loaded.Items);
                Assert.Equal("L.01", loaded.Items[0].Code);
                // Campi estesi presenti ma default vuoti
                Assert.Equal(string.Empty, loaded.Items[0].Description);
                Assert.Equal(string.Empty, loaded.Items[0].ListName);
                Assert.Null(loaded.Items[0].ListId);
                Assert.Equal(string.Empty, loaded.Items[0].Note);
            }
            finally { Directory.Delete(globalDir, recursive: true); }
        }
    }
}
