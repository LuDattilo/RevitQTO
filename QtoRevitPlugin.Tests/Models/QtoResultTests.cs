using FluentAssertions;
using QtoRevitPlugin.Models;
using Xunit;

namespace QtoRevitPlugin.Tests.Models
{
    public class QtoResultTests
    {
        [Fact]
        public void Quantity_IsGrossMinusDeducted()
        {
            var result = new QtoResult { QuantityGross = 45.2, QuantityDeducted = 3.1 };
            result.Quantity.Should().BeApproximately(42.1, 0.001);
        }

        [Fact]
        public void Total_IsQuantityTimesUnitPrice()
        {
            var result = new QtoResult
            {
                QuantityGross = 45.2,
                QuantityDeducted = 3.1,
                UnitPrice = 85.0
            };

            result.Total.Should().BeApproximately(42.1 * 85.0, 0.01);
        }

        [Fact]
        public void Source_DefaultsToRevitElement()
        {
            var result = new QtoResult();
            result.Source.Should().Be(QtoSource.RevitElement);
        }
    }
}
