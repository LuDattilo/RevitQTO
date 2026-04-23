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
    }

    public interface IFavoritesRepository
    {
        FavoriteSet LoadGlobal();
        void SaveGlobal(FavoriteSet set);
        FavoriteSet? LoadForProject(string cmePath);
        void SaveForProject(string cmePath, FavoriteSet set);
    }
}
