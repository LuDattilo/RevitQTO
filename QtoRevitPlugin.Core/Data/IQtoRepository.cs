using QtoRevitPlugin.Models;
using System.Collections.Generic;

namespace QtoRevitPlugin.Data
{
    public interface IQtoRepository
    {
        int InsertSession(WorkSession session);
        void UpdateSession(WorkSession session);
        WorkSession? GetSession(int sessionId);

        int InsertAssignment(QtoAssignment assignment);
        void UpdateAssignment(QtoAssignment assignment);
        IReadOnlyList<QtoAssignment> GetAssignments(int sessionId);

        void AppendChangeLog(ChangeLogEntry entry);
        IReadOnlyList<ChangeLogEntry> GetChangeLog(int sessionId);

        void UpsertSnapshot(ElementSnapshot snapshot);
        IReadOnlyList<ElementSnapshot> GetSnapshots(int sessionId);

        // ComputoChapter CRUD (Sprint 9)
        int InsertComputoChapter(ComputoChapter ch);
        void UpdateComputoChapter(ComputoChapter ch);
        void DeleteComputoChapter(int chapterId);
        System.Collections.Generic.IReadOnlyList<ComputoChapter> GetComputoChapters(int sessionId);

        // Reconciliation batch (Sprint 9)
        void AcceptDiffBatch(System.Collections.Generic.IReadOnlyList<SupersedeOp> ops);

        // ProjectInfo (Sprint 10): metadati computo per intestazione export XPWE/PDF/Excel
        ProjectInfo? GetProjectInfo(int sessionId);
        void UpsertProjectInfo(ProjectInfo info);

        // SoaCategory (Sprint 10 step 2 · v8): codici OG/OS normativi (read-only).
        // Seedati al primo avvio da SoaCategorySeed. Utilizzati nel ComboBox della
        // Struttura Computo per assegnare OG/OS ai nodi.
        System.Collections.Generic.IReadOnlyList<SoaCategory> GetSoaCategories();

        // UserFavorites (v10 · UserLibrary.db). Lista preferiti utente globale.
        System.Collections.Generic.IReadOnlyList<UserFavorite> GetFavorites();
        int AddFavorite(UserFavorite fav);
        void RemoveFavorite(int id);
        bool IsFavorite(string code, int? listId);

        /// <summary>
        /// Ritorna l'insieme degli EpCode usati attivamente nel computo (QtoAssignments
        /// con AuditStatus='Active') per la sessione data. Usato per marcare i preferiti
        /// come "Usato/Non usato" nel panel Preferiti e per il bulk "Rimuovi inutilizzati".
        /// Ritorna un HashSet per lookup O(1) lato chiamante.
        /// </summary>
        System.Collections.Generic.HashSet<string> GetUsedEpCodes(int sessionId);

        /// <summary>
        /// Bulk-delete dei preferiti i cui Id sono nella lista. Esegue in una singola
        /// transazione. NON tocca il listino (UserFavorites vive in UserLibrary.db ma
        /// è una tabella separata da PriceItems). Ritorna il numero di righe cancellate.
        /// </summary>
        int RemoveFavorites(System.Collections.Generic.IEnumerable<int> favoriteIds);
    }

    public interface IFavoritesRepository
    {
        FavoriteSet LoadGlobal();
        void SaveGlobal(FavoriteSet set);
        FavoriteSet? LoadForProject(string cmePath);
        void SaveForProject(string cmePath, FavoriteSet set);
    }
}
