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
    }
}
