using FluentAssertions;
using QtoRevitPlugin.Services;
using System;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Data
{
    /// <summary>
    /// Test per <see cref="SharedParameterFileHelper"/> — gli helper filesystem
    /// per i file Shared Parameters di Revit. Vive in QtoRevitPlugin.Core
    /// (netstandard2.0) senza dipendenze Revit, quindi testabile automaticamente.
    /// I metodi che richiedono Revit API (CreateAndBindProjectInfoParam nel plugin)
    /// sono verificati manualmente in Revit.
    /// </summary>
    public class SharedParameterFileHelperTests
    {
        [Fact]
        public void GetCmeSpFilePath_ReturnsPathUnderQtoPluginFolder()
        {
            var path = SharedParameterFileHelper.GetCmeSpFilePath();

            path.Should().EndWith(SharedParameterFileHelper.CmeSpFileName);
            path.Should().Contain("QtoPlugin");
            Path.IsPathRooted(path).Should().BeTrue();
        }

        [Fact]
        public void GetCmeSpFilePath_CreatesFolderIfMissing()
        {
            // Chiamare GetCmeSpFilePath deve garantire che la cartella %AppData%\QtoPlugin\
            // esista (il file in sé è creato solo da EnsureSpFileExists).
            var path = SharedParameterFileHelper.GetCmeSpFilePath();
            var dir = Path.GetDirectoryName(path);

            Directory.Exists(dir).Should().BeTrue();
        }

        [Fact]
        public void EnsureSpFileExists_CreatesFileWithValidHeader()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"sp_test_{Guid.NewGuid():N}.txt");
            try
            {
                SharedParameterFileHelper.EnsureSpFileExists(tempPath);

                File.Exists(tempPath).Should().BeTrue();
                var content = File.ReadAllText(tempPath);
                // Revit richiede questo header esatto (case-sensitive) per aprire il file
                content.Should().Contain("# This is a Revit shared parameter file.");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void EnsureSpFileExists_NoOpIfFileAlreadyExists()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"sp_test_{Guid.NewGuid():N}.txt");
            try
            {
                // File pre-esistente con contenuto custom
                File.WriteAllText(tempPath, "# This is a Revit shared parameter file.\r\nGROUP\t1\tMioGruppo\r\n");
                var originalContent = File.ReadAllText(tempPath);

                SharedParameterFileHelper.EnsureSpFileExists(tempPath);

                // Il contenuto originale NON deve essere sovrascritto (comportamento idempotente)
                File.ReadAllText(tempPath).Should().Be(originalContent,
                    "EnsureSpFileExists non deve cancellare/sovrascrivere file SP esistenti");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void EnsureSpFileExists_NullOrEmptyPath_NoThrow()
        {
            // Safe-no-op per chiamate con input nullo (es. SharedParametersFilename mai impostato)
            SharedParameterFileHelper.EnsureSpFileExists("");
            SharedParameterFileHelper.EnsureSpFileExists(null!);
            // No assertion: il test passa se non throw
        }

        [Fact]
        public void EnsureSpFileExists_CreatesIntermediateDirectories()
        {
            var tempFolder = Path.Combine(Path.GetTempPath(), $"sp_nested_{Guid.NewGuid():N}");
            var nestedPath = Path.Combine(tempFolder, "sub", "deep", "CME.txt");
            try
            {
                Directory.Exists(tempFolder).Should().BeFalse();

                SharedParameterFileHelper.EnsureSpFileExists(nestedPath);

                File.Exists(nestedPath).Should().BeTrue();
            }
            finally
            {
                if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true);
            }
        }

        [Fact]
        public void SpFileHeader_ContainsRevitValidationString()
        {
            // Revit valida i file SP cercando questa stringa come prima riga (case-sensitive).
            // Non cambiarla senza verificare su un Revit reale.
            SharedParameterFileHelper.SpFileHeader
                .Should().StartWith("# This is a Revit shared parameter file.");
        }
    }
}
