using UnchainedLauncher.Core.Services.Mods;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ServerConfigurationFixtureTests {
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "test-resources", "server-configuration");

    public static IEnumerable<object[]> Cases() => Directory.GetDirectories(Fixtures)
        .OrderBy(path => path, StringComparer.Ordinal).Select(path => new object[] { Path.GetFileName(path) });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ServerYamlProducesExpectedFiles(string testCase) {
        var fixture = Path.Combine(Fixtures, testCase);
        var temporary = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()),
            "server-configuration-fixture-" + Guid.NewGuid().ToString("N"));
        var input = Path.Combine(temporary, "input");
        var output = Path.Combine(temporary, "output");
        Directory.CreateDirectory(input);
        try {
            foreach (var file in Directory.GetFiles(Path.Combine(fixture, "input"), "*", SearchOption.AllDirectories)) {
                var destination = Path.Combine(input, Path.GetRelativePath(Path.Combine(fixture, "input"), file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            var originalInput = Directory.GetFiles(input, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(input, path), File.ReadAllBytes);
            var command = YamlConfiguration.Load(Path.Combine(input, "server.yaml"), client: false);
            var servers = Assert.IsType<ServerGroupOptions>(command).Servers;
            foreach (var (id, server) in servers) {
                server.Shared.Assets = new ProfileAssetPaths(id, Path.Combine(temporary, "assets"));
                var existingProfile = Path.Combine(input, id);
                if (Directory.Exists(existingProfile)) {
                    foreach (var file in Directory.GetFiles(existingProfile, "*", SearchOption.AllDirectories)) {
                        var target = Path.Combine(output, id, Path.GetRelativePath(existingProfile, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target);
                    }
                }
            }
            var expected = ReadTree(Path.Combine(fixture, "output"));
            Assert.NotEmpty(expected);
            for (var pass = 0; pass < 2; pass++) {
                foreach (var (id, server) in servers) {
                    ServerLauncher.InitializeServerProfile(server, _ => Path.Combine(output, id, "Config", "WindowsNoEditor"));
                    foreach (var ini in Directory.GetFiles(Path.Combine(output, id, "Config", "WindowsNoEditor"), "*.ini"))
                        Assert.All(new StructuredINI.StructuredINIParser().ReadEntries(File.ReadAllText(ini)),
                            entry => Assert.DoesNotContain(entry.Key[0], "!+.-"));
                    await MapProfileIni.PrepareAsync(server, CancellationToken.None);
                    if (server.MapProfilePath is not null) CopyRuntimeProfile(server.MapProfilePath, Path.Combine(output, id, "unchained"));
                }
                AssertTree($"{testCase} pass {pass + 1}", expected, ReadTree(output));
            }
            Assert.Equal(originalInput.Keys.OrderBy(path => path, StringComparer.Ordinal),
                Directory.GetFiles(input, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(input, path)).OrderBy(path => path, StringComparer.Ordinal));
            foreach (var (path, bytes) in originalInput) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(input, path)));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void CopyRuntimeProfile(string profileRoot, string destination) {
        foreach (var file in Directory.GetFiles(profileRoot, "*", SearchOption.AllDirectories)) {
            var target = Path.Combine(destination, Path.GetRelativePath(profileRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static SortedDictionary<string, string> ReadTree(string directory) => new(Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), path => File.ReadAllText(path).Replace("\r\n", "\n")), StringComparer.Ordinal);

    private static void AssertTree(string testCase, SortedDictionary<string, string> expected, SortedDictionary<string, string> actual) {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var (path, contents) in expected)
            Assert.True(contents == actual[path], $"{testCase}: {path}\nExpected:\n{contents}\nActual:\n{actual[path]}");
    }
}
