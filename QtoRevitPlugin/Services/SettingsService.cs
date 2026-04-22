using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Text.Json;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Carica/salva CmeSettings in %AppData%\CmePlugin\settings.json.
    /// Singleton per comodità — le impostazioni sono user-level, non per-progetto.
    /// </summary>
    public class SettingsService
    {
        private static readonly object _lock = new();
        private static CmeSettings? _cached;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CmePlugin", "settings.json");

        public static event EventHandler? SettingsChanged;

        /// <summary>Restituisce le impostazioni correnti, caricandole da disco se non in cache.</summary>
        public static CmeSettings Load()
        {
            if (_cached != null) return _cached;

            lock (_lock)
            {
                if (_cached != null) return _cached;
                _cached = LoadFromDisk();
                return _cached;
            }
        }

        private static CmeSettings LoadFromDisk()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new CmeSettings();
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<CmeSettings>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return loaded ?? new CmeSettings();
            }
            catch
            {
                return new CmeSettings();
            }
        }

        /// <summary>Salva le impostazioni su disco e aggiorna la cache. Notifica i listener.</summary>
        public static void Save(CmeSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            // Normalizza il minimo prima di salvare
            settings.AutoSaveIntervalMinutes = settings.NormalizedAutoSaveIntervalMinutes;

            lock (_lock)
            {
                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);

                _cached = settings;
            }

            SettingsChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
