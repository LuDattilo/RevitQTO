using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Calcolatore dell'analisi prezzi per Nuovi Prezzi secondo D.Lgs. 36/2023 All. II.14.
    ///
    /// <para>Formula ufficiale (§I8 QTO-Implementazioni-v3.md):</para>
    /// <code>
    /// CT = Manodopera + Materiali + Noli + Trasporti
    /// NP = CT × (1 + SG%) × (1 + Utile%) × (1 − Ribasso%)
    /// </code>
    ///
    /// <para><b>Range ammessi dal codice</b>:</para>
    /// <list type="bullet">
    ///   <item><c>SpGenerali</c>: 13–17% (D.Lgs. 36/2023 All. II.14)</item>
    ///   <item><c>UtileImpresa</c>: 10% (valore fisso normativo)</item>
    ///   <item><c>RibassoAsta</c>: 0–100%, opzionale (Parere MIT n. 3545/2025 — i NP
    ///   non sono automaticamente soggetti al ribasso)</item>
    /// </list>
    ///
    /// <para>Il calculator è puro (static): nessuno stato, thread-safe, testabile.</para>
    /// </summary>
    public static class NuovoPrezzoCalculator
    {
        /// <summary>Minimo spese generali consentito (D.Lgs. 36/2023 All. II.14).</summary>
        public const double MinSpGenerali = 13.0;
        /// <summary>Massimo spese generali consentito.</summary>
        public const double MaxSpGenerali = 17.0;
        /// <summary>Utile impresa standard (D.Lgs. 36/2023 All. II.14).</summary>
        public const double DefaultUtileImpresa = 10.0;

        /// <summary>
        /// Calcola il Costo Totale come somma delle 4 componenti di cantiere.
        /// Valori negativi sono rifiutati (non avrebbe senso fisico).
        /// </summary>
        public static double ComputeCostoTotale(double manodopera, double materiali, double noli, double trasporti)
        {
            if (manodopera < 0) throw new ArgumentOutOfRangeException(nameof(manodopera), "Non può essere negativo.");
            if (materiali < 0) throw new ArgumentOutOfRangeException(nameof(materiali), "Non può essere negativo.");
            if (noli < 0) throw new ArgumentOutOfRangeException(nameof(noli), "Non può essere negativo.");
            if (trasporti < 0) throw new ArgumentOutOfRangeException(nameof(trasporti), "Non può essere negativo.");
            return manodopera + materiali + noli + trasporti;
        }

        /// <summary>
        /// Calcola il prezzo unitario finale applicando SG, Utile, Ribasso sulla CT.
        /// <para>Percentuali in [0, 100]. Ribasso può essere 0 (NP fuori dalla contrattazione).</para>
        /// </summary>
        public static double ComputeUnitPrice(double costoTotale, double spGenerali, double utileImpresa, double ribassoAsta)
        {
            if (costoTotale < 0) throw new ArgumentOutOfRangeException(nameof(costoTotale));
            if (spGenerali < 0 || spGenerali > 100) throw new ArgumentOutOfRangeException(nameof(spGenerali));
            if (utileImpresa < 0 || utileImpresa > 100) throw new ArgumentOutOfRangeException(nameof(utileImpresa));
            if (ribassoAsta < 0 || ribassoAsta > 100) throw new ArgumentOutOfRangeException(nameof(ribassoAsta));

            return costoTotale
                 * (1.0 + spGenerali / 100.0)
                 * (1.0 + utileImpresa / 100.0)
                 * (1.0 - ribassoAsta / 100.0);
        }

        /// <summary>
        /// Calcola il prezzo unitario a partire dal <see cref="NuovoPrezzo"/>.
        /// Equivalente alla property <c>UnitPrice</c> del model ma qui validato.
        /// </summary>
        public static double ComputeUnitPrice(NuovoPrezzo np)
        {
            if (np == null) throw new ArgumentNullException(nameof(np));
            var ct = ComputeCostoTotale(np.Manodopera, np.Materiali, np.Noli, np.Trasporti);
            return ComputeUnitPrice(ct, np.SpGenerali, np.UtileImpresa, np.RibassoAsta);
        }

        /// <summary>
        /// Verifica che un <see cref="NuovoPrezzo"/> rispetti i vincoli normativi.
        /// Ritorna la lista di violazioni (vuota = valido).
        /// </summary>
        public static IReadOnlyList<string> Validate(NuovoPrezzo np)
        {
            if (np == null) throw new ArgumentNullException(nameof(np));
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(np.Code))
                errors.Add("Il codice NP è obbligatorio (es. 'NP.001').");

            if (string.IsNullOrWhiteSpace(np.Description))
                errors.Add("La descrizione è obbligatoria.");

            if (np.Manodopera < 0 || np.Materiali < 0 || np.Noli < 0 || np.Trasporti < 0)
                errors.Add("Le componenti di costo (manodopera, materiali, noli, trasporti) non possono essere negative.");

            if (np.Manodopera + np.Materiali + np.Noli + np.Trasporti == 0)
                errors.Add("Il costo totale è zero: inserisci almeno una componente di costo.");

            if (np.SpGenerali < MinSpGenerali || np.SpGenerali > MaxSpGenerali)
                errors.Add($"Spese generali {np.SpGenerali}% fuori range normativo ({MinSpGenerali}–{MaxSpGenerali}% per D.Lgs. 36/2023 All. II.14).");

            if (np.UtileImpresa < 0 || np.UtileImpresa > 100)
                errors.Add($"Utile impresa {np.UtileImpresa}% non valido (0–100%).");

            if (np.RibassoAsta < 0 || np.RibassoAsta > 100)
                errors.Add($"Ribasso d'asta {np.RibassoAsta}% non valido (0–100%).");

            return errors;
        }

        /// <summary>
        /// True se il NP passa la validazione normativa (senza warning bloccanti).
        /// </summary>
        public static bool IsValid(NuovoPrezzo np)
        {
            if (np == null) return false;
            return Validate(np).Count == 0;
        }
    }
}
