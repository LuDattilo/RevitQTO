using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Servizio per serializzare/deserializzare <see cref="SelectionRulePreset"/>
    /// in formato JSON, e persisterli su filesystem (%AppData%\QtoPlugin\rules\).
    ///
    /// <para>Layer 1 di persistenza (§I6). Layer 2 (DataStorage su .rvt) è responsabilità
    /// di un helper distinto nel plugin project (perché richiede API Revit).</para>
    ///
    /// <para>Il service è statico + puro: nessuno stato interno, niente caching.
    /// Il chiamante gestisce eventuale cache in memoria.</para>
    /// </summary>
    public static class SelectionRulePresetService
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Directory dei preset JSON (<c>%AppData%\QtoPlugin\rules\</c>).
        /// Creata al volo se mancante.
        /// </summary>
        public static string GetPresetsDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "QtoPlugin", "rules");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Serializza un preset in JSON indentato.</summary>
        public static string Serialize(SelectionRulePreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            return JsonSerializer.Serialize(preset, JsonOpts);
        }

        /// <summary>Deserializza un preset da JSON. Throw su contenuto invalido.</summary>
        public static SelectionRulePreset Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON vuoto.", nameof(json));

            var preset = JsonSerializer.Deserialize<SelectionRulePreset>(json, JsonOpts);
            if (preset == null)
                throw new FormatException("Deserializzazione ha prodotto null — JSON malformato.");
            return preset;
        }

        /// <summary>
        /// Salva il preset come file JSON nella directory standard.
        /// Il nome file è <c>{RuleName}.json</c>, sanitizzato per filesystem.
        /// Ritorna il path completo del file creato.
        /// </summary>
        public static string SaveToFile(SelectionRulePreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (string.IsNullOrWhiteSpace(preset.RuleName))
                throw new ArgumentException("Il preset deve avere un RuleName non vuoto.", nameof(preset));

            var dir = GetPresetsDirectory();
            var fileName = SanitizeFileName(preset.RuleName) + ".json";
            var fullPath = Path.Combine(dir, fileName);

            File.WriteAllText(fullPath, Serialize(preset));
            return fullPath;
        }

        /// <summary>Carica tutti i preset dai file JSON nella directory standard.</summary>
        public static IReadOnlyList<SelectionRulePreset> LoadAllFromFiles()
        {
            var dir = GetPresetsDirectory();
            var result = new List<SelectionRulePreset>();

            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var preset = Deserialize(json);
                    result.Add(preset);
                }
                catch
                {
                    // File corrotto o non JSON valido → skip silenzioso
                    // (il chiamante può chiamare Deserialize su file specifici per error handling esplicito)
                }
            }

            return result
                .OrderBy(p => p.RuleName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Carica un preset specifico per nome (case-insensitive su filename).</summary>
        public static SelectionRulePreset? LoadFromFile(string ruleName)
        {
            if (string.IsNullOrWhiteSpace(ruleName)) return null;

            var fileName = SanitizeFileName(ruleName) + ".json";
            var path = Path.Combine(GetPresetsDirectory(), fileName);
            if (!File.Exists(path)) return null;

            try
            {
                return Deserialize(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Elimina il file JSON del preset. Ritorna true se esisteva e è stato rimosso.
        /// </summary>
        public static bool DeleteFile(string ruleName)
        {
            if (string.IsNullOrWhiteSpace(ruleName)) return false;

            var fileName = SanitizeFileName(ruleName) + ".json";
            var path = Path.Combine(GetPresetsDirectory(), fileName);
            if (!File.Exists(path)) return false;

            File.Delete(path);
            return true;
        }

        /// <summary>
        /// Sostituisce i caratteri non validi nel filesystem (Windows) con underscore.
        /// Mantiene leggibile il nome originale (es. "Muri Esterni - A" → "Muri Esterni - A").
        /// Esposto public per testabilità.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "default";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
            {
                chars[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
            }
            return new string(chars).Trim();
        }
    }
}
