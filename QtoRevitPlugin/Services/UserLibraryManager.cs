using QtoRevitPlugin.Data;
using System;
using System.IO;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Libreria globale (per-utente) dei listini prezzi importati.
    /// Il DB vive in <c>%AppData%\QtoPlugin\UserLibrary.db</c>: un solo file per utente,
    /// condiviso da TUTTI i computi .cme aperti su qualsiasi progetto Revit.
    ///
    /// Modello: il .cme contiene solo le assegnazioni (QtoAssignments), mentre PriceLists
    /// e PriceItems stanno qui. In questo modo il listino Firenze 2025 (67MB) viene
    /// importato UNA sola volta, non duplicato per ogni .cme.
    ///
    /// Schema: riusa <see cref="QtoRepository"/> e <c>DatabaseSchema.InitialStatements</c>
    /// (tabelle Sessions/QtoAssignments/etc. ci sono ma non usate qui — no-op).
    /// </summary>
    public class UserLibraryManager : IDisposable
    {
        public static string DefaultLibraryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QtoPlugin",
            "UserLibrary.db");

        public QtoRepository Library { get; }
        public string LibraryPath { get; }
        private bool _disposed;

        public UserLibraryManager() : this(DefaultLibraryPath) { }

        public UserLibraryManager(string libraryPath)
        {
            LibraryPath = libraryPath;
            var dir = Path.GetDirectoryName(libraryPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Library = new QtoRepository(libraryPath);
        }

        /// <summary>Flush esplicito — SQLite auto-flusha ma utile per commit manuali.</summary>
        public void Flush() { /* no-op: QtoRepository usa transazioni auto-committed per insert batch */ }

        public void Dispose()
        {
            if (_disposed) return;
            try { Library?.Dispose(); } catch { /* best effort */ }
            _disposed = true;
        }
    }
}
