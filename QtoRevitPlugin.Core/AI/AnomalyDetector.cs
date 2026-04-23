using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.AI
{
    /// <summary>
    /// Rileva quantità anomale tra le assegnazioni EP usando z-score statistico.
    /// <b>Non richiede AI/Ollama</b>: funziona sempre, in locale, su CPU.
    ///
    /// <para>Algoritmo (QTO-AI-Integration.md §7.5):</para>
    /// <list type="number">
    ///   <item>Raggruppa assignments per <c>EpCode</c> (stessa voce di listino).</item>
    ///   <item>Per ogni gruppo con ≥ 3 elementi, calcola media μ e deviazione std σ delle <c>Quantity</c>.</item>
    ///   <item>Per ogni elemento: <c>z = |Quantity - μ| / σ</c>.</item>
    ///   <item>Flag anomalia se <c>z &gt; 2.5</c> (Media) o <c>z &gt; 3.5</c> (Alta).</item>
    /// </list>
    /// <para>Esempio: 10 muri con volume ~15 m³ e uno con 200 m³ → z molto alto → flagged.</para>
    /// <para>Limiti: non rileva anomalie in gruppi piccoli (&lt; 3) né quando tutti
    /// gli elementi hanno la stessa quantity (σ ≈ 0).</para>
    /// </summary>
    public sealed class AnomalyDetector
    {
        /// <summary>Soglia z-score oltre la quale l'elemento è flagged come anomalo.</summary>
        public double Threshold { get; set; } = 2.5;

        /// <summary>Soglia z-score oltre la quale la severità è Alta (vs Media).</summary>
        public double HighSeverityThreshold { get; set; } = 3.5;

        /// <summary>Dimensione minima del campione per calcolare z-score.
        /// Con N=1 o N=2 non ha senso parlare di deviazione statistica.</summary>
        public int MinSampleSize { get; set; } = 3;

        /// <summary>
        /// Analizza le assegnazioni e ritorna la lista delle anomalie trovate.
        /// Ignora gruppi troppo piccoli (&lt; MinSampleSize) e gruppi senza variabilità
        /// (σ ≈ 0, tutti gli elementi con la stessa quantity).
        /// </summary>
        public IReadOnlyList<QuantityAnomaly> Detect(IReadOnlyList<QtoAssignment> assignments)
        {
            if (assignments == null || assignments.Count == 0)
                return new List<QuantityAnomaly>();

            var anomalies = new List<QuantityAnomaly>();

            foreach (var group in assignments
                .Where(a => !string.IsNullOrEmpty(a.EpCode))
                .GroupBy(a => a.EpCode))
            {
                var items = group.ToList();
                if (items.Count < MinSampleSize) continue;

                var quantities = items.Select(a => a.Quantity).ToList();

                double mean = quantities.Average();
                double variance = quantities.Select(q => (q - mean) * (q - mean)).Average();
                double stdDev = Math.Sqrt(variance);

                // Gruppi senza variabilità: tutti uguali → nessuna anomalia
                if (stdDev < 1e-6) continue;

                foreach (var a in items)
                {
                    double z = Math.Abs(a.Quantity - mean) / stdDev;
                    if (z <= Threshold) continue;

                    anomalies.Add(new QuantityAnomaly
                    {
                        UniqueId = a.UniqueId,
                        EpCode   = a.EpCode,
                        Quantity = a.Quantity,
                        Mean     = mean,
                        StdDev   = stdDev,
                        ZScore   = z,
                        Severity = z > HighSeverityThreshold ? AnomalySeverity.Alta : AnomalySeverity.Media,
                        Message  = $"Quantità {a.Quantity:F2} anomala " +
                                   $"(media gruppo {a.EpCode}: {mean:F2}, z={z:F1})"
                    });
                }
            }

            return anomalies;
        }
    }
}
