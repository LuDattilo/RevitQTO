using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using QtoRevitPlugin.Computo.Extraction;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Carica/salva le regole delle voci derivate (armatura, casseforme, parità) da un JSON, con la
    /// stessa strategia di posizione delle <see cref="MappingRulesService"/>: file globale in
    /// <c>%AppData%\CmePlugin\</c> oppure locale accanto al <c>.cme</c> (il locale prevale).
    ///
    /// Default DELIBERATAMENTE VUOTO (disciplina H7): nessuna voce derivata viene generata finché non
    /// è configurata una regola, per non fatturare quantità con coefficienti inventati. Un esempio
    /// documentato è disponibile via <see cref="ExampleConfig"/>.
    /// </summary>
    public class DerivedRulesService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private const string FileName = "QTO_DerivedRules.json";

        private readonly string _globalDir;
        private string? _projectCmePath;

        public DerivedRulesService(string? globalDir = null, string? projectCmePath = null)
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

        /// <summary>Config effettiva: quella del progetto (accanto al .cme) se presente, altrimenti la globale, altrimenti vuota.</summary>
        public DerivedRulesConfig LoadEffective() => LoadForProject() ?? LoadGlobal();

        public DerivedRulesConfig LoadGlobal()
        {
            var path = Path.Combine(_globalDir, FileName);
            if (!File.Exists(path)) return new DerivedRulesConfig();
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<DerivedRulesConfig>(json) ?? new DerivedRulesConfig();
            }
            catch { return new DerivedRulesConfig(); }
        }

        public DerivedRulesConfig? LoadForProject()
        {
            if (string.IsNullOrEmpty(_projectCmePath)) return null;
            var dir = Path.GetDirectoryName(_projectCmePath);
            if (string.IsNullOrEmpty(dir)) return null;
            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<DerivedRulesConfig>(json);
            }
            catch { return null; }
        }

        public void SaveGlobal(DerivedRulesConfig config)
        {
            Directory.CreateDirectory(_globalDir);
            File.WriteAllText(Path.Combine(_globalDir, FileName), JsonSerializer.Serialize(config, JsonOptions));
        }

        /// <summary>Genera l'esempio globale se il file non esiste ancora (per aiutare la prima configurazione). Ritorna true se creato.</summary>
        public bool WriteExampleIfMissing()
        {
            var path = Path.Combine(_globalDir, FileName);
            if (File.Exists(path)) return false;
            SaveGlobal(ExampleConfig());
            return true;
        }

        /// <summary>
        /// Esempio documentato (NON attivo di default): armatura kg/mc su pilastri con gate anti-doppio
        /// su OST_Rebar, e casseforme mq/mc su volume con bias di sovrastima dichiarato. I coefficienti e
        /// i nomi parametro sono indicativi: vanno adattati al proprio prezzario/modello.
        /// </summary>
        public static DerivedRulesConfig ExampleConfig() => new DerivedRulesConfig
        {
            Version = 1,
            Rules = new List<DerivedRuleTemplate>
            {
                new DerivedRuleTemplate
                {
                    BindCategory = "Pilastri strutturali",
                    Code = "ARM.01",
                    Um = "kg",
                    BaseMeasure = "volume",
                    CoefficientParameter = "Incidenza Armatura",   // kg/mc letto dall'elemento
                    FixedCoefficient = null,
                    // Nome della categoria Revit (locale-dipendente) la cui presenza in selezione
                    // indica che l'armatura è già modellata → la derivata viene soppressa.
                    AntiDoubleCategory = "Armature strutturali",
                    AntiDoubleLabel = "armatura",
                    ShortDescription = "Acciaio in barre per c.a.",
                    ExtendedDescription = "Acciaio in barre ad aderenza migliorata per cemento armato.",
                },
                new DerivedRuleTemplate
                {
                    BindCategory = "Pilastri strutturali",
                    Code = "CAS.01",
                    Um = "mq",
                    BaseMeasure = "area",
                    FixedCoefficient = 1.0,   // mq casseforme per mq superficie
                    OverestimateBias = true,
                    OverestimateNote = "stima parametrica in eccesso ai giunti fra getti: verificare",
                    ShortDescription = "Casseforme per getti in c.a.",
                    ExtendedDescription = "Casseforme rette per getti di cemento armato in elevazione.",
                },
            }
        };
    }
}
