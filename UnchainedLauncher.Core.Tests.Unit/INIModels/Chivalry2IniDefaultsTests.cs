using FluentAssertions;
using StructuredINI;
using UnchainedLauncher.Core.INIModels;

namespace UnchainedLauncher.Core.Tests.Unit.INIModels {
    public class Chivalry2IniDefaultsTests {
        [Fact]
        public void LoadOrDefault_WhenIniHasNoIpNetDriverSection_ShouldStillProvideNonNullIpNetDriver() {
            var tmpPath = Path.Combine(Path.GetTempPath(), $"unchainedlauncher_{Guid.NewGuid():N}_Engine.ini");

            try {
                // Ensure the INI has at least one section so StructuredINIReader.Load() succeeds,
                // while still omitting the [/Script/OnlineSubsystemUtils.IpNetDriver] section.
                File.WriteAllText(tmpPath, "[OtherSection]" + Environment.NewLine + "Foo=Bar" + Environment.NewLine);

                var engineIni = StructuredINIReader.LoadOrDefault(tmpPath, EngineINI.Default);

                engineIni.Should().NotBeNull();
                engineIni.IpNetDriver.Should().NotBeNull();
            }
            finally {
                if (File.Exists(tmpPath)) {
                    File.Delete(tmpPath);
                }
            }
        }
    }
}
