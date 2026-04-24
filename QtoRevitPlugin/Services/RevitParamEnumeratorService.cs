using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Enumera i parametri disponibili su <see cref="ProjectInfo"/> del documento attivo,
    /// classificandoli in BuiltIn vs Shared/project. Accede ai built-in via
    /// <see cref="BuiltInParameter"/> (language-independent) per evitare dipendenza
    /// dalla localizzazione di Revit (IT vs EN).
    /// </summary>
    public static class RevitParamEnumeratorService
    {
        /// <summary>Descrittore di un parametro disponibile per il mapping.</summary>
        public sealed class ParamEntry
        {
            public string ParamName { get; set; } = "";
            /// <summary>Label mostrata nel dropdown.</summary>
            public string DisplayName { get; set; } = "";
            public bool IsBuiltIn { get; set; }
            /// <summary>Valore letto dal ProjectInfo al momento dell'enumerazione (preview).</summary>
            public string? CurrentValue { get; set; }
        }

        /// <summary>Parametri BuiltIn di ProjectInformation (language-independent).</summary>
        private static readonly (BuiltInParameter Bip, string Label)[] BuiltInDefs =
        {
            (BuiltInParameter.PROJECT_NAME,                      "Nome progetto (built-in)"),
            (BuiltInParameter.CLIENT_NAME,                       "Cliente/Committente (built-in)"),
            (BuiltInParameter.PROJECT_ADDRESS,                   "Indirizzo (built-in)"),
            (BuiltInParameter.PROJECT_BUILDING_NAME,             "Nome edificio (built-in)"),
            (BuiltInParameter.PROJECT_NUMBER,                    "Numero progetto (built-in)"),
            (BuiltInParameter.PROJECT_AUTHOR,                    "Autore (built-in)"),
            (BuiltInParameter.PROJECT_ISSUE_DATE,                "Data emissione (built-in)"),
            (BuiltInParameter.PROJECT_STATUS,                    "Stato progetto (built-in)"),
            (BuiltInParameter.PROJECT_ORGANIZATION_NAME,         "Nome organizzazione (built-in)"),
            (BuiltInParameter.PROJECT_ORGANIZATION_DESCRIPTION,  "Descrizione organizzazione (built-in)"),
        };

        /// <summary>
        /// Ritorna tutti i parametri di ProjectInformation: prima i BuiltIn (ordine fisso,
        /// accesso via BuiltInParameter per massima compatibilità IT/EN), poi i parametri
        /// condivisi/progetto aggiuntivi (ordinati alfabeticamente).
        /// Filtra solo parametri di tipo testo (StorageType.String).
        /// </summary>
        public static IReadOnlyList<ParamEntry> GetAllParams(Document doc)
        {
            var result = new List<ParamEntry>();
            if (doc == null) return result;
            var pi = doc.ProjectInformation;
            if (pi == null) return result;

            var addedIds = new HashSet<int>();

            // BuiltIn — accesso via BuiltInParameter (language-independent)
            foreach (var (bip, label) in BuiltInDefs)
            {
                var p = pi.get_Parameter(bip);
                if (p == null) continue;
                if (p.StorageType != StorageType.String) continue;

#if REVIT2025_OR_LATER
                var pid = (int)p.Id.Value;
#else
                var pid = p.Id.IntegerValue;
#endif
                if (!addedIds.Add(pid)) continue;

                result.Add(new ParamEntry
                {
                    ParamName = p.Definition?.Name ?? bip.ToString(),
                    DisplayName = label,
                    IsBuiltIn = true,
                    CurrentValue = ReadValue(p)
                });
            }

            // Shared / project params — tutti gli altri di tipo stringa non già aggiunti
            var customs = new List<ParamEntry>();
            foreach (Parameter p in pi.Parameters)
            {
                if (p.Definition?.Name == null) continue;
                if (p.StorageType != StorageType.String) continue;

#if REVIT2025_OR_LATER
                var pid = (int)p.Id.Value;
#else
                var pid = p.Id.IntegerValue;
#endif
                if (addedIds.Contains(pid)) continue;

                var name = p.Definition.Name;
                customs.Add(new ParamEntry
                {
                    ParamName = name,
                    DisplayName = p.IsShared
                        ? $"{name} (parametro condiviso)"
                        : $"{name} (parametro progetto)",
                    IsBuiltIn = false,
                    CurrentValue = ReadValue(p)
                });
            }

            customs.Sort((a, b) => string.Compare(a.ParamName, b.ParamName,
                System.StringComparison.OrdinalIgnoreCase));
            result.AddRange(customs);

            return result;
        }

        /// <summary>
        /// Legge il valore corrente di un parametro dal ProjectInfo per nome.
        /// </summary>
        public static string? ReadValue(Document doc, string paramName)
        {
            if (doc?.ProjectInformation == null || string.IsNullOrEmpty(paramName)) return null;
            var p = doc.ProjectInformation.LookupParameter(paramName);
            return ReadValue(p);
        }

        private static string? ReadValue(Parameter? p)
        {
            if (p == null || !p.HasValue) return null;
            return p.AsString() ?? p.AsValueString();
        }
    }
}
