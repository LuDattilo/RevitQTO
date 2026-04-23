using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test per <see cref="SelectionRulePresetService"/> (JSON + filesystem)
    /// e CRUD repository (DB layer).
    /// </summary>
    public class SelectionRulePresetTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly string _tempDir;

        public SelectionRulePresetTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"rules_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            // Test isolati in una directory temp per non inquinare %AppData% utente
            _tempDir = Path.Combine(Path.GetTempPath(), $"rules_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        // ─── JSON serialization ───

        [Fact]
        public void Serialize_RoundtripPreservesAllFields()
        {
            var preset = new SelectionRulePreset
            {
                RuleName = "Muri Esterni – A",
                Category = "OST_Walls",
                PhaseId = 123456,
                PhaseStatus = "New",
                Rules = new List<SelectionRuleEntry>
                {
                    new SelectionRuleEntry { Parameter = "FUNCTION_PARAM", Evaluator = "Equals", Value = "Exterior" },
                    new SelectionRuleEntry { Parameter = "QTO_Contrassegno", Evaluator = "Contains", Value = "A" }
                },
                InlineSearchParam = "ALL_MODEL_MARK"
            };

            var json = SelectionRulePresetService.Serialize(preset);
            var roundtripped = SelectionRulePresetService.Deserialize(json);

            roundtripped.RuleName.Should().Be("Muri Esterni – A");
            roundtripped.Category.Should().Be("OST_Walls");
            roundtripped.PhaseId.Should().Be(123456);
            roundtripped.PhaseStatus.Should().Be("New");
            roundtripped.Rules.Should().HaveCount(2);
            roundtripped.Rules[0].Parameter.Should().Be("FUNCTION_PARAM");
            roundtripped.Rules[0].Evaluator.Should().Be("Equals");
            roundtripped.Rules[1].Value.Should().Be("A");
            roundtripped.InlineSearchParam.Should().Be("ALL_MODEL_MARK");
        }

        [Fact]
        public void Serialize_NullPreset_Throws()
        {
            Action act = () => SelectionRulePresetService.Serialize(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Deserialize_EmptyString_Throws()
        {
            Action act = () => SelectionRulePresetService.Deserialize("");
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Deserialize_InvalidJson_Throws()
        {
            Action act = () => SelectionRulePresetService.Deserialize("{ not valid json");
            act.Should().Throw<Exception>(); // JsonException o FormatException
        }

        [Fact]
        public void Deserialize_JsonFromDoc_ParsesCorrectly()
        {
            // Formato esempio da §I6 del doc QTO-Implementazioni-v3.md
            var json = @"{
              ""RuleName"": ""Muri Esterni – Contrassegno A"",
              ""Category"": ""OST_Walls"",
              ""PhaseId"": 123456,
              ""PhaseStatus"": ""New"",
              ""Rules"": [
                { ""Parameter"": ""FUNCTION_PARAM"", ""Evaluator"": ""Equals"", ""Value"": ""Exterior"" }
              ],
              ""InlineSearchParam"": ""ALL_MODEL_MARK""
            }";

            var preset = SelectionRulePresetService.Deserialize(json);
            preset.RuleName.Should().Be("Muri Esterni – Contrassegno A");
            preset.Rules.Should().ContainSingle();
        }

        // ─── FileName sanitization ───

        [Theory]
        [InlineData("Muri Esterni", "Muri Esterni")]
        [InlineData("Nome / con slash", "Nome _ con slash")]
        [InlineData("Con: duepunti?", "Con_ duepunti_")]
        [InlineData("", "default")]
        public void SanitizeFileName_ReplacesInvalidChars(string input, string expected)
        {
            SelectionRulePresetService.SanitizeFileName(input).Should().Be(expected);
        }

        // ─── DB CRUD ───

        [Fact]
        public void UpsertSelectionRulePreset_InsertThenUpdate_KeepsSingleRow()
        {
            var preset = new SelectionRulePreset
            {
                RuleName = "Test Rule",
                Category = "OST_Walls",
                PhaseStatus = "New"
            };

            var id1 = _repo.UpsertSelectionRulePreset(preset);
            id1.Should().BeGreaterThan(0);

            // Secondo upsert con stesso nome ma valori diversi → UPDATE
            preset.PhaseStatus = "Existing";
            var id2 = _repo.UpsertSelectionRulePreset(preset);

            id2.Should().Be(id1, "upsert su stesso nome deve aggiornare la riga esistente");
            _repo.GetSelectionRulePresetNames().Should().ContainSingle();

            var loaded = _repo.GetSelectionRulePreset(id1);
            loaded.Should().NotBeNull();
            loaded!.PhaseStatus.Should().Be("Existing");
        }

        [Fact]
        public void GetSelectionRulePresetNames_OrderedByName()
        {
            _repo.UpsertSelectionRulePreset(new SelectionRulePreset { RuleName = "Zulu" });
            _repo.UpsertSelectionRulePreset(new SelectionRulePreset { RuleName = "Alpha" });
            _repo.UpsertSelectionRulePreset(new SelectionRulePreset { RuleName = "Mike" });

            var names = _repo.GetSelectionRulePresetNames().Select(r => r.Name).ToList();
            names.Should().ContainInOrder("Alpha", "Mike", "Zulu");
        }

        [Fact]
        public void DeleteSelectionRulePreset_Removes()
        {
            var id = _repo.UpsertSelectionRulePreset(new SelectionRulePreset { RuleName = "To Delete" });
            _repo.GetSelectionRulePresetNames().Should().ContainSingle();

            _repo.DeleteSelectionRulePreset(id);
            _repo.GetSelectionRulePresetNames().Should().BeEmpty();
        }

        [Fact]
        public void UpsertSelectionRulePreset_EmptyName_Throws()
        {
            Action act = () => _repo.UpsertSelectionRulePreset(new SelectionRulePreset { RuleName = "" });
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void GetSelectionRulePreset_UnknownId_ReturnsNull()
        {
            _repo.GetSelectionRulePreset(99999).Should().BeNull();
        }

        [Fact]
        public void GetSelectionRulePreset_LoadedJsonHasRuleName()
        {
            // Anche se il JSON salvato avesse RuleName vuoto, il repo popola dal campo Name.
            var preset = new SelectionRulePreset
            {
                RuleName = "Original Name",
                Category = "OST_Floors"
            };
            var id = _repo.UpsertSelectionRulePreset(preset);

            var loaded = _repo.GetSelectionRulePreset(id);
            loaded.Should().NotBeNull();
            loaded!.RuleName.Should().Be("Original Name");
            loaded.Category.Should().Be("OST_Floors");
        }
    }
}
