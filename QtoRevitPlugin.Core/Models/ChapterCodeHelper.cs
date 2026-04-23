using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Helper puri per codici ComputoChapter. Estratti in Core per testabilità
    /// senza dipendenze Revit/WPF.
    /// </summary>
    public static class ChapterCodeHelper
    {
        /// <summary>
        /// Converte un indice 1-based in suffisso alfabetico Excel-like:
        /// 1→A, 2→B, ..., 26→Z, 27→AA, 28→AB, ..., 52→AZ, 53→BA, ...
        ///
        /// <para>Fix MED-C1 (code review Sprint 10): il codice precedente usava
        /// <c>(char)('A' + idx - 1)</c> producendo caratteri non stampabili per
        /// idx > 26 (es. 27 → '[', ASCII 91). Un computo DEI Opere Edili ha
        /// spesso 30+ capitoli di livello 2 e generava codici corrotti.</para>
        /// </summary>
        public static string ToAlpha(int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), "n must be >= 1");
            if (n <= 26) return ((char)('A' + n - 1)).ToString();
            return ToAlpha((n - 1) / 26) + (char)('A' + (n - 1) % 26);
        }
    }
}
