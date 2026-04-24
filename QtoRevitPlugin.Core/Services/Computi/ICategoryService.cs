using System.Collections.Generic;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public interface ICategoryService
    {
        IReadOnlyList<CategoryNode> GetAll(int documentId);
        CategoryNode AddSuperCategory(int documentId, string codice, string desSintetica);
        CategoryNode AddCategory(int documentId, int parentSpCatId, string codice, string desSintetica);
        CategoryNode AddSubCategory(int documentId, int parentCatId, string codice, string desSintetica);
        void Update(CategoryNode node);
        void Delete(int nodeId);
    }
}
