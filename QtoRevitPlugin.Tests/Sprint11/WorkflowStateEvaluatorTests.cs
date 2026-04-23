using System.Linq;
using FluentAssertions;
using QtoRevitPlugin.Models;
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

        // ======================================================================
        // EvaluateSteps — tabellare per le 6 view del workflow CME
        // ======================================================================

        [Fact]
        public void EvaluateSteps_NoSession_AllStepsLocked()
        {
            var evaluator = new WorkflowStateEvaluator();

            var steps = evaluator.EvaluateSteps(session: null, hasActivePriceList: false);

            steps.Should().HaveCount(6);
            steps.Select(s => s.Key).Should().ContainInOrder(
                "Setup", "Listino", "Selection", "Tagging", "Verification", "Export");
            steps.Should().OnlyContain(s => s.Status == WorkflowStepStatus.Locked);
        }

        [Fact]
        public void EvaluateSteps_FreshSessionNoPriceList_SetupDone_ListinoCurrent()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = NewSession();

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: false);

            Step(steps, "Setup").Status.Should().Be(WorkflowStepStatus.Done);
            // Listino è Available ma viene promosso a Current (primo Available → CTA)
            Step(steps, "Listino").Status.Should().Be(WorkflowStepStatus.Current);
            Step(steps, "Selection").Status.Should().Be(WorkflowStepStatus.Locked);
            Step(steps, "Tagging").Status.Should().Be(WorkflowStepStatus.Locked);
            Step(steps, "Verification").Status.Should().Be(WorkflowStepStatus.Locked);
            Step(steps, "Export").Status.Should().Be(WorkflowStepStatus.Locked);
        }

        [Fact]
        public void EvaluateSteps_ListinoActive_SelectionPromotedToCurrent()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = NewSession();

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            Step(steps, "Listino").Status.Should().Be(WorkflowStepStatus.Done);
            Step(steps, "Selection").Status.Should().Be(WorkflowStepStatus.Current);
            Step(steps, "Tagging").Status.Should().Be(WorkflowStepStatus.Locked);
        }

        [Fact]
        public void EvaluateSteps_ElementsSelected_TaggingPromotedToCurrent()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = NewSession();
            session.TotalElements = 120;

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            Step(steps, "Selection").Status.Should().Be(WorkflowStepStatus.Done);
            Step(steps, "Selection").Hint.Should().Be("120 elementi selezionati");
            Step(steps, "Tagging").Status.Should().Be(WorkflowStepStatus.Current);
            Step(steps, "Tagging").Hint.Should().Be("0/120 taggati");
        }

        [Fact]
        public void EvaluateSteps_PartialTagging_TaggingIsCurrent_VerifyAvailable()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = NewSession();
            session.TotalElements = 120;
            session.TaggedElements = 32;

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            Step(steps, "Tagging").Status.Should().Be(WorkflowStepStatus.Current);
            Step(steps, "Tagging").Hint.Should().Be("32/120 taggati");
            // Tagging già Current blocca la promozione di Verifica a Current.
            Step(steps, "Verification").Status.Should().Be(WorkflowStepStatus.Available);
        }

        [Fact]
        public void EvaluateSteps_FullyTagged_TaggingDone_VerifyPromoted()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = NewSession();
            session.TotalElements = 120;
            session.TaggedElements = 120;

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            Step(steps, "Tagging").Status.Should().Be(WorkflowStepStatus.Done);
            Step(steps, "Verification").Status.Should().Be(WorkflowStepStatus.Current);
            Step(steps, "Export").Status.Should().Be(WorkflowStepStatus.Locked);
        }

        [Fact]
        public void EvaluateSteps_TaggedWithAmount_VerificationDone_ExportPromoted()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = NewSession();
            session.TotalElements = 120;
            session.TaggedElements = 120;
            session.TotalAmount = 15_000;

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            Step(steps, "Verification").Status.Should().Be(WorkflowStepStatus.Done);
            Step(steps, "Verification").Hint.Should().Contain("15");
            Step(steps, "Export").Status.Should().Be(WorkflowStepStatus.Current);
        }

        [Fact]
        public void EvaluateSteps_Exported_ExportDone_NoCurrent()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = NewSession();
            session.TotalElements = 120;
            session.TaggedElements = 120;
            session.TotalAmount = 15_000;
            session.Status = SessionStatus.Exported;

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            Step(steps, "Export").Status.Should().Be(WorkflowStepStatus.Done);
            steps.Should().OnlyContain(s => s.Status != WorkflowStepStatus.Current);
        }

        [Fact]
        public void EvaluateSteps_CompletedStatus_MarksVerificationDoneEvenWithoutAmount()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = NewSession();
            session.TotalElements = 120;
            session.TaggedElements = 120;
            session.Status = SessionStatus.Completed;

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            Step(steps, "Verification").Status.Should().Be(WorkflowStepStatus.Done);
        }

        // ---- helpers -----------------------------------------------------

        private static WorkSession NewSession() => new WorkSession
        {
            Id = 1,
            ProjectName = "Palazzo Test",
            SessionName = "SESS-01",
            Status = SessionStatus.InProgress
        };

        private static WorkflowStepState Step(System.Collections.Generic.IReadOnlyList<WorkflowStepState> steps, string key)
            => steps.First(s => s.Key == key);
    }
}
