using System;
using System.IO;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Helper filesystem per gestire i file Shared Parameters di Revit.
    /// Vive in <c>QtoRevitPlugin.Core</c> (netstandard2.0) senza dipendenze Revit API
    /// per essere testabile automaticamente. Il wrapper <c>SharedParameterWriterService</c>
    /// (nel plugin) riusa questi helper prima di chiamare l'API Revit.
    /// </summary>
    public static class SharedParameterFileHelper
    {
        /// <summary>Header che Revit si aspetta per validare un file SP.</summary>
        public const string SpFileHeader =
            "# This is a Revit shared parameter file.\r\n" +
            "# Do not edit manually — managed by CME plugin.\r\n";

        /// <summary>Nome del file SP dedicato CME nella cartella AppData del plugin.</summary>
        public const string CmeSpFileName = "CME_SharedParameters.txt";

        /// <summary>
        /// Ritorna il path completo del file SP CME dedicato
        /// (<c>%AppData%\QtoPlugin\CME_SharedParameters.txt</c>).
        /// Crea la directory <c>QtoPlugin</c> se mancante (comportamento idempotente).
        /// </summary>
        public static string GetCmeSpFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "QtoPlugin");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, CmeSpFileName);
        }

        /// <summary>
        /// Assicura che il file SP esista nel path specificato (lo crea con header
        /// minimale valido se manca). Crea ricorsivamente le directory intermedie.
        /// Safe-no-op per path null o vuoto.
        /// </summary>
        public static void EnsureSpFileExists(string? spPath)
        {
            if (string.IsNullOrWhiteSpace(spPath)) return;

            var dir = Path.GetDirectoryName(spPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(spPath!))
            {
                File.WriteAllText(spPath!, SpFileHeader);
            }
        }
    }
}
