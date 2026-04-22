using System;
using FluentAssertions;
using QtoRevitPlugin.Models;
using Xunit;

namespace QtoRevitPlugin.Tests.Models
{
    public class QtoElementDataTests
    {
        [Fact]
        public void Default_Ctor_HasSensibleValues()
        {
            var d = new QtoElementData();

            d.AssignedEpCodes.Should().NotBeNull().And.BeEmpty();
            d.Source.Should().Be("RevitElement");
            d.ExclusionReason.Should().BeNull();
            d.LastTagged.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void IsUntagged_True_WhenNoEpAndNoExclusion()
        {
            var d = new QtoElementData();

            d.IsUntagged.Should().BeTrue();
            d.IsExcluded.Should().BeFalse();
        }

        [Fact]
        public void IsUntagged_False_WhenHasAssignedEp()
        {
            var d = new QtoElementData { AssignedEpCodes = new[] { "A.01.001" } };

            d.IsUntagged.Should().BeFalse();
            d.IsExcluded.Should().BeFalse();
        }

        [Fact]
        public void IsExcluded_True_WhenExclusionReasonSet()
        {
            var d = new QtoElementData { ExclusionReason = "escluso dal DL" };

            d.IsExcluded.Should().BeTrue();
            d.IsUntagged.Should().BeFalse();
        }

        [Fact]
        public void IsExcluded_False_WhenExclusionEmptyString()
        {
            var d = new QtoElementData { ExclusionReason = "" };

            d.IsExcluded.Should().BeFalse();
            d.IsUntagged.Should().BeTrue();
        }

        [Fact]
        public void MultiEp_StoresAllCodesInOrder()
        {
            var d = new QtoElementData
            {
                AssignedEpCodes = new[] { "A.01.001", "B.02.003", "C.05.999" }
            };

            d.AssignedEpCodes.Should().HaveCount(3);
            d.AssignedEpCodes[0].Should().Be("A.01.001");
            d.AssignedEpCodes[2].Should().Be("C.05.999");
        }

        [Theory]
        [InlineData("RevitElement")]
        [InlineData("Room")]
        [InlineData("Manual")]
        public void Source_AcceptsAllThreeSourceTypes(string src)
        {
            var d = new QtoElementData { Source = src };
            d.Source.Should().Be(src);
        }
    }
}
