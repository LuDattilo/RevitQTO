using System;
using System.IO;
using System.Text;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Logger diagnostico per crash del plugin. Scrive tutto in %AppData%\QtoPlugin\startup.log.
    /// Sopravvive al crash del processo Revit (flush immediato dopo ogni write).
    /// </summary>
    public static class CrashLogger
    {
        private static readonly object _lock = new();
        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QtoPlugin", "startup.log");

        /// <summary>Installa handler globali su AppDomain e TaskScheduler.</summary>
        public static void InstallGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                WriteException("AppDomain.UnhandledException", ex);
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                WriteException("TaskScheduler.UnobservedTaskException", args.Exception);
                args.SetObserved();
            };
            System.Windows.Threading.Dispatcher.CurrentDispatcher.UnhandledException += (_, args) =>
            {
                WriteException("Dispatcher.UnhandledException", args.Exception);
                args.Handled = true;   // prevent process shutdown from WPF side
            };
        }

        public static void Info(string message)
        {
            Write($"[INFO ] {message}");
        }

        public static void Warn(string message, Exception? ex = null)
        {
            Write($"[WARN ] {message}" + (ex != null ? $"\n       {ex.GetType().Name}: {ex.Message}" : ""));
        }

        public static void WriteException(string context, Exception? ex)
        {
            if (ex == null)
            {
                Write($"[ERROR] {context}: <null exception>");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[ERROR] {context}");
            var current = ex;
            int depth = 0;
            while (current != null && depth < 10)
            {
                sb.AppendLine($"        [{depth}] {current.GetType().FullName}: {current.Message}");
                sb.AppendLine($"             {current.StackTrace}");
                current = current.InnerException;
                depth++;
            }
            Write(sb.ToString());
        }

        private static void Write(string line)
        {
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(LogPath)!;
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(LogPath,
                        $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Non propagare mai errori del logger
            }
        }

        /// <summary>Truncate log all'avvio (solo l'ultima sessione). Evita file gigante.</summary>
        public static void Reset()
        {
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(LogPath)!;
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(LogPath,
                        $"=== QTO Plugin startup {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ==={Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
