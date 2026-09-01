using System;

namespace QtoRevitPlugin.Computo
{
    /// <summary>
    /// Deriva <c>chapter</c>/<c>cam</c> da un codice di prezzario, es.
    /// <c>TOS26_02CAM.B07.005.002</c> → chapter <c>"02CAM"</c>, cam <c>true</c>. Portato dal modulo
    /// Computo di Pulse.
    ///
    /// Regola generica, nessuna conoscenza della numerazione di un prezzario specifico: <c>chapter</c>
    /// è la sottostringa fra il PRIMO <c>_</c> e il primo <c>.</c> successivo; <c>cam</c> è vero se quel
    /// capitolo contiene "CAM" (case-insensitive). Se il codice è null/vuoto, senza <c>_</c>, o senza
    /// <c>.</c> dopo l'<c>_</c>, entrambi gli output sono null — MAI un valore inventato (disciplina H7:
    /// una forma non riconosciuta è riportata come tale, non indovinata).
    ///
    /// Conseguenza per CME: il riconoscimento CAM funziona solo se il codice del prezzario incorpora il
    /// marcatore (tipico dei prezzari regionali, es. Toscana <c>TOS26_..CAM..</c>). Codici senza quella
    /// struttura risultano "non classificabili", non "non-CAM".
    /// </summary>
    public static class PricelistCodeClassifier
    {
        public static void Classify(string? code, out string? chapter, out bool? cam)
        {
            chapter = null;
            cam = null;

            if (string.IsNullOrEmpty(code))
                return;

            var underscoreIndex = code!.IndexOf('_');
            if (underscoreIndex < 0)
                return;

            var dotIndex = code.IndexOf('.', underscoreIndex + 1);
            if (dotIndex < 0)
                return;

            chapter = code.Substring(underscoreIndex + 1, dotIndex - underscoreIndex - 1);
            cam = chapter.IndexOf("CAM", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
