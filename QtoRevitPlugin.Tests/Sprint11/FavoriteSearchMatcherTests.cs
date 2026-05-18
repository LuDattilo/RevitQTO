using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Search;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class FavoriteSearchMatcherTests
    {
        [Fact]
        public void Matches_QueryInDescription_ReturnsTrue()
        {
            var item = new FavoriteItem
            {
                Code = "A.01.001",
                ShortDesc = "Muratura",
                Description = "Demolizione controllata di tramezzi",
                ListName = "Prezzario Test"
            };

            FavoriteSearchMatcher.Matches(item, "tramezzi").Should().BeTrue();
        }

        [Fact]
        public void Matches_QueryMissing_ReturnsFalse()
        {
            var item = new FavoriteItem
            {
                Code = "A.01.001",
                ShortDesc = "Muratura",
                Description = "Demolizione controllata di tramezzi",
                ListName = "Prezzario Test"
            };

            FavoriteSearchMatcher.Matches(item, "impianto").Should().BeFalse();
        }
    }
}
