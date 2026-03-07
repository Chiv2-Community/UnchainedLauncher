using UnchainedLauncher.Core.Services;
using Semver;
using Xunit;
using System.IO;

namespace UnchainedLauncher.Core.Tests.Unit.Services.VersionExtraction {
    public class FileInfoVersionExtractorTests {
        private const string TestDataDir = "TestData/VersionTestFiles";

        [Theory]
        [InlineData("TestSemVer_1.2.3.dll", "1.2.3")]
        [InlineData("TestSemVer_0.1.2.dll", "0.1.2")]
        [InlineData("TestSemVer_1.0.0-RC0.dll", "1.0.0-RC0")]
        [InlineData("TestSemVer_2.1.0-alpha.5.dll", "2.1.0-alpha.5")]
        [InlineData("TestSemVer_3.0.0+build.123.dll", "3.0.0+build.123")]
        [InlineData("TestLegacy_1.2.3.4.dll", "1.2.3")] // Fallback to 3 components
        public void GetVersion_ShouldExtractCorrectVersion(string fileName, string expectedVersion) {
            var extractor = new FileInfoVersionExtractor();
            var filePath = Path.Combine(TestDataDir, fileName);

            Assert.True(File.Exists(filePath), $"Test file not found at {Path.GetFullPath(filePath)}");

            var version = extractor.GetVersion(filePath);
            
            Assert.NotNull(version);
            Assert.Equal(expectedVersion, version!.ToString());
        }

        [Fact]
        public void GetVersion_ShouldReturnNullForNonExistentFile() {
            var extractor = new FileInfoVersionExtractor();
            var version = extractor.GetVersion("non_existent_file.dll");
            Assert.Null(version);
        }
    }
}
