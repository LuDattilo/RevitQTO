using System;
using System.IO;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Plan C-6 debug: logger append-only per diagnosticare il flusso Assegna EP.
    /// File: %TEMP%\QtoRevitPlugin\assignep.log — ruota a 5 MB.
    /// Thread-safe via lock (chiamate non frequenti, overhead irrilevante).
    /// </summary>
    public static class AssignEpLogger
    {
        private static readonly object _sync = new object();
        private const long MaxBytes = 5 * 1024 * 1024;

        public static string LogPath { get; } =
            Path.Combine(Path.GetTempPath(), "QtoRevitPlugin", "assignep.log");

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
            catch
            {
                // Logger non deve mai throware. Best-effort.
            }
        }
    }
}
