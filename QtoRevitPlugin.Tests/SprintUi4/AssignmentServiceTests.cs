using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using Xunit;

namespace QtoRevitPlugin.Tests.SprintUi4
{
    /// <summary>
    /// Test del servizio Core <see cref="AssignmentService"/>. Copre:
    /// - Assegnazione base: inserimento N target → N QtoAssignment + KPI session aggiornati
    /// - Primo uso: <see cref="AssignmentOutcome.IsFirstUseOfEp"/> true alla prima assegnazione,
    ///   false alle successive per lo stesso EpCode
    /// - Dedup intra-batch: UniqueId duplicati nello stesso request vengono saltati con skipReason
    /// - Validazione: Quantity &lt;= 0 e UniqueId vuoto vengono skippati, non falliscono
    /// - Contract errors: SessionId=0, EpCode vuoto, request null → ArgumentException
    /// </summary>
    public class AssignmentServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly AssignmentService _sut;
        private readonly int _sessionId;

        public AssignmentServiceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"assign_svc_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession
            {
                ProjectPath = "test.rvt",
                ProjectName = "Test",
                SessionName = "SESS-T",
                CreatedAt = DateTime.UtcNow
            });
            _sut = new AssignmentService(_repo);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        // ------------------------------------------------------------------
        // Happy path
        // ------------------------------------------------------------------

        [Fact]
        public void AssignEp_CreatesOneAssignmentPerTarget()
        {
            var req = NewRequest("EP.001", 10.0, "m²");
            req.Targets.Add(new AssignmentTarget(101, "U-101", "Walls", "Muro base", 5.0));
            req.Targets.Add(new AssignmentTarget(102, "U-102", "Walls", "Muro base", 8.0));

            var outcome = _sut.AssignEp(req);

            outcome.InsertedCount.Should().Be(2);
            outcome.SkippedCount.Should().Be(0);
            _repo.GetAssignments(_sessionId).Should().HaveCount(2);
        }

        [Fact]
        public void AssignEp_UpdatesSessionKpis_FromActiveAssignments()
        {
            var req = NewRequest("EP.002", 20.0, "m³");
            req.Targets.Add(new AssignmentTarget(1, "U-1", "Floors", "Solaio", 3.0));
            req.Targets.Add(new AssignmentTarget(2, "U-2", "Floors", "Solaio", 7.0));

            _sut.AssignEp(req);
            var s = _repo.GetSession(_sessionId)!;

            s.TotalElements.Should().Be(2);
            s.TaggedElements.Should().Be(2);
            // 3*20 + 7*20 = 200
            s.TotalAmount.Should().Be(200.0);
            s.LastEpCode.Should().Be("EP.002");
        }

        // ------------------------------------------------------------------
        // Primo uso (trigger per prompt preferiti)
        // ------------------------------------------------------------------

        [Fact]
        public void AssignEp_IsFirstUseOfEp_TrueOnFirstAssignment()
        {
            var req = NewRequest("EP.FIRST", 10.0, "m");
            req.Targets.Add(new AssignmentTarget(1, "U-1", "Walls", "Muro", 5.0));

            var outcome = _sut.AssignEp(req);

            outcome.IsFirstUseOfEp.Should().BeTrue();
        }

        [Fact]
        public void AssignEp_IsFirstUseOfEp_FalseOnSecondAssignment()
        {
            var req1 = NewRequest("EP.REPEAT", 10.0, "m");
            req1.Targets.Add(new AssignmentTarget(1, "U-1", "Walls", "Muro", 5.0));
            _sut.AssignEp(req1);

            var req2 = NewRequest("EP.REPEAT", 10.0, "m");
            req2.Targets.Add(new AssignmentTarget(2, "U-2", "Walls", "Muro", 4.0));
            var outcome = _sut.AssignEp(req2);

            outcome.IsFirstUseOfEp.Should().BeFalse();
        }

        [Fact]
        public void AssignEp_IsFirstUseOfEp_TrueForDifferentEpOnSameSession()
        {
            var req1 = NewRequest("EP.A", 10.0, "m");
            req1.Targets.Add(new AssignmentTarget(1, "U-1", "Walls", "Muro", 5.0));
            _sut.AssignEp(req1);

            var req2 = NewRequest("EP.B", 15.0, "m");
            req2.Targets.Add(new AssignmentTarget(2, "U-2", "Walls", "Muro", 4.0));
            var outcome = _sut.AssignEp(req2);

            outcome.IsFirstUseOfEp.Should().BeTrue();
        }

        // ------------------------------------------------------------------
        // Dedup / validazione
        // ------------------------------------------------------------------

        [Fact]
        public void AssignEp_SkipsDuplicateUniqueIdInBatch()
        {
            var req = NewRequest("EP.DUP", 10.0, "m");
            req.Targets.Add(new AssignmentTarget(1, "U-SAME", "Walls", "Muro", 5.0));
            req.Targets.Add(new AssignmentTarget(2, "U-SAME", "Walls", "Muro", 5.0));
            req.Targets.Add(new AssignmentTarget(3, "U-OTHER", "Walls", "Muro", 5.0));

            var outcome = _sut.AssignEp(req);

            outcome.InsertedCount.Should().Be(2);
            outcome.SkippedCount.Should().Be(1);
            outcome.SkipReasons.Should().ContainSingle(r => r.Contains("duplicato"));
        }

        [Fact]
        public void AssignEp_SkipsTargetWithEmptyUniqueId()
        {
            var req = NewRequest("EP.X", 10.0, "m");
            req.Targets.Add(new AssignmentTarget(1, "", "Walls", "Muro", 5.0));
            req.Targets.Add(new AssignmentTarget(2, "U-2", "Walls", "Muro", 5.0));

            var outcome = _sut.AssignEp(req);

            outcome.InsertedCount.Should().Be(1);
            outcome.SkippedCount.Should().Be(1);
            outcome.SkipReasons.Should().ContainSingle(r => r.Contains("UniqueId mancante"));
        }

        [Fact]
        public void AssignEp_SkipsTargetWithZeroOrNegativeQuantity()
        {
            var req = NewRequest("EP.Z", 10.0, "m");
            req.Targets.Add(new AssignmentTarget(1, "U-1", "Walls", "Muro", 0.0));
            req.Targets.Add(new AssignmentTarget(2, "U-2", "Walls", "Muro", -3.0));
            req.Targets.Add(new AssignmentTarget(3, "U-3", "Walls", "Muro", 2.0));

            var outcome = _sut.AssignEp(req);

            outcome.InsertedCount.Should().Be(1);
            outcome.SkippedCount.Should().Be(2);
            outcome.SkipReasons.Should().HaveCount(2);
            outcome.SkipReasons.Should().OnlyContain(r => r.Contains("quantità non positiva"));
        }

        // ------------------------------------------------------------------
        // Contract validation
        // ------------------------------------------------------------------

        [Fact]
        public void AssignEp_NullRequest_Throws()
        {
            var act = () => _sut.AssignEp(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void AssignEp_InvalidSessionId_Throws()
        {
            var req = new AssignmentRequest(0, "EP.X");

            var act = () => _sut.AssignEp(req);

            act.Should().Throw<ArgumentException>().WithMessage("*SessionId*");
        }

        [Fact]
        public void AssignEp_EmptyEpCode_Throws()
        {
            var req = new AssignmentRequest(_sessionId, "   ");

            var act = () => _sut.AssignEp(req);

            act.Should().Throw<ArgumentException>().WithMessage("*EpCode*");
        }

        // ------------------------------------------------------------------
        // TotalAmount reconciliation (ricalcolo da DB, non increment)
        // ------------------------------------------------------------------

        [Fact]
        public void AssignEp_TotalAmount_IsRecomputedFromDb_NotIncremented()
        {
            // Scenario: esiste già un'assegnazione attiva da 100€. Aggiungiamo
            // un'altra da 50€. Il TotalAmount atteso = 150€, non (0 + 50).
            _repo.InsertAssignment(new QtoAssignment
            {
                SessionId = _sessionId,
                UniqueId = "PRE",
                ElementId = 99,
                Category = "Walls",
                FamilyName = "Muro",
                EpCode = "EP.PRE",
                Quantity = 10,
                UnitPrice = 10, // 100€
                AuditStatus = AssignmentStatus.Active,
                AssignedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Version = 1
            });

            var req = NewRequest("EP.NEW", 5.0, "m");
            req.Targets.Add(new AssignmentTarget(1, "U-NEW", "Walls", "Muro", 10.0)); // 50€
            _sut.AssignEp(req);

            var s = _repo.GetSession(_sessionId)!;
            s.TotalAmount.Should().Be(150.0);
            s.TotalElements.Should().Be(2);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private AssignmentRequest NewRequest(string ep, double unitPrice, string unit) => new AssignmentRequest(_sessionId, ep)
        {
            UnitPrice = unitPrice,
            Unit = unit,
            EpDescription = $"Desc {ep}",
            CreatedBy = "tester"
        };
    }
}
