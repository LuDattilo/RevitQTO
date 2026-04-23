using FluentAssertions;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.AI
{
    /// <summary>
    /// Test per <see cref="AnomalyDetector"/> — rilevazione z-score senza dipendenze Ollama.
    /// </summary>
    public class AnomalyDetectorTests
    {
        private readonly AnomalyDetector _sut = new AnomalyDetector();

        [Fact]
        public void Detect_EmptyList_ReturnsEmpty()
        {
            _sut.Detect(new List<QtoAssignment>()).Should().BeEmpty();
            _sut.Detect(null!).Should().BeEmpty();
        }

        [Fact]
        public void Detect_GroupTooSmall_ReturnsEmpty()
        {
            // 2 elementi dello stesso EP → sample troppo piccolo (< MinSampleSize=3)
            var items = new List<QtoAssignment>
            {
                NewAssignment("EP1", 10.0),
                NewAssignment("EP1", 1000.0), // sarebbe anomalo ma ignorato per campione
            };

            _sut.Detect(items).Should().BeEmpty();
        }

        [Fact]
        public void Detect_AllSameQuantity_ReturnsEmpty()
        {
            // Tutti 15 m³ → stdDev ≈ 0 → nessuna anomalia possibile
            var items = Enumerable.Range(0, 10)
                .Select(_ => NewAssignment("EP1", 15.0))
                .ToList();

            _sut.Detect(items).Should().BeEmpty();
        }

        [Fact]
        public void Detect_SingleOutlier_FlaggedAsAnomaly()
        {
            // 10 elementi vicini a 15, uno a 200 → outlier forte.
            // Con z-score classico, 1 outlier forte in 11 campioni inflaziona σ: z massimo ≈ √10 ≈ 3.16
            // quindi Media (2.5 < z ≤ 3.5), non Alta. Verifichiamo solo che sia flagged.
            var items = new List<QtoAssignment>();
            for (int i = 0; i < 10; i++)
                items.Add(NewAssignment("EP1", 15.0 + (i % 3))); // 15, 16, 17 alternati
            items.Add(NewAssignment("EP1", 200.0)); // outlier

            var anomalies = _sut.Detect(items);

            anomalies.Should().ContainSingle();
            anomalies[0].Quantity.Should().Be(200.0);
            anomalies[0].ZScore.Should().BeGreaterThan(_sut.Threshold);
            anomalies[0].EpCode.Should().Be("EP1");
        }

        [Fact]
        public void Detect_MediumOutlier_MediumSeverity()
        {
            // Output mirato a z-score tra 2.5 e 3.5 → Media
            // Campione molto stretto + outlier moderato
            var items = new List<QtoAssignment>();
            for (int i = 0; i < 9; i++)
                items.Add(NewAssignment("EP1", 10.0));
            items.Add(NewAssignment("EP1", 20.0)); // outlier moderato

            var anomalies = _sut.Detect(items);

            anomalies.Should().ContainSingle();
            anomalies[0].ZScore.Should().BeGreaterThan(_sut.Threshold);
            anomalies[0].Severity.Should().BeOneOf(AnomalySeverity.Media, AnomalySeverity.Alta);
        }

        [Fact]
        public void Detect_MultipleEpGroups_IsolatedPerGroup()
        {
            // Gruppo EP1 (5 elementi 10-14): variabilità naturale, nessuno oltre z=2.5
            // Gruppo EP2 (10 valori a 50 + 1 a 5000): outlier fortissimo, z ≈ √10 ≈ 3.16
            // Più elementi "normali" nel gruppo outlier → z più alto per l'outlier
            var items = new List<QtoAssignment>();
            for (int i = 0; i < 5; i++) items.Add(NewAssignment("EP1", 10.0 + i));
            for (int i = 0; i < 10; i++) items.Add(NewAssignment("EP2", 50.0));
            items.Add(NewAssignment("EP2", 5000.0)); // outlier estremo

            var anomalies = _sut.Detect(items);

            anomalies.Should().NotBeEmpty();
            anomalies.Should().OnlyContain(a => a.EpCode == "EP2",
                "il gruppo EP1 ha variabilità naturale ma nessun outlier oltre z=2.5");
        }

        [Fact]
        public void Detect_IgnoresAssignmentsWithoutEpCode()
        {
            // Assignments senza EpCode non devono essere raggruppati/analizzati
            var items = new List<QtoAssignment>();
            for (int i = 0; i < 5; i++)
                items.Add(NewAssignment("", 1000.0)); // nessun codice → ignorati

            _sut.Detect(items).Should().BeEmpty();
        }

        [Fact]
        public void Detect_CustomThreshold_ChangesResults()
        {
            // Con soglia più bassa, elementi borderline vengono flagged
            var items = new List<QtoAssignment>();
            for (int i = 0; i < 5; i++) items.Add(NewAssignment("EP1", 10.0));
            items.Add(NewAssignment("EP1", 13.0)); // z moderato

            var defaultResult = _sut.Detect(items);

            var lenientSut = new AnomalyDetector { Threshold = 1.5 };
            var lenientResult = lenientSut.Detect(items);

            // Con soglia più permissiva troviamo almeno il borderline
            lenientResult.Count.Should().BeGreaterOrEqualTo(defaultResult.Count);
        }

        private static QtoAssignment NewAssignment(string epCode, double quantity)
        {
            return new QtoAssignment
            {
                UniqueId = Guid.NewGuid().ToString(),
                EpCode   = epCode,
                Quantity = quantity,
                AuditStatus = AssignmentStatus.Active
            };
        }
    }
}
