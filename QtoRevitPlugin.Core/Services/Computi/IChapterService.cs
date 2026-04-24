using System.Collections.Generic;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public interface IChapterService
    {
        IReadOnlyList<ChapterNode> GetAll(int documentId);
        ChapterNode AddSuperChapter(int documentId, string codice, string desSintetica);
        ChapterNode AddChapter(int documentId, int parentSpCapId, string codice, string desSintetica);
        ChapterNode AddSubChapter(int documentId, int parentCapId, string codice, string desSintetica);
        void Update(ChapterNode node);
        void Delete(int nodeId);
    }
}
