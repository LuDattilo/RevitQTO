using FluentAssertions;
using QtoRevitPlugin.Services;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class WorkflowStateEvaluatorTests
    {
        [Fact]
        public void Evaluate_NoSession_EnablesOnlyStartupActions()
        {
            var evaluator = new WorkflowStateEvaluator();

            var state = evaluator.Evaluate(hasActiveSession: false, hasActivePriceList: false);

            state.CanOpenSetup.Should().BeFalse();
            state.CanOpenSelection.Should().BeFalse();
            state.PrimaryMessage.Should().Be("Per iniziare serve un computo attivo");
        }

        [Fact]
        public void Evaluate_SessionWithoutPriceList_BlocksSelectionButNotListino()
        {
            var evaluator = new WorkflowStateEvaluator();

            var state = evaluator.Evaluate(hasActiveSession: true, hasActivePriceList: false);

            state.CanOpenSetup.Should().BeTrue();
            state.CanOpenListino.Should().BeTrue();
            state.CanOpenSelection.Should().BeFalse();
            state.SecondaryMessage.Should().Contain("listino");
        }
    }
}
