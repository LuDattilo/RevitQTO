using FluentAssertions;
using QtoRevitPlugin.Reports;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint10
{
    public class ExportTemplateRepositoryTests : IDisposable
    {
        private readonly string _tempFolder;

        public ExportTemplateRepositoryTests()
        {
            _tempFolder = Path.Combine(Path.GetTempPath(), $"tpl_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempFolder);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true);
        }

        [Fact]
        public void Ctor_OnEmptyFolder_SeedsStandardAndDei2024()
        {
            var repo = new ExportTemplateRepository(_tempFolder);
            var all = repo.LoadAll();
            all.Should().HaveCount(2);
            all.Select(t => t.Name).Should().BeEquivalentTo(new[] { "Standard", "DEI2024" });
        }

        [Fact]
        public void SaveTemplate_AndLoad_RoundTripsCustomFields()
        {
            var repo = new ExportTemplateRepository(_tempFolder);
            var custom = new ExportTemplate
            {
                Name = "Custom1",
                DisplayName = "Mio template",
                HeaderColorHex = "#AB12CD",
                Footer = "Test footer"
            };
            repo.SaveTemplate(custom);

            var all = repo.LoadAll();
            all.Should().HaveCount(3);
            var reloaded = all.First(t => t.Name == "Custom1");
            reloaded.DisplayName.Should().Be("Mio template");
            reloaded.HeaderColorHex.Should().Be("#AB12CD");
            reloaded.Footer.Should().Be("Test footer");
        }

        [Fact]
        public void GetDefault_ReturnsStandard_WhenPresent()
        {
            var repo = new ExportTemplateRepository(_tempFolder);
            var def = repo.GetDefault();
            def.Name.Should().Be("Standard");
            def.HeaderColorHex.Should().Be("#1E6FD9");
        }
    }
}
