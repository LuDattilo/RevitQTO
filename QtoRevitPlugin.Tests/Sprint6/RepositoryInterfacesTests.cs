using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.Collections.Generic;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint6
{
    public class RepositoryInterfacesTests
    {
        [Fact]
        public void IQtoRepository_HasInsertAssignment()
        {
            var type = typeof(IQtoRepository);
            var method = type.GetMethod("InsertAssignment");
            Assert.NotNull(method);
        }

        [Fact]
        public void IFavoritesRepository_HasLoadGlobal()
        {
            var type = typeof(IFavoritesRepository);
            var method = type.GetMethod("LoadGlobal");
            Assert.NotNull(method);
        }

        [Fact]
        public void IPriceListRepository_HasGetItems()
        {
            var type = typeof(IPriceListRepository);
            var method = type.GetMethod("GetItems");
            Assert.NotNull(method);
        }
    }
}
