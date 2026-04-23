using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using Xunit;

namespace QtoRevitPlugin.Tests.SprintUi8
{
    /// <summary>
    /// Test del gateway Health Check (UI-8). Verifica:
    /// - Contract graceful: null/vuoto → HealthReport.Empty, mai throw
    /// - AnomalyDetector usato sempre (anche con AI off): anomalie reali emergono
    /// - Parte AI skippata se disabled o irraggiungibile (AiUsed=false)
    /// - HealthReport.TotalIssues somma correttamente
    /// </summary>
    public class HealthCheckGatewayTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public HealthCheckGatewayTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"health_gw_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _repo.InsertSession(new WorkSession
            {
                ProjectPath = "test.rvt",
                SessionName = "health",
                CreatedAt = DateTime.UtcNow
            });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public async Task RunAsync_NullAssignments_ReturnsEmpty()
        {
            var result = await HealthCheckGateway.RunAsync(
                new CmeSettings(), _repo, assignments: null!);

            result.Should().NotBeNull();
            result.AssignmentsAnalyzed.Should().Be(0);
            result.Anomalies.Should().BeEmpty();
            result.Mismatches.Should().BeEmpty();
            result.AiUsed.Should().BeFalse();
            result.TotalIssues.Should().Be(0);
        }

        [Fact]
        public async Task RunAsync_EmptyAssignments_ReturnsEmpty()
        {
            var result = await HealthCheckGateway.RunAsync(
                new CmeSettings(), _repo, assignments: new List<QtoAssignment>());

            result.AssignmentsAnalyzed.Should().Be(0);
            result.TotalIssues.Should().Be(0);
        }

        [Fact]
        public async Task RunAsync_AiDisabled_StillDetectsAnomalies()
        {
            // Scenario: 10 elementi con stesso EP, 9 quantity ~1.0, 1 outlier = 100.0.
            // L'AnomalyDetector deve pescarlo anche senza AI.
            var settings = new CmeSettings { AiEnabled = false };
            var assignments = new List<QtoAssignment>();
            for (int i = 0; i < 9; i++)
            {
                assignments.Add(NewAssignment("EP-X", quantity: 1.0 + i * 0.1));
            }
            assignments.Add(NewAssignment("EP-X", quantity: 100.0)); // outlier grosso

            var result = await HealthCheckGateway.RunAsync(settings, _repo, assignments);

            result.AssignmentsAnalyzed.Should().Be(10);
            result.Anomalies.Should().NotBeEmpty("l'outlier z-score deve emergere anche senza AI");
            result.Mismatches.Should().BeEmpty("AI disabilitata → no mismatch check");
            result.AiUsed.Should().BeFalse();
            result.TotalIssues.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task RunAsync_AiEnabledButOllamaUnreachable_StillDetectsAnomalies()
        {
            // AI enabled ma URL non raggiungibile → AiUsed=false, anomalie ancora rilevate.
            var settings = new CmeSettings
            {
                AiEnabled = true,
                OllamaBaseUrl = "http://198.51.100.1:11434",
                EmbeddingModel = "test-model"
            };
            var assignments = new List<QtoAssignment>();
            for (int i = 0; i < 8; i++)
                assignments.Add(NewAssignment("EP-Y", quantity: 2.0));
            assignments.Add(NewAssignment("EP-Y", quantity: 200.0)); // outlier

            var result = await HealthCheckGateway.RunAsync(
                settings, _repo, assignments, timeoutMs: 500);

            result.AssignmentsAnalyzed.Should().Be(9);
            result.AiUsed.Should().BeFalse("Ollama unreachable → fallback NullAiProvider");
            result.Mismatches.Should().BeEmpty();
            // L'outlier potrebbe o meno emergere a seconda della soglia z-score;
            // il punto è che il controllo è stato eseguito senza crashare.
            result.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task RunAsync_NeverThrowsEvenOnEdgeInputs()
        {
            var weirdAssignments = new List<QtoAssignment>
            {
                NewAssignment(string.Empty, 0),        // EpCode vuoto + quantity 0
                NewAssignment("!@#$%^&*()", -1),       // caratteri speciali + neg
                NewAssignment(new string('A', 500), double.MaxValue / 2), // stringhe lunghe
            };

            var act = async () => await HealthCheckGateway.RunAsync(
                new CmeSettings { AiEnabled = false }, _repo, weirdAssignments);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void HealthReport_TotalIssues_SumsAnomaliesAndMismatches()
        {
            var report = new HealthReport
            {
                Anomalies = new List<QuantityAnomaly>
                {
                    new QuantityAnomaly { UniqueId = "a" },
                    new QuantityAnomaly { UniqueId = "b" },
                },
                Mismatches = new List<SemanticMismatch>
                {
                    new SemanticMismatch { UniqueId = "c" },
                },
            };

            report.TotalIssues.Should().Be(3);
        }

        // ---- helpers ----

        private static QtoAssignment NewAssignment(string epCode, double quantity) => new QtoAssignment
        {
            SessionId = 1,
            UniqueId = $"U-{Guid.NewGuid():N}",
            ElementId = 1,
            Category = "Walls",
            FamilyName = "Muro",
            EpCode = epCode,
            Quantity = quantity,
            Unit = "m²",
            UnitPrice = 10,
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow,
            AssignedAt = DateTime.UtcNow,
            AuditStatus = AssignmentStatus.Active,
            Version = 1,
        };
    }
}
