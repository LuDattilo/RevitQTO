using QtoRevitPlugin.Services;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint7
{
    public class MappingRulesServiceTests
    {
        [Fact]
        public void GetRule_ForWall_ReturnsAreaDefault()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var svc = new MappingRulesService(globalDir: tempDir);
                var rule = svc.GetRule("OST_Walls");
                Assert.Equal("Area", rule.DefaultParam);
                Assert.Contains("Area", rule.AllowedParams);
            }
            finally { Directory.Delete(tempDir, recursive: true); }
        }

        [Fact]
        public void GetRule_ForUnknownCategory_ReturnsFallback()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var svc = new MappingRulesService(globalDir: tempDir);
                var rule = svc.GetRule("OST_FurnitureSystems");
                Assert.Equal("Count", rule.DefaultParam);
            }
            finally { Directory.Delete(tempDir, recursive: true); }
        }

        [Fact]
        public void SaveAndLoad_RoundTrips()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var svc = new MappingRulesService(globalDir: tempDir);
                var config = svc.LoadGlobal();
                config.Rules[0].RoundingDecimals = 3;
                svc.SaveGlobal(config);

                var svc2 = new MappingRulesService(globalDir: tempDir);
                var reloaded = svc2.LoadGlobal();
                Assert.Equal(3, reloaded.Rules[0].RoundingDecimals);
            }
            finally { Directory.Delete(tempDir, recursive: true); }
        }
    }
}
