using FluentAssertions;
using StructuredINI;
using UnchainedLauncher.Core.INIModels;
using UnchainedLauncher.Core.INIModels.Game;

namespace UnchainedLauncher.Core.Tests.Unit.INIModels {
    public class ArenaGameModeTimeBetweenRoundsTests {
        [Fact]
        public void LoadOrDefault_WhenTimeBetweenRoundsIsInt_ShouldParseAsInt() {
            var tmpPath = Path.Combine(Path.GetTempPath(), $"unchainedlauncher_{Guid.NewGuid():N}_Game.ini");

            try {
                File.WriteAllText(tmpPath,
                    "[/Script/TBL.ArenaGameMode]" + Environment.NewLine +
                    "TimeBetweenRounds=25" + Environment.NewLine
                );

                var gameIni = StructuredINIReader.LoadOrDefault(tmpPath, GameINI.Default);

                gameIni.ArenaGameMode.TimeBetweenRounds.Should().Be(25);
            }
            finally {
                if (File.Exists(tmpPath)) {
                    File.Delete(tmpPath);
                }
            }
        }

        [Fact]
        public void SaveAndReload_WhenTimeBetweenRoundsIsCustomized_ShouldRoundTrip() {
            var tmpPath = Path.Combine(Path.GetTempPath(), $"unchainedlauncher_{Guid.NewGuid():N}_Game.ini");

            try {
                var gameIni = new GameINI(
                    new GameSession(),
                    new TBLGameMode(),
                    new LTSGameMode(),
                    new ArenaGameMode(TimeBetweenRounds: 12),
                    null,
                    null
                );

                StructuredINIWriter.Save(tmpPath, gameIni).Should().BeTrue();

                var loaded = StructuredINIReader.LoadOrDefault(tmpPath, GameINI.Default);

                loaded.ArenaGameMode.TimeBetweenRounds.Should().Be(12);
            }
            finally {
                if (File.Exists(tmpPath)) {
                    File.Delete(tmpPath);
                }
            }
        }
    }
}