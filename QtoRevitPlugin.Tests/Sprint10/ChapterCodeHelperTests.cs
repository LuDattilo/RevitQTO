using FluentAssertions;
using QtoRevitPlugin.Models;
using System;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint10
{
    public class ChapterCodeHelperTests
    {
        [Theory]
        [InlineData(1, "A")]
        [InlineData(2, "B")]
        [InlineData(26, "Z")]
        public void ToAlpha_SingleChar_ForFirst26(int n, string expected)
        {
            ChapterCodeHelper.ToAlpha(n).Should().Be(expected);
        }

        [Theory]
        [InlineData(27, "AA")]
        [InlineData(28, "AB")]
        [InlineData(52, "AZ")]
        [InlineData(53, "BA")]
        [InlineData(702, "ZZ")]
        [InlineData(703, "AAA")]
        public void ToAlpha_DoubleAndTripleChar_ForOverflow(int n, string expected)
        {
            ChapterCodeHelper.ToAlpha(n).Should().Be(expected);
        }

        [Fact]
        public void ToAlpha_ThrowsOnZero()
        {
            var act = () => ChapterCodeHelper.ToAlpha(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void ToAlpha_ThrowsOnNegative()
        {
            var act = () => ChapterCodeHelper.ToAlpha(-5);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
