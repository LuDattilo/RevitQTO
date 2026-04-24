using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class RoomMappingPersistenceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public RoomMappingPersistenceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"rm_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose() { _repo.Dispose(); if (File.Exists(_dbPath)) File.Delete(_dbPath); }

        [Fact]
        public void InsertRoomMappingConfig_ReturnsPositiveId()
        {
            var cfg = new RoomMappingConfig
            {
                SessionId = _sessionId,
                EpCode = "A.01",
                EpDescription = "Test",
                Unit = "mq",
                Formula = "Area",
                TargetCategory = RoomTargetCategory.Rooms,
                RoomNameFilter = ""
            };
            var id = _repo.InsertRoomMappingConfig(cfg);
            id.Should().BeGreaterThan(0);
        }

        [Fact]
        public void GetRoomMappingConfigs_ReturnsInsertedRows()
        {
            _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "A.01", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = RoomTargetCategory.Rooms, RoomNameFilter = "" });
            _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "A.02", EpDescription = "D2", Unit = "ml", Formula = "Perimeter", TargetCategory = RoomTargetCategory.Rooms, RoomNameFilter = "Piano" });
            var list = _repo.GetRoomMappingConfigs(_sessionId);
            list.Should().HaveCount(2);
            list.Select(r => r.EpCode).Should().BeEquivalentTo(new[] { "A.01", "A.02" });
        }

        [Fact]
        public void UpdateRoomMappingConfig_ChangesFormula()
        {
            var id = _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "B.01", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = RoomTargetCategory.Rooms, RoomNameFilter = "" });
            var cfg = _repo.GetRoomMappingConfigs(_sessionId).First(r => r.Id == id);
            cfg.Formula = "Area * 2";
            _repo.UpdateRoomMappingConfig(cfg);
            _repo.GetRoomMappingConfigs(_sessionId).First(r => r.Id == id).Formula.Should().Be("Area * 2");
        }

        [Fact]
        public void DeleteRoomMappingConfig_RemovesRow()
        {
            var id = _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "C.01", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = RoomTargetCategory.Rooms, RoomNameFilter = "" });
            _repo.DeleteRoomMappingConfig(id);
            _repo.GetRoomMappingConfigs(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void GetRoomMappingConfigs_IsolatedBySession()
        {
            var sessionB = _repo.InsertSession(new WorkSession { ProjectPath = "b.rvt", ProjectName = "b" });
            _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "X", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = RoomTargetCategory.Rooms, RoomNameFilter = "" });
            _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = sessionB, EpCode = "Y", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = RoomTargetCategory.Rooms, RoomNameFilter = "" });
            _repo.GetRoomMappingConfigs(_sessionId).Should().ContainSingle(r => r.EpCode == "X");
        }
    }
}
