using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.Pak.Objects;
using Moq;
using Spectre.Console.Cli;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Processors;
using UnrealModScanner.Models;
using Xunit;

namespace UnchainedLauncher.PakScannerApp.Tests {
    public class AssetClassificationTests : IDisposable {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "scanner-classification-" + Guid.NewGuid());

        public AssetClassificationTests() => Directory.CreateDirectory(_root);

        [Theory]
        [InlineData("preset")]
        [InlineData("shipped-config")]
        [InlineData("cli")]
        [InlineData("full-prefix")]
        public async Task LegacyConfigAndCliClassifyAssetsLikeThePreset(string source) {
            var args = new List<string> { "--pak", _root, "--mode", "Mods" };
            if (source == "shipped-config")
                args.AddRange(["--config", Path.Combine(AppContext.BaseDirectory, "scanconfig_default.json")]);
            if (source == "cli" || source == "full-prefix")
                args.AddRange(["--official-dir", source == "cli" ? "UI" : "TBL/Content/UI",
                    "--official-dir", source == "cli" ? "Weapons" : "TBL/Content/Weapons"]);

            var resolved = await ParseSettings(args);
            AssertClassification(resolved, "TBL/Content/UI/Replacement.uasset", true);
            AssertClassification(resolved, "TBL/Content/Weapons/Replacement.uasset", true);
            AssertClassification(resolved, "TBL/Content/Mods/Custom/NewAsset.uasset", false);

            if (source == "shipped-config" || source == "preset")
                Assert.Equal(GameScanOptions.Chivalry2.VanillaAssetPaths, resolved.Options!.VanillaAssetPaths);
            else
                Assert.Equal(["TBL/Content/UI", "TBL/Content/Weapons"], resolved.Options!.VanillaAssetPaths);
        }

        [Theory]
        [InlineData("config")]
        [InlineData("cli")]
        [InlineData("options")]
        public async Task ExplicitCustomPrefixesAndFiltersArePreserved(string source) {
            var config = Path.Combine(_root, "custom.json");
            var options = GameScanOptions.Chivalry2 with {
                VanillaAssetPaths = ["OtherGame/Content/Official"],
                ScanFilter = new UnchainedLauncher.UnrealModScanner.Config.Blacklist(["Excluded"])
            };
            File.WriteAllText(config, JsonSerializer.Serialize(new ScanConfiguration {
                PakDirectory = _root,
                ScanMode = ScanMode.Mods,
                Options = options,
                OfficialDirectories = source == "options" ? null : source == "cli" ? ["UI"] : ["OtherGame/Content/Official"]
            }));
            var args = new List<string> { "--config", config };
            if (source == "cli") {
                // Override a conflicting legacy top-level value, not the nested custom filter.
                args.AddRange(["--official-dir", "OtherGame/Content/Official"]);
            }
            var resolved = await ParseSettings(args);
            Assert.Equal(options.VanillaAssetPaths, resolved.Options!.VanillaAssetPaths);
            AssertClassification(resolved, "OtherGame/Content/Official/Replacement.uasset", true);
            AssertClassification(resolved, "TBL/Content/UI/Replacement.uasset", false);
            AssertClassification(resolved, "OtherGame/Content/Official/Excluded.uasset", true, included: false);
        }

        private static async Task<ScanConfiguration> ParseSettings(IEnumerable<string> args) {
            ScanConfiguration? resolved = null;
            var app = new CommandApp<SettingsCaptureCommand>();
            app.WithData(new Action<ScanConfiguration>(settings => resolved = settings));
            app.Configure(config => config.PropagateExceptions());
            Assert.Equal(0, await app.RunAsync(args));
            Assert.NotNull(resolved);
            return resolved!;
        }

        // Use the real CLI binder and loader without performing pak/package I/O.
        public sealed class SettingsCaptureCommand : Command<ScanCommandSettings> {
            protected override int Execute(CommandContext context, ScanCommandSettings settings, CancellationToken cancellationToken) {
                ((Action<ScanConfiguration>)context.Data!)(ScanCommand.LoadSettings(settings));
                return 0;
            }
        }

        private static void AssertClassification(ScanConfiguration settings, string path, bool replacement, bool included = true) {
            var options = settings.Options!;
            var customEntry = CreateEntry("CustomReplacement.pak");
            var vanillaEntry = CreateEntry(options.VanillaPakNames.First());
            using var provider = FilteredFileProvider.CreateFromOptions(options, settings.ScanMode!.Value, settings.PakDirectory!);
            provider.Files.AddFiles(new Dictionary<string, GameFile> {
                [path] = customEntry,
                ["TBL/Content/UI/Vanilla.uasset"] = vanillaEntry
            }, 0);
            var filtered = provider.FilteredFiles.ToList();
            Assert.Equal(included ? 1 : 0, filtered.Count);
            if (!included) return;
            Assert.Equal(path, filtered[0].Key);

            // Synthetic pak identity and an empty package isolate classification from binary parsing.
            var context = new ScanContext(Mock.Of<IFileProvider>(), Mock.Of<IPackage>(), path, customEntry);
            var result = new PakScanResult();
            new ReplacementProcessor(options.VanillaPakNames, options.VanillaAssetPaths).Process(context, result);
            new ArbitraryAssetProcessor(options.VanillaPakNames, options.VanillaAssetPaths).Process(context, result);
            Assert.Equal(replacement ? 1 : 0, result._AssetReplacements.Count);
            Assert.Equal(replacement ? 0 : 1, result.ArbitraryAssets.Count);
        }

        private static FPakEntry CreateEntry(string pakName) {
            var reader = (PakFileReader)RuntimeHelpers.GetUninitializedObject(typeof(PakFileReader));
            SetField(reader, "<Name>k__BackingField", pakName);
            var entry = (FPakEntry)RuntimeHelpers.GetUninitializedObject(typeof(FPakEntry));
            SetField(entry, "<Vfs>k__BackingField", reader);
            return entry;
        }

        private static void SetField(object instance, string name, object value) {
            for (var type = instance.GetType(); type != null; type = type.BaseType) {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(instance, value);
                return;
            }
            throw new MissingFieldException(instance.GetType().FullName, name);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}