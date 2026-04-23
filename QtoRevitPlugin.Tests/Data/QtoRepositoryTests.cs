using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Data
{
    public class QtoRepositoryTests : IDisposable
    {
        private readonly string _tempDbPath;
        private readonly QtoRepository _repo;

        public QtoRepositoryTests()
        {
            _tempDbPath = Path.Combine(Path.GetTempPath(), $"qto_test_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_tempDbPath);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
        }

        [Fact]
        public void NewDatabase_HasCurrentSchemaVersion()
        {
            // v7 introdotta in Sprint 10: tabella ProjectInfo per metadati computo
            //    (denominazione opera, committente, RUP, DL, luogo, date, CIG/CUP, ribasso)
            // v6 introdotta in Sprint 9 Task 5: UNIQUE constraint su QtoAssignments aggiornato
            //    a (SessionId, UniqueId, EpCode, Version) per supportare il pattern Supersede
            // v5 introdotta in Sprint 9 con ComputoChapters + ComputoChapterId su QtoAssignments
            // v4 introdotta in Sprint 6 con ChangeLog + ElementSnapshots + audit fields
            // v3 introdotta in Sprint 4 con PriceLists.PublicId (GUID portabile per snapshot .rvt)
            // v2 introdotta in Sprint 2 con PriceItems_FTS
            _repo.GetSchemaVersion().Should().Be(7);
        }

        [Fact]
        public void InsertSession_AssignsIdAndRoundtrips()
        {
            var session = new WorkSession
            {
                ProjectPath = @"C:\projects\Lotto_A.rvt",
                ProjectName = "Lotto A",
                SessionName = "Computo strutture",
                Status = SessionStatus.InProgress,
                ActivePhaseId = 123456,
                ActivePhaseName = "Progetto",
                TotalElements = 318,
                TaggedElements = 142,
                TotalAmount = 48320.0,
                LastEpCode = "B.02.004"
            };

            var id = _repo.InsertSession(session);

            id.Should().BeGreaterThan(0);
            session.Id.Should().Be(id);

            var reloaded = _repo.GetSession(id);
            reloaded.Should().NotBeNull();
            reloaded!.ProjectPath.Should().Be(@"C:\projects\Lotto_A.rvt");
            reloaded.SessionName.Should().Be("Computo strutture");
            reloaded.Status.Should().Be(SessionStatus.InProgress);
            reloaded.TotalElements.Should().Be(318);
            reloaded.TaggedElements.Should().Be(142);
            reloaded.TotalAmount.Should().Be(48320.0);
            reloaded.TaggedPercent.Should().BeApproximately(44.65, 0.05);
        }

        [Fact]
        public void UpdateSession_PersistsChanges()
        {
            var session = new WorkSession
            {
                ProjectPath = @"C:\projects\X.rvt",
                ProjectName = "X",
                SessionName = "v1"
            };
            _repo.InsertSession(session);

            session.SessionName = "v2";
            session.TaggedElements = 50;
            session.Status = SessionStatus.Completed;
            _repo.UpdateSession(session);

            var reloaded = _repo.GetSession(session.Id)!;
            reloaded.SessionName.Should().Be("v2");
            reloaded.TaggedElements.Should().Be(50);
            reloaded.Status.Should().Be(SessionStatus.Completed);
        }

        [Fact]
        public void GetSessionsForProject_ReturnsOnlyMatching()
        {
            _repo.InsertSession(new WorkSession { ProjectPath = "/a.rvt", SessionName = "s1" });
            _repo.InsertSession(new WorkSession { ProjectPath = "/a.rvt", SessionName = "s2" });
            _repo.InsertSession(new WorkSession { ProjectPath = "/b.rvt", SessionName = "s3" });

            var aSessions = _repo.GetSessionsForProject("/a.rvt");
            aSessions.Should().HaveCount(2);
            aSessions.Select(s => s.SessionName).Should().BeEquivalentTo(new[] { "s1", "s2" });

            var bSessions = _repo.GetSessionsForProject("/b.rvt");
            bSessions.Should().HaveCount(1);
        }

        [Fact]
        public void TouchSession_UpdatesLastSavedAt()
        {
            var session = new WorkSession { ProjectPath = "/x.rvt" };
            _repo.InsertSession(session);
            _repo.GetSession(session.Id)!.LastSavedAt.Should().BeNull();

            _repo.TouchSession(session.Id);

            var reloaded = _repo.GetSession(session.Id)!;
            reloaded.LastSavedAt.Should().NotBeNull();
            reloaded.LastSavedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void DeleteSession_RemovesRow()
        {
            var session = new WorkSession { ProjectPath = "/x.rvt" };
            _repo.InsertSession(session);

            _repo.DeleteSession(session.Id).Should().Be(1);
            _repo.GetSession(session.Id).Should().BeNull();
        }
    }
}
