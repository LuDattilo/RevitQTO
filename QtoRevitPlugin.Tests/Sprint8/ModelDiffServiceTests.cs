using QtoRevitPlugin.Services;
using System.Collections.Generic;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint8
{
    /// <summary>
    /// Tests for the hash logic extracted into ElementHasher (Core, no Revit API).
    /// The test project cannot reference QtoRevitPlugin (main) due to Revit API deps,
    /// so we test ElementHasher.ComputeHash directly — ModelDiffService.ComputeHashStatic
    /// delegates to the same method, making coverage equivalent.
    /// </summary>
    public class ModelDiffServiceTests
    {
        [Fact]
        public void ComputeHash_SameInput_SameHash()
        {
            var hash1 = ElementHasher.ComputeHash("elem-001", new List<(string, double)> { ("Area", 12.5) });
            var hash2 = ElementHasher.ComputeHash("elem-001", new List<(string, double)> { ("Area", 12.5) });
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void ComputeHash_DifferentArea_DifferentHash()
        {
            var hash1 = ElementHasher.ComputeHash("elem-001", new List<(string, double)> { ("Area", 12.5) });
            var hash2 = ElementHasher.ComputeHash("elem-001", new List<(string, double)> { ("Area", 13.0) });
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void ComputeHash_IsTwelveHexChars()
        {
            var hash = ElementHasher.ComputeHash("elem-001", new List<(string, double)> { ("Area", 5.0) });
            Assert.Equal(12, hash.Length);
            Assert.Matches("^[0-9A-F]+$", hash);
        }
    }
}
