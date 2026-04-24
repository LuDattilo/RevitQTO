using System;
using System.IO;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Plan C-6 debug: logger append-only per diagnosticare il flusso Assegna EP.
    /// File: Desktop\QtoRevitPlugin-assignep.log (il più visibile/accessibile per l'utente).
    /// Thread-safe via lock.
    /// </summary>
    public static class AssignEpLogger
    {
        private static readonly object _sync = new object();
        private const long MaxBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Path del log. Priorità: Desktop (massima visibilità per l'utente) → TEMP (fallback).
        /// Scelta fatta runtime perché GetFolderPath(Desktop) può fallire su sessioni particolari.
        /// </summary>
        public static string LogPath { get; } = ResolveLogPath();

        private static string ResolveLogPath()
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
                    return Path.Combine(desktop, "QtoRevitPlugin-assignep.log");
            }
            catch { }
            // Fallback 1: %APPDATA%\QtoRevitPlugin\assignep.log
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    var dir = Path.Combine(appData, "QtoRevitPlugin");
                    return Path.Combine(dir, "assignep.log");
                }
            }
            catch { }
            // Fallback 2: TEMP
            return Path.Combine(Path.GetTempPath(), "QtoRevitPlugin-assignep.log");
        }

        public static void Log(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                lock (_sync)
                {
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    {
                        var backup = LogPath + ".old";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(LogPath, backup);
                    }
                    var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    File.AppendAllText(LogPath, $"[{ts}] {message}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                // Non mangiamo l'errore in silenzio: lo scriviamo nel StatusMessage di chi chiama
                // via proprietà LastError. Se il logger stesso fallisce, l'utente vede comunque
                // il codice di errore e il path tentato nella UI.
                LastError = $"Log errore su '{LogPath}': {ex.GetType().Name} {ex.Message}";
            }
        }

        /// <summary>Se il logger fallisce, contiene il motivo. Letto dalla UI per debug.</summary>
        public static string? LastError { get; private set; }
    }
}
