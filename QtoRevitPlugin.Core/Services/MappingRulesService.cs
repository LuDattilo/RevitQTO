using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace QtoRevitPlugin.Services
{
    public class MappingRulesService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private const string FileName = "QTO_MappingRules.json";

        private readonly string _globalDir;
        private string? _projectCmePath;

        private static readonly MappingRulesConfig DefaultConfig = new MappingRulesConfig
        {
            Version = 1,
            Rules = new List<MappingRule>
            {
                new MappingRule { RevitCategory = "OST_Walls",             DefaultParam = "Area",   AllowedParams = new List<string>{"Area","Volume","Length","Count"}, HashParams = new List<string>{"Area","Volume"},   UnitDisplay = "m²", RoundingDecimals = 2, VuotoPerPieno = true  },
                new MappingRule { RevitCategory = "OST_Floors",            DefaultParam = "Area",   AllowedParams = new List<string>{"Area","Volume","Count"},          HashParams = new List<string>{"Area","Volume"},   UnitDisplay = "m²", RoundingDecimals = 2, VuotoPerPieno = false },
                new MappingRule { RevitCategory = "OST_Ceilings",          DefaultParam = "Area",   AllowedParams = new List<string>{"Area","Count"},                  HashParams = new List<string>{"Area"},            UnitDisplay = "m²", RoundingDecimals = 2, VuotoPerPieno = false },
                new MappingRule { RevitCategory = "OST_Roofs",             DefaultParam = "Area",   AllowedParams = new List<string>{"Area","Volume","Count"},          HashParams = new List<string>{"Area","Volume"},   UnitDisplay = "m²", RoundingDecimals = 2, VuotoPerPieno = false },
                new MappingRule { RevitCategory = "OST_StructuralFraming", DefaultParam = "Length", AllowedParams = new List<string>{"Length","Count"},                HashParams = new List<string>{"Length"},          UnitDisplay = "m",       RoundingDecimals = 2, VuotoPerPieno = false },
                new MappingRule { RevitCategory = "OST_StructuralColumns", DefaultParam = "Volume", AllowedParams = new List<string>{"Volume","Length","Count"},        HashParams = new List<string>{"Volume","Length"}, UnitDisplay = "m³", RoundingDecimals = 3, VuotoPerPieno = false },
                new MappingRule { RevitCategory = "OST_Doors",             DefaultParam = "Count",  AllowedParams = new List<string>{"Count"},                         HashParams = new List<string>{"Count"},           UnitDisplay = "nr",      RoundingDecimals = 0, VuotoPerPieno = false },
                new MappingRule { RevitCategory = "OST_Windows",           DefaultParam = "Count",  AllowedParams = new List<string>{"Count"},                         HashParams = new List<string>{"Count"},           UnitDisplay = "nr",      RoundingDecimals = 0, VuotoPerPieno = false },
            }
        };

        private static readonly MappingRule FallbackRule = new MappingRule
        {
            RevitCategory = "*",
            DefaultParam = "Count",
            AllowedParams = new List<string> { "Count" },
            HashParams = new List<string> { "Count" },
            UnitDisplay = "nr",
            RoundingDecimals = 0
        };

        public MappingRulesService(string? globalDir = null, string? projectCmePath = null)
        {
            _globalDir = globalDir ?? GetDefaultGlobalDir();
            _projectCmePath = projectCmePath;
        }

        public static string GetDefaultGlobalDir()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CmePlugin");
        }

        public void SetProjectCmePath(string cmePath) => _projectCmePath = cmePath;

        public MappingRulesConfig LoadGlobal()
        {
            var path = Path.Combine(_globalDir, FileName);
            if (!File.Exists(path)) return Clone(DefaultConfig);
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<MappingRulesConfig>(json) ?? Clone(DefaultConfig);
            }
            catch { return Clone(DefaultConfig); }
        }

        public void SaveGlobal(MappingRulesConfig config)
        {
            Directory.CreateDirectory(_globalDir);
            File.WriteAllText(Path.Combine(_globalDir, FileName), JsonSerializer.Serialize(config, JsonOptions));
        }

        public MappingRulesConfig? LoadForProject()
        {
            if (string.IsNullOrEmpty(_projectCmePath)) return null;
            var dir = Path.GetDirectoryName(_projectCmePath);
            if (string.IsNullOrEmpty(dir)) return null;
            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<MappingRulesConfig>(json);
            }
            catch { return null; }
        }

        public MappingRule GetRule(string revitCategory)
        {
            var config = LoadForProject() ?? LoadGlobal();
            foreach (var rule in config.Rules)
                if (string.Equals(rule.RevitCategory, revitCategory, StringComparison.OrdinalIgnoreCase))
                    return rule;
            return FallbackRule;
        }

        private static MappingRulesConfig Clone(MappingRulesConfig src)
        {
            var json = JsonSerializer.Serialize(src, JsonOptions);
            return JsonSerializer.Deserialize<MappingRulesConfig>(json)!;
        }
    }
}
