using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace QtoRevitPlugin.Reports
{
    /// <summary>
    /// Load/save di ExportTemplate JSON da filesystem. Folder default:
    /// %AppData%\QtoPlugin\Templates\. Al primo uso genera 2 template:
    /// "Standard" (blu) e "DEI 2024" (nero/bordeaux con footer).
    /// </summary>
    public class ExportTemplateRepository
    {
        private readonly string _folder;
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ExportTemplateRepository(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(_folder);
            SeedDefaultsIfEmpty();
        }

        public static string GetDefaultFolder()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "QtoPlugin", "Templates");
        }

        public List<ExportTemplate> LoadAll()
        {
            var list = new List<ExportTemplate>();
            foreach (var file in Directory.GetFiles(_folder, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var t = JsonSerializer.Deserialize<ExportTemplate>(json, JsonOpts);
                    if (t != null) list.Add(t);
                }
                catch { }
            }
            return list.OrderBy(t => t.DisplayName).ToList();
        }

        public void SaveTemplate(ExportTemplate template)
        {
            var safeName = string.IsNullOrEmpty(template.Name) ? "template" : template.Name;
            foreach (var c in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(c, '_');
            var path = Path.Combine(_folder, safeName + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOpts));
        }

        public ExportTemplate GetDefault()
        {
            var all = LoadAll();
            return all.FirstOrDefault(t => t.Name == "Standard")
                   ?? all.FirstOrDefault()
                   ?? BuildStandardDefault();
        }

        private void SeedDefaultsIfEmpty()
        {
            if (Directory.GetFiles(_folder, "*.json").Length > 0) return;
            SaveTemplate(BuildStandardDefault());
            SaveTemplate(BuildDei2024Default());
        }

        private static ExportTemplate BuildStandardDefault() => new ExportTemplate
        {
            Name = "Standard",
            DisplayName = "Standard",
            Format = "All",
            HeaderColorHex = "#1E6FD9",
            HeaderTextColorHex = "#FFFFFF",
            SubtotalColorHex = "#F0F0F0",
            Footer = "",
            IncludeSubtotalRows = true,
            NumberFormat = "#,##0.00",
            CurrencyFormat = "#,##0.00 €"
        };

        private static ExportTemplate BuildDei2024Default() => new ExportTemplate
        {
            Name = "DEI2024",
            DisplayName = "DEI 2024",
            Format = "All",
            HeaderColorHex = "#2C2C2C",
            HeaderTextColorHex = "#FFFFFF",
            SubtotalColorHex = "#EFE5D9",
            Footer = "Formato DEI Nuove Costruzioni 2024",
            IncludeSubtotalRows = true,
            NumberFormat = "#,##0.00",
            CurrencyFormat = "#,##0.00 €"
        };
    }
}
