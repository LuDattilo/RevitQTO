using FluentAssertions;
using QtoRevitPlugin.Services;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint10
{
    public class FloatingWindowReusePolicyTests
    {
        [Fact]
        public void Decide_WhenWindowDoesNotExist_CreatesNewWindow()
        {
            FloatingWindowReusePolicy.Decide(hasWindow: false, isVisible: false)
                .Should().Be(FloatingWindowReuseAction.Create);
        }

        [Fact]
        public void Decide_WhenWindowIsVisible_ActivatesExistingWindow()
        {
            FloatingWindowReusePolicy.Decide(hasWindow: true, isVisible: true)
                .Should().Be(FloatingWindowReuseAction.ActivateVisible);
        }

        [Fact]
        public void Decide_WhenWindowIsHidden_ShowsExistingWindowAgain()
        {
            FloatingWindowReusePolicy.Decide(hasWindow: true, isVisible: false)
                .Should().Be(FloatingWindowReuseAction.ShowHidden);
        }
    }
}
