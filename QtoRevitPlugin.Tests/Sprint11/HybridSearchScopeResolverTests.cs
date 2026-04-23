using FluentAssertions;
using QtoRevitPlugin.Search;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class HybridSearchScopeResolverTests
    {
        [Fact]
        public void Resolve_NoActivePriceList_AllUsesProjectAndPersonalFavoritesOnly()
        {
            var resolver = new HybridSearchScopeResolver();

            var resolved = resolver.Resolve(HybridSearchScope.All, hasActivePriceList: false);

            resolved.UseActivePriceList.Should().BeFalse();
            resolved.UseProjectFavorites.Should().BeTrue();
            resolved.UsePersonalFavorites.Should().BeTrue();
        }

        [Fact]
        public void Resolve_ActivePriceList_AllUsesEveryAvailableCorpus()
        {
            var resolver = new HybridSearchScopeResolver();

            var resolved = resolver.Resolve(HybridSearchScope.All, hasActivePriceList: true);

            resolved.UseActivePriceList.Should().BeTrue();
            resolved.UseProjectFavorites.Should().BeTrue();
            resolved.UsePersonalFavorites.Should().BeTrue();
        }
    }
}
