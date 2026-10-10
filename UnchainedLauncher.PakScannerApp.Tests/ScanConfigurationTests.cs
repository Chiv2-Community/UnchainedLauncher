using Spectre.Console.Cli;
using System.Text.Json;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using Xunit;

namespace UnchainedLauncher.PakScannerApp.Tests {
    public class ScanConfigurationTests : IDisposable {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "scanner-config-" + Guid.NewGuid());

        public ScanConfigurationTests() => Directory.CreateDirectory(_root);

        [Theory]
        [InlineData("Mods", ScanMode.Mods)]
        [InlineData("GameInternal", ScanMode.GameInternal)]
        [InlineData("All", ScanMode.All)]
        public async Task ConfigOnlyCliScansConfiguredDirectoryAndWritesConfiguredOutput(string mode, ScanMode expected) {
            var input = Path.Combine(_root, "input");
            var output = Path.Combine(_root, "output");
            Directory.CreateDirectory(input);
            var config = WriteConfig(input, output, mode);
            var resolved = ScanCommand.LoadSettings(new ScanCommandSettings { ConfigFile = config });
            Assert.Equal(input, resolved.PakDirectory);
            Assert.Equal(output, resolved.OutputDirectory);
            Assert.Equal(expected, resolved.ScanMode);
            Assert.Equal(["CustomOfficial"], resolved.Options!.VanillaAssetPaths);

            Assert.Equal(0, await RunCli("scan", "--config", config));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            Assert.Equal(0, manifest.RootElement.GetProperty("paks").GetArrayLength());
        }

        [Fact]
        public async Task CliOverridesConfiguredInputOutputModeAndOfficialDirectories() {
            var input = Path.Combine(_root, "override-input");
            var output = Path.Combine(_root, "override-output");
            Directory.CreateDirectory(input);
            var config = WriteConfig(Path.Combine(_root, "missing"), Path.Combine(_root, "wrong-output"), "GameInternal");
            var resolved = ScanCommand.LoadSettings(new ScanCommandSettings {
                ConfigFile = config,
                PakDirectory = input,
                OutputDirectory = output,
                ScanMode = ScanMode.Mods,
                OfficialDirs = ["OverrideOfficial"]
            });
            Assert.Equal(ScanMode.Mods, resolved.ScanMode);
            Assert.Equal(["OverrideOfficial"], resolved.Options!.VanillaAssetPaths);
            Assert.Equal(0, await RunCli("scan", "--config", config, "--pak", input, "--out", output,
                "--mode", "Mods", "--official-dir", "OverrideOfficial"));
            Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
            Assert.False(Directory.Exists(Path.Combine(_root, "wrong-output")));
        }

        [Fact]
        public async Task ConfiguredMissingInputIsNotIgnored() {
            var output = Path.Combine(_root, "output");
            var config = WriteConfig(Path.Combine(_root, "missing"), output, "All");
            Assert.NotEqual(0, await RunCli("scan", "--config", config));
            Assert.False(File.Exists(Path.Combine(output, "manifest.json")));
        }

        [Fact]
        public void ScannerOptionsAreReadAlongsideTopLevelConfiguration() {
            var config = Path.Combine(_root, "config.json");
            var options = GameScanOptions.Chivalry2 with { VanillaAssetPaths = ["CustomPreset"] };
            File.WriteAllText(config, JsonSerializer.Serialize(new ScanConfiguration {
                PakDirectory = _root,
                ScanMode = ScanMode.GameInternal,
                Options = options
            }));
            var resolved = ScanCommand.LoadSettings(new ScanCommandSettings { ConfigFile = config });
            Assert.Equal(["CustomPreset"], resolved.Options!.VanillaAssetPaths);
            Assert.Equal(options.GameVersion, resolved.Options.GameVersion);
            Assert.Equal(ScanMode.GameInternal, resolved.ScanMode);
        }

        [Fact]
        public void MissingPakAndNullConfigFailAfterReadingConfig() {
            var config = Path.Combine(_root, "config.json");
            File.WriteAllText(config, "{}");
            Assert.Throws<InvalidOperationException>(() => ScanCommand.LoadSettings(new ScanCommandSettings { ConfigFile = config }));
            File.WriteAllText(config, "null");
            Assert.Throws<InvalidOperationException>(() => ScanCommand.LoadSettings(new ScanCommandSettings { ConfigFile = config }));
            Assert.Throws<FileNotFoundException>(() => ScanCommand.LoadSettings(new ScanCommandSettings { ConfigFile = config + ".missing" }));
        }

        private string WriteConfig(string input, string output, string mode) {
            var path = Path.Combine(_root, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new {
                PakDirectory = input,
                OutputDirectory = output,
                ScanMode = mode,
                OfficialDirectories = new[] { "CustomOfficial" }
            }));
            return path;
        }

        private static Task<int> RunCli(params string[] args) {
            var app = new CommandApp();
            app.Configure(config => config.AddCommand<ScanCommand>("scan"));
            return app.RunAsync(args);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}