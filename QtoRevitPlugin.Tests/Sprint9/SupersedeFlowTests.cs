using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint9
{
    public class SupersedeFlowTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public SupersedeFlowTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sup_test_{Guid.NewGuid()}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private (int oldId, QtoAssignment original) SeedAssignment(double qty = 10.0)
        {
            var a = new QtoAssignment
            {
                SessionId = _sessionId, ElementId = 100, UniqueId = "uid-X",
                EpCode = "EP-01", EpDescription = "desc", Quantity = qty,
                Unit = "m²", UnitPrice = 25.0, RuleApplied = "Area",
                Source = QtoSource.RevitElement, CreatedBy = "tester",
                CreatedAt = DateTime.UtcNow, AuditStatus = AssignmentStatus.Active, Version = 1
            };
            a.Id = _repo.InsertAssignment(a);
            return (a.Id, a);
        }

        [Fact]
        public void AcceptDiffBatch_ModifiedOp_CreatesVersionPlus1_AndMarksOldSuperseded()
        {
            var (oldId, orig) = SeedAssignment(10.0);

            var newAssignment = new QtoAssignment
            {
                SessionId = _sessionId, ElementId = orig.ElementId, UniqueId = orig.UniqueId,
                EpCode = orig.EpCode, EpDescription = orig.EpDescription, Quantity = 15.0,
                Unit = orig.Unit, UnitPrice = orig.UnitPrice, RuleApplied = orig.RuleApplied,
                Source = orig.Source, CreatedBy = "tester", CreatedAt = DateTime.UtcNow,
                AuditStatus = AssignmentStatus.Active, Version = orig.Version + 1
            };
            var op = new SupersedeOp
            {
                OldAssignmentId = oldId, NewVersion = newAssignment, Kind = SupersedeKind.Modified,
                NewSnapshot = new ElementSnapshot
                {
                    SessionId = _sessionId, ElementId = orig.ElementId, UniqueId = orig.UniqueId,
                    SnapshotHash = "NEWHASH", SnapshotQty = 15.0,
                    AssignedEP = new List<string> { orig.EpCode }, LastUpdated = DateTime.UtcNow
                },
                Log = new ChangeLogEntry
                {
                    SessionId = _sessionId, ElementUniqueId = orig.UniqueId, PriceItemCode = orig.EpCode,
                    ChangeType = "Superseded",
                    OldValueJson = "{\"qty\":10.0}", NewValueJson = "{\"qty\":15.0}",
                    UserId = "tester", Timestamp = DateTime.UtcNow
                }
            };

            _repo.AcceptDiffBatch(new[] { op });

            var all = _repo.GetAssignments(_sessionId);
            all.Should().HaveCount(2);
            all.Single(a => a.Id == oldId).AuditStatus.Should().Be(AssignmentStatus.Superseded);
            all.Single(a => a.Version == 2).AuditStatus.Should().Be(AssignmentStatus.Active);
            all.Single(a => a.Version == 2).Quantity.Should().Be(15.0);
        }

        [Fact]
        public void AcceptDiffBatch_DeletedOp_MarksAssignmentDeleted()
        {
            var (oldId, orig) = SeedAssignment();
            var op = new SupersedeOp
            {
                OldAssignmentId = oldId, Kind = SupersedeKind.Deleted,
                NewVersion = orig, NewSnapshot = new ElementSnapshot
                {
                    SessionId = _sessionId, ElementId = orig.ElementId, UniqueId = orig.UniqueId,
                    SnapshotHash = "", SnapshotQty = 0, AssignedEP = new List<string>(), LastUpdated = DateTime.UtcNow
                },
                Log = new ChangeLogEntry
                {
                    SessionId = _sessionId, ElementUniqueId = orig.UniqueId, PriceItemCode = orig.EpCode,
                    ChangeType = "Deleted", OldValueJson = "{\"qty\":10.0}",
                    UserId = "tester", Timestamp = DateTime.UtcNow
                }
            };

            _repo.AcceptDiffBatch(new[] { op });

            var all = _repo.GetAssignments(_sessionId);
            all.Should().HaveCount(1);
            all.Single().AuditStatus.Should().Be(AssignmentStatus.Deleted);
        }

        [Fact]
        public void AcceptDiffBatch_AllOpsInSingleTransaction_RollsBackOnException()
        {
            var (oldId, orig) = SeedAssignment();
            var validOp = new SupersedeOp
            {
                OldAssignmentId = oldId, Kind = SupersedeKind.Deleted,
                NewVersion = orig, NewSnapshot = new ElementSnapshot
                {
                    SessionId = _sessionId, ElementId = orig.ElementId, UniqueId = orig.UniqueId,
                    SnapshotHash = "", SnapshotQty = 0, AssignedEP = new List<string>(), LastUpdated = DateTime.UtcNow
                },
                Log = new ChangeLogEntry
                {
                    SessionId = _sessionId, ElementUniqueId = orig.UniqueId, PriceItemCode = orig.EpCode,
                    ChangeType = "Deleted", UserId = "tester", Timestamp = DateTime.UtcNow
                }
            };
            var invalidOp = new SupersedeOp
            {
                OldAssignmentId = 999999, Kind = SupersedeKind.Modified,
                NewVersion = null!, NewSnapshot = null!, Log = null!
            };

            var act = () => _repo.AcceptDiffBatch(new[] { validOp, invalidOp });
            act.Should().Throw<Exception>();

            // Rollback: la prima op non deve essere applicata
            var all = _repo.GetAssignments(_sessionId);
            all.Should().HaveCount(1);
            all.Single().AuditStatus.Should().Be(AssignmentStatus.Active, "transazione deve rollbackare interamente");
        }

        [Fact]
        public void AcceptDiffBatch_WritesChangeLog_WithOldAndNewJson()
        {
            var (oldId, orig) = SeedAssignment();
            var op = new SupersedeOp
            {
                OldAssignmentId = oldId, Kind = SupersedeKind.Modified,
                NewVersion = new QtoAssignment
                {
                    SessionId = _sessionId, ElementId = orig.ElementId, UniqueId = orig.UniqueId,
                    EpCode = orig.EpCode, Quantity = 20, Unit = "m²", UnitPrice = 25,
                    CreatedBy = "t", CreatedAt = DateTime.UtcNow, AuditStatus = AssignmentStatus.Active, Version = 2
                },
                NewSnapshot = new ElementSnapshot
                {
                    SessionId = _sessionId, ElementId = orig.ElementId, UniqueId = orig.UniqueId,
                    SnapshotHash = "H2", SnapshotQty = 20, AssignedEP = new List<string> { orig.EpCode }, LastUpdated = DateTime.UtcNow
                },
                Log = new ChangeLogEntry
                {
                    SessionId = _sessionId, ElementUniqueId = orig.UniqueId, PriceItemCode = orig.EpCode,
                    ChangeType = "Superseded",
                    OldValueJson = "{\"qty\":10,\"hash\":\"H1\"}", NewValueJson = "{\"qty\":20,\"hash\":\"H2\"}",
                    UserId = "tester", Timestamp = DateTime.UtcNow
                }
            };

            _repo.AcceptDiffBatch(new[] { op });

            var logs = _repo.GetChangeLog(_sessionId);
            logs.Should().ContainSingle(l => l.ChangeType == "Superseded"
                && l.OldValueJson!.Contains("\"qty\":10")
                && l.NewValueJson!.Contains("\"qty\":20"));
        }
    }
}
