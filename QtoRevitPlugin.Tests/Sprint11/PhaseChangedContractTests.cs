using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    /// <summary>
    /// Contract test per la propagazione PhaseChanged introdotta in Fase 3 UI
    /// redesign. Non testa SessionManager (che dipende da Revit Document),
    /// testa le invarianti del contratto esposto a subscriber VM:
    ///
    /// 1. <see cref="SessionChangeKind.PhaseChanged"/> esiste e non collide con
    ///    gli altri kind (regressione: se qualcuno rinomina l'enum member).
    /// 2. <see cref="WorkflowStateEvaluator.EvaluateSteps"/> è stabile rispetto
    ///    ad <see cref="WorkSession.ActivePhaseId"/> (la fase non entra nel
    ///    calcolo degli step — ma un domani potrebbe, i test blindano
    ///    l'invariante attuale).
    /// 3. Un toggle ActivePhase su una sessione preserva tutti gli altri
    ///    calcoli Home (elementi/taggati/importo immutabili).
    /// </summary>
    public class PhaseChangedContractTests
    {
        [Fact]
        public void SessionChangeKind_PhaseChanged_IsDistinctMember()
        {
            // Se qualcuno rinomina PhaseChanged in un futuro refactor, la
            // propagazione cross-view si rompe silenziosamente: questo test
            // rende esplicito il contratto.
            var kinds = System.Enum.GetValues<SessionChangeKind>();

            kinds.Should().Contain(SessionChangeKind.PhaseChanged);
            // Non deve avere lo stesso valore di un altro member
            ((int)SessionChangeKind.PhaseChanged).Should().NotBe((int)SessionChangeKind.Renamed);
            ((int)SessionChangeKind.PhaseChanged).Should().NotBe((int)SessionChangeKind.Closed);
        }

        [Fact]
        public void EvaluateSteps_IsStable_AcrossPhaseToggle()
        {
            var evaluator = new WorkflowStateEvaluator();
            var session = new WorkSession
            {
                ProjectName = "Palazzo",
                SessionName = "SESS",
                TotalElements = 50,
                TaggedElements = 20,
                TotalAmount = 1000,
                Status = SessionStatus.InProgress
            };

            // Fase 1
            session.ActivePhaseId = 101;
            session.ActivePhaseName = "New Construction";
            var stepsPhase1 = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            // Stesso session, fase diversa (soft-switch)
            session.ActivePhaseId = 202;
            session.ActivePhaseName = "Demolizioni";
            var stepsPhase2 = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            // Tutti i 6 step hanno gli stessi Status indipendentemente dalla
            // fase (il gating Home è basato su TotalElements/TaggedElements/
            // Amount, non sulla fase).
            stepsPhase1.Should().HaveCount(6);
            stepsPhase2.Should().HaveCount(6);
            for (int i = 0; i < 6; i++)
            {
                stepsPhase1[i].Key.Should().Be(stepsPhase2[i].Key);
                stepsPhase1[i].Status.Should().Be(stepsPhase2[i].Status,
                    because: $"step {stepsPhase1[i].Key} non deve cambiare stato col phase toggle");
            }
        }

        [Fact]
        public void EvaluateSteps_WithZeroPhaseId_DoesNotCrash()
        {
            // Regressione: ActivePhaseId=0 (default) deve essere gestito
            // come "nessuna fase attiva", non come fase valida.
            var evaluator = new WorkflowStateEvaluator();
            var session = new WorkSession
            {
                ProjectName = "X",
                SessionName = "Y",
                ActivePhaseId = 0,
                ActivePhaseName = string.Empty
            };

            var act = () => evaluator.EvaluateSteps(session, hasActivePriceList: false);

            act.Should().NotThrow();
        }

        [Fact]
        public void EvaluateSteps_EmptyPhaseName_StillReturnsSixSteps()
        {
            // La fase vuota è lecita (sessione appena creata, fase non scelta).
            // L'evaluator non deve ridurre il set di step.
            var evaluator = new WorkflowStateEvaluator();
            var session = new WorkSession
            {
                ProjectName = "P",
                SessionName = "S"
            };

            var steps = evaluator.EvaluateSteps(session, hasActivePriceList: true);

            steps.Should().HaveCount(6);
        }

        [Fact]
        public void SessionChangedEventArgs_ForPhaseChanged_CarriesSession()
        {
            // L'args deve esporre la Session corrente — i subscriber leggono
            // session.ActivePhaseName per aggiornare la label.
            var session = new WorkSession
            {
                ProjectName = "Bike",
                ActivePhaseId = 42,
                ActivePhaseName = "Fase Ciclica"
            };

            var args = new SessionChangedEventArgs(session, SessionChangeKind.PhaseChanged);

            args.Session.Should().BeSameAs(session);
            args.Kind.Should().Be(SessionChangeKind.PhaseChanged);
            args.Session.ActivePhaseName.Should().Be("Fase Ciclica");
        }
    }
}
