using FluentAssertions;
using QtoRevitPlugin.AI;
using Xunit;

namespace QtoRevitPlugin.Tests.AI
{
    public class CosineSimilarityTests
    {
        [Fact]
        public void Identical_Vectors_Score_ApproxOne()
        {
            var a = new[] { 1f, 2f, 3f, 4f };
            CosineSimilarity.Compute(a, a).Should().BeApproximately(1f, 1e-5f);
        }

        [Fact]
        public void Orthogonal_Vectors_ScoreZero()
        {
            var a = new[] { 1f, 0f };
            var b = new[] { 0f, 1f };
            CosineSimilarity.Compute(a, b).Should().BeApproximately(0f, 1e-5f);
        }

        [Fact]
        public void Opposite_Vectors_ScoreMinusOne()
        {
            var a = new[] { 1f, 2f, 3f };
            var b = new[] { -1f, -2f, -3f };
            CosineSimilarity.Compute(a, b).Should().BeApproximately(-1f, 1e-5f);
        }

        [Fact]
        public void Different_Lengths_ReturnsZero()
        {
            var a = new[] { 1f, 2f };
            var b = new[] { 1f, 2f, 3f };
            CosineSimilarity.Compute(a, b).Should().Be(0f);
        }

        [Fact]
        public void Null_Or_Empty_ReturnsZero()
        {
            CosineSimilarity.Compute(null!, new[] { 1f }).Should().Be(0f);
            CosineSimilarity.Compute(new[] { 1f }, null!).Should().Be(0f);
            CosineSimilarity.Compute(new float[0], new float[0]).Should().Be(0f);
        }

        [Fact]
        public void ZeroVector_ReturnsApproxZero()
        {
            // Divisione per magnitudo 0 evitata via epsilon → score ≈ 0 (non NaN)
            var a = new[] { 0f, 0f, 0f };
            var b = new[] { 1f, 2f, 3f };
            var score = CosineSimilarity.Compute(a, b);
            score.Should().Be(0f);
            float.IsNaN(score).Should().BeFalse();
        }
    }
}
