using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public interface IComputoDocumentService
    {
        ComputoDocument GetOrCreate(int workSessionId, int defaultTipo = 1);
        void Update(ComputoDocument doc);
    }
}
