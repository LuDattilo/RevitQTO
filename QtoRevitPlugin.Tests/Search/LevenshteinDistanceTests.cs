using FluentAssertions;
using QtoRevitPlugin.Search;
using Xunit;

namespace QtoRevitPlugin.Tests.Search
{
    public class LevenshteinDistanceTests
    {
        [Theory]
        [InlineData("", "", 0)]
        [InlineData("a", "", 1)]
        [InlineData("", "b", 1)]
        [InlineData("kitten", "sitting", 3)]
        [InlineData("scavo", "scavi", 1)]
        [InlineData("calcestruzzo", "calcestrusso", 2)] // doppia sostituzione zz→ss
        public void Compute_ReturnsClassicEditDistance(string a, string b, int expected)
        {
            LevenshteinDistance.Compute(a, b).Should().Be(expected);
        }

        [Theory]
        [InlineData("ABC", "abc")]
        [InlineData("Calcestruzzo", "CALCESTRUZZO")]
        public void Compute_IsCaseInsensitive(string a, string b)
        {
            LevenshteinDistance.Compute(a, b).Should().Be(0);
        }

        [Fact]
        public void Compute_NullArgs_TreatedAsEmpty()
        {
            LevenshteinDistance.Compute(null, "abc").Should().Be(3);
            LevenshteinDistance.Compute("abc", null).Should().Be(3);
            LevenshteinDistance.Compute(null, null).Should().Be(0);
        }

        [Fact]
        public void Similarity_IdenticalStrings_ReturnsOne()
        {
            LevenshteinDistance.Similarity("scavo", "scavo").Should().Be(1.0);
        }

        [Fact]
        public void Similarity_CompletelyDifferent_ReturnsLow()
        {
            // "abc" vs "xyz" → dist=3, maxLen=3 → sim=0
            LevenshteinDistance.Similarity("abc", "xyz").Should().Be(0.0);
        }

        [Fact]
        public void Similarity_EmptyStrings_ReturnsOne()
        {
            LevenshteinDistance.Similarity("", "").Should().Be(1.0);
        }

        [Fact]
        public void Similarity_SmallTypo_HighSimilarity()
        {
            // "calcestruzzo" vs "calcestrusso" → dist=2 (zz→ss), maxLen=12 → sim ≈ 0.833
            LevenshteinDistance.Similarity("calcestruzzo", "calcestrusso")
                .Should().BeApproximately(10.0 / 12.0, 0.001);
        }

        [Fact]
        public void Similarity_OneCharDiff_HighSimilarity()
        {
            // "scavo" vs "scavi" → dist=1, maxLen=5 → sim = 0.8
            LevenshteinDistance.Similarity("scavo", "scavi")
                .Should().BeApproximately(4.0 / 5.0, 0.001);
        }

        [Fact]
        public void Similarity_Symmetric_ABEqualsBA()
        {
            var ab = LevenshteinDistance.Similarity("scavo", "scavi");
            var ba = LevenshteinDistance.Similarity("scavi", "scavo");
            ab.Should().Be(ba);
        }
    }
}
