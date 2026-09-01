using System;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Computo.Extraction
{
    /// <summary>
    /// La fase decide COSA è una quantità, non solo quanto: un elemento demolito nella fase misurata
    /// appartiene alle voci di demolizione, uno nuovo alle nuove opere, uno esistente che resta di norma
    /// non è contabilizzato. Revit risponde con <c>Element.GetPhaseStatus(phaseId)</c>. Portato dal
    /// modulo Computo di Pulse.
    ///
    /// Puro: normalizza ciò che scrive il chiamante (IT o EN) e decide se uno stato è in scope. Uno stato
    /// non riconosciuto è RIFIUTATO, non scartato — scartarlo allargherebbe in silenzio lo scope di un
    /// computo, e contabilizzare demolizioni come nuove opere è esattamente l'errore che non deve poter
    /// accadere in silenzio.
    /// </summary>
    public static class ComputoPhaseFilter
    {
        public static readonly string[] ValidStatuses =
            { "new", "demolished", "existing", "temporary", "past", "future", "none" };

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["new"] = "new", ["nuovo"] = "new", ["nuova"] = "new", ["nuove"] = "new", ["nuovi"] = "new",
            ["demolished"] = "demolished", ["demolito"] = "demolished", ["demolita"] = "demolished",
            ["demolizione"] = "demolished", ["demolizioni"] = "demolished",
            ["existing"] = "existing", ["esistente"] = "existing", ["esistenti"] = "existing",
            ["temporary"] = "temporary", ["temporaneo"] = "temporary", ["provvisorio"] = "temporary",
            ["provvisionale"] = "temporary", ["provvisionali"] = "temporary",
            ["past"] = "past", ["passato"] = "past",
            ["future"] = "future", ["futuro"] = "future",
            ["none"] = "none", ["nessuno"] = "none",
        };

        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["new"] = "Nuove opere",
            ["demolished"] = "Demolizioni",
            ["temporary"] = "Opere provvisionali",
            ["existing"] = "Esistente",
            ["past"] = "Preesistente demolito",
            ["future"] = "Fasi successive",
            ["none"] = "Senza fase",
        };

        /// <summary>Il nome canonico dello stato, o null quando non è uno che Revit conosce.</summary>
        public static string? NormalizeStatus(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Aliases.TryGetValue(raw.Trim(), out var canonical) ? canonical : null;
        }

        /// <summary>Gli stati scritti dal chiamante che non significano nulla — riportati, mai ignorati in silenzio.</summary>
        public static IEnumerable<string> UnrecognisedStatuses(IEnumerable<string> requested) =>
            (requested ?? Enumerable.Empty<string>()).Where(s => NormalizeStatus(s) == null);

        /// <summary>True quando lo stato dell'elemento è in scope. Nessun filtro ⇒ tutto lo è.</summary>
        public static bool Matches(string status, IReadOnlyCollection<string>? filter)
        {
            if (filter == null || filter.Count == 0) return true;
            var canonical = NormalizeStatus(status) ?? status;
            return filter.Any(f => string.Equals(NormalizeStatus(f) ?? f, canonical, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Etichetta leggibile per raggruppare le voci per fase (demolizioni e nuove opere distinguibili a colpo d'occhio).</summary>
        public static string GroupLabel(string status)
        {
            var canonical = NormalizeStatus(status) ?? status ?? "";
            return Labels.TryGetValue(canonical, out var label) ? label : canonical;
        }
    }
}
