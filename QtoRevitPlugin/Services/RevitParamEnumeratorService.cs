using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Enumera i parametri disponibili su <see cref="ProjectInfo"/> del documento attivo,
    /// classificandoli in BuiltIn (Name/ClientName/...) vs Shared (parametri condivisi custom).
    /// Usato dal dropdown della scheda Informazioni Progetto per offrire all'utente
    /// la lista dei possibili "source" da cui ereditare ogni campo CME.
    /// </summary>
    public static class RevitParamEnumeratorService
    {
        /// <summary>Descrittore di un parametro disponibile per il mapping.</summary>
        public sealed class ParamEntry
        {
            public string ParamName { get; set; } = "";
            /// <summary>Label mostrata nel dropdown (es. "ClientName — Committente").</summary>
            public string DisplayName { get; set; } = "";
            public bool IsBuiltIn { get; set; }
            /// <summary>Valore letto dal ProjectInfo al momento dell'enumerazione (preview).
            /// null se il parametro non ha valore.</summary>
            public string? CurrentValue { get; set; }
        }

        /// <summary>Elenco dei parametri "BuiltIn" di ProjectInformation sempre disponibili
        /// (indipendentemente dal template). Nome API Revit + label italiana.</summary>
        private static readonly (string ParamName, string Label)[] BuiltInDefs = new[]
        {
            ("Name",                    "Name — Nome progetto"),
            ("ClientName",              "ClientName — Cliente/Committente"),
            ("Address",                 "Address — Indirizzo"),
            ("BuildingName",            "BuildingName — Nome edificio"),
            ("Number",                  "Number — Numero progetto"),
            ("Author",                  "Author — Autore"),
            ("IssueDate",               "IssueDate — Data emissione"),
            ("Status",                  "Status — Stato progetto"),
            ("OrganizationName",        "OrganizationName — Nome organizzazione"),
            ("OrganizationDescription", "OrganizationDescription — Descrizione org.")
        };

        /// <summary>
        /// Ritorna tutti i parametri di ProjectInformation: prima i BuiltIn (ordine fisso,
        /// curato), poi i Shared Parameter custom (ordinati alfabeticamente).
        /// Filtra solo parametri di tipo testo (StorageType.String) perché i campi CME
        /// sono tutti stringhe.
        /// </summary>
        public static IReadOnlyList<ParamEntry> GetAllParams(Document doc)
        {
            var result = new List<ParamEntry>();
            if (doc == null) return result;
            var pi = doc.ProjectInformation;
            if (pi == null) return result;

            var builtInNames = new HashSet<string>(
                BuiltInDefs.Select(d => d.ParamName),
                System.StringComparer.OrdinalIgnoreCase);

            // BuiltIn — ordine curato, solo quelli di tipo stringa
            foreach (var (name, label) in BuiltInDefs)
            {
                var p = pi.LookupParameter(name);
                if (p == null) continue;
                if (p.StorageType != StorageType.String) continue;

                result.Add(new ParamEntry
                {
                    ParamName = name,
                    DisplayName = label,
                    IsBuiltIn = true,
                    CurrentValue = ReadValue(p)
                });
            }

            // Shared / Custom — tutti i parametri non-BuiltIn di tipo stringa
            var customs = new List<ParamEntry>();
            foreach (Parameter p in pi.Parameters)
            {
                if (p.Definition?.Name == null) continue;
                if (p.StorageType != StorageType.String) continue;

                var name = p.Definition.Name;
                if (builtInNames.Contains(name)) continue;

                customs.Add(new ParamEntry
                {
                    ParamName = name,
                    DisplayName = p.IsShared ? $"{name} (parametro condiviso)" : $"{name} (parametro custom)",
                    IsBuiltIn = false,
                    CurrentValue = ReadValue(p)
                });
            }

            // Ordina custom alfabeticamente (case-insensitive)
            customs.Sort((a, b) => string.Compare(a.ParamName, b.ParamName,
                System.StringComparison.OrdinalIgnoreCase));
            result.AddRange(customs);

            return result;
        }

        /// <summary>
        /// Legge il valore corrente di un parametro dato ParamName salvato e flag IsBuiltIn.
        /// Cerca prima per nome esatto; se il parametro non esiste o non ha valore ritorna null.
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
