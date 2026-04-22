using FluentAssertions;
using QtoRevitPlugin.Models;
using Xunit;

namespace QtoRevitPlugin.Tests.Models
{
    public class PriceItemTests
    {
        [Fact]
        public void ToString_ReturnsCodeAndShortDesc()
        {
            var item = new PriceItem
            {
                Code = "A.02.001",
                ShortDesc = "Muratura mattoni pieni",
                Description = "Muratura in mattoni pieni faccia a vista..."
            };

            item.ToString().Should().Be("A.02.001 – Muratura mattoni pieni");
        }

        [Fact]
        public void ToString_FallsBackToDescription_WhenShortDescEmpty()
        {
            var item = new PriceItem
            {
                Code = "B.01.003",
                ShortDesc = "",
                Description = "Intonaco civile interno a due strati"
            };

            item.ToString().Should().Be("B.01.003 – Intonaco civile interno a due strati");
        }

        [Fact]
        public void IsNP_DefaultsFalse()
        {
            var item = new PriceItem();
            item.IsNP.Should().BeFalse();
        }
    }
}
