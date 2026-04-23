using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Text.Json;

namespace QtoRevitPlugin.Data
{
    public class FileFavoritesRepository : IFavoritesRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private readonly string _globalDir;

        public FileFavoritesRepository(string globalDir)
        {
            _globalDir = globalDir ?? throw new ArgumentNullException(nameof(globalDir));
        }

        public static string GetDefaultGlobalDir()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CmePlugin", "Favorites");
        }

        public FavoriteSet LoadGlobal()
        {
            var path = Path.Combine(_globalDir, "favorites.personal.json");
            return LoadFromFile(path) ?? new FavoriteSet { Scope = FavoriteScope.Personal };
        }

        public void SaveGlobal(FavoriteSet set)
        {
            Directory.CreateDirectory(_globalDir);
            set.Scope = FavoriteScope.Personal;
            var path = Path.Combine(_globalDir, "favorites.personal.json");
            File.WriteAllText(path, JsonSerializer.Serialize(set, JsonOptions));
        }

        public FavoriteSet? LoadForProject(string cmePath)
        {
            var dir = Path.GetDirectoryName(cmePath);
            if (string.IsNullOrEmpty(dir)) return null;
            var path = Path.Combine(dir, "favorites.project.json");
            return LoadFromFile(path);
        }

        public void SaveForProject(string cmePath, FavoriteSet set)
        {
            var dir = Path.GetDirectoryName(cmePath);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            set.Scope = FavoriteScope.Project;
            var path = Path.Combine(dir, "favorites.project.json");
            File.WriteAllText(path, JsonSerializer.Serialize(set, JsonOptions));
        }

        private static FavoriteSet? LoadFromFile(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<FavoriteSet>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
