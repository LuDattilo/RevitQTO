using System.Collections.Generic;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public interface IWbsService
    {
        IReadOnlyList<WbsNode> GetAll(int documentId, string? kind = null);
        WbsNode Add(int documentId, string kind, int? parentId, string desSintetica);
        void Update(WbsNode node);
        void Delete(int nodeId);
    }
}
