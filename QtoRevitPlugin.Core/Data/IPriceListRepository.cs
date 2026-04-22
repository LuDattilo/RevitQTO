using QtoRevitPlugin.Models;
using System.Collections.Generic;

namespace QtoRevitPlugin.Data
{
    public interface IPriceListRepository
    {
        IReadOnlyList<PriceList> GetAllLists();
        IReadOnlyList<PriceItem> GetItems(int listId);
        PriceItem? GetItem(string code);
    }
}
