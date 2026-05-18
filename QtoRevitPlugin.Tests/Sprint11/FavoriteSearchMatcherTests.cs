using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Search;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class FavoriteSearchMatcherTests
    {
        private static FavoriteItem Item(string code, string shortDesc = "", string desc = "", string listName = "") =>
            new FavoriteItem { Code = code, ShortDesc = shortDesc, Description = desc, ListName = listName };

        [Fact]
        public void Matches_EmptyQuery_ReturnsFalse()
        {
            FavoriteSearchMatcher.Matches(Item("A.01"), "").Should().BeFalse();
            FavoriteSearchMatcher.Matches(Item("A.01"), "   ").Should().BeFalse();
        }

        [Fact]
        public void Matches_ExactCode_ReturnsTrue()
        {
            FavoriteSearchMatcher.Matches(Item("A.01"), "A.01").Should().BeTrue();
        }

        [Fact]
        public void Matches_PartialCode_ReturnsTrue()
        {
            FavoriteSearchMatcher.Matches(Item("A.01.002"), "A.01").Should().BeTrue();
        }

        [Fact]
        public void Matches_CaseInsensitive_ReturnsTrue()
        {
            FavoriteSearchMatcher.Matches(Item("A.01", shortDesc: "Muratura"), "muratura").Should().BeTrue();
        }

        [Fact]
        public void Matches_ShortDesc_ReturnsTrue()
        {
            FavoriteSearchMatcher.Matches(Item("X", shortDesc: "Intonaco civile"), "intonaco").Should().BeTrue();
        }

        [Fact]
        public void Matches_Description_ReturnsTrue()
        {
            FavoriteSearchMatcher.Matches(Item("X", desc: "Calcestruzzo C25/30"), "C25").Should().BeTrue();
        }

        [Fact]
        public void Matches_ListName_ReturnsTrue()
        {
            FavoriteSearchMatcher.Matches(Item("X", listName: "DEI 2024"), "DEI").Should().BeTrue();
        }

        [Fact]
        public void Matches_NoField_ReturnsFalse()
        {
            FavoriteSearchMatcher.Matches(Item("A.01", "Muratura", "Muratura portante", "DEI"), "xyz123").Should().BeFalse();
        }

        [Fact]
        public void Matches_NullFields_DoesNotThrow()
        {
            var item = new FavoriteItem { Code = null!, ShortDesc = null!, Description = null!, ListName = null! };
            var act = () => FavoriteSearchMatcher.Matches(item, "test");
            act.Should().NotThrow();
            act().Should().BeFalse();
        }

        [Fact]
        public void Matches_QueryWithSpaces_TrimsAndMatches()
        {
            FavoriteSearchMatcher.Matches(Item("A.01"), "  A.01  ").Should().BeTrue();
        }
    }
}
