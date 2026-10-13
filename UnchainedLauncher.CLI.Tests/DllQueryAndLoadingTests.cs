using System.CommandLine;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class DllQueryAndLoadingTests : IDisposable {
    private readonly string _directory = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "dll-query-" + Guid.NewGuid().ToString("N"));
    public DllQueryAndLoadingTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("client")]
    [InlineData("server")]
    public void UnsafePermissionIsExplicitAndHiddenFromGeneratedHelp(string command) {
        string[] args = [command, "--chivalry2-path", ".", "--platform", "Steam"];
        Assert.False(CliLaunchOptions.Parse(args).Shared.UnsafeLoadAllDlls);
        Assert.True(CliLaunchOptions.Parse([.. args, UnsafeDllLoading.FlagName]).Shared.UnsafeLoadAllDlls);
        Assert.False(CliLaunchOptions.Parse([.. args, "--", UnsafeDllLoading.FlagName]).Shared.UnsafeLoadAllDlls);
        var help = CommandLineOptions.Parse([command, "--help"]);
        Assert.DoesNotContain(UnsafeDllLoading.FlagName, help.HelpText);
    }

    [Fact]
    public void UnsafePermissionSurvivesAuthoritativeYamlOnlyWhenExplicitlyOnCommandLine() {
        var path = Path.Combine(_directory, "launch.yaml");
        File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\n");
        Assert.False(CliLaunchOptions.Parse(["client", "--config-path", path]).Shared.UnsafeLoadAllDlls);
        Assert.True(CliLaunchOptions.Parse(["client", "--config-path", path, UnsafeDllLoading.FlagName]).Shared.UnsafeLoadAllDlls);
        File.AppendAllText(path, "unsafe-very-dangerous-load-all-dlls: true\n");
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["client", "--config-path", path]));
    }

    [Theory]
    [InlineData("--unsafe-very-dangerous-load-all-dlls=true")]
    [InlineData("--unsafe-very-dangerous-load-all-dlls=false")]
    public void PermissionIsAValueLessFlagEvenWhenYamlIgnoresOtherArguments(string flag) {
        var path = Path.Combine(_directory, "launch.yaml");
        File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\n");
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--config-path", path, flag]));
        Assert.Throws<ArgumentException>(() => UnsafeDllLoading.ParseGuiArguments([flag]));
    }

    [Fact]
    public void DuplicatePermissionAndQueryPermissionAreRejected() {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["client", "--chivalry2-path", ".", "--platform", "Steam", UnsafeDllLoading.FlagName, UnsafeDllLoading.FlagName]));
        Assert.Throws<ArgumentException>(() => UnsafeDllLoading.ParseGuiArguments([UnsafeDllLoading.FlagName, UnsafeDllLoading.FlagName]));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["dll-query", "list", UnsafeDllLoading.FlagName]));
    }

    [Fact]
    public void GuiPermissionIsRemovedFromGameArgumentsAndDoesNotConsumeAuthOrSeparatorTail() {
        string[] args = ["--startunchained", "-AUTH_PASSWORD=secret with spaces", UnsafeDllLoading.FlagName, "--", UnsafeDllLoading.FlagName];
        var parsed = UnsafeDllLoading.ParseGuiArguments(args);
        Assert.True(parsed.Enabled);
        Assert.Equal(new[] { "--startunchained", "-AUTH_PASSWORD=secret with spaces", "--", UnsafeDllLoading.FlagName }, parsed.RemainingArguments);
        var tailOnly = UnsafeDllLoading.ParseGuiArguments(["--", UnsafeDllLoading.FlagName]);
        Assert.False(tailOnly.Enabled);
        Assert.Equal(new[] { "--", UnsafeDllLoading.FlagName }, tailOnly.RemainingArguments);
    }

    [Fact]
    public void GuiUnsafeOptionIsHiddenAndResponseFilesCannotGrantPermission() {
        var root = new RootCommand();
        var flag = UnsafeDllLoading.CreateOption();
        root.Options.Add(flag);
        Assert.True(flag.Hidden);
        Assert.False(UnsafeDllLoading.ParseGuiArguments(["@not-a-permission-file", "--startunchained"]).Enabled);
        Assert.False(UnsafeDllLoading.ParseGuiArguments(["-AUTH_PASSWORD=" + UnsafeDllLoading.FlagName]).Enabled);
    }

    [Fact]
    public void DefaultInjectionSelectsOnlyRequiredPluginAndUnsafeSelectsAllRecursiveDlls() {
        var nested = Path.Combine(_directory, "nested");
        Directory.CreateDirectory(nested);
        foreach (var file in new[] { "UnchainedPlugin.dll", "Unexpected.dll", "not-a-dll.txt", "nested/Other.DLL" })
            File.WriteAllText(Path.Combine(_directory, file), "not executed");
        Assert.Empty(new DllInjector(_directory).GetDllPaths());
        Assert.Equal(3, new DllInjector(_directory, unsafeLoadAllDlls: true).GetDllPaths().Count());
        Assert.Empty(new DllInjector(Path.Combine(_directory, "missing")).GetDllPaths());
    }

    private const string Info = """
        {"repo_url":"https://github.com/org/plugin","name":"Example plugin","description":"Native plugin","authors":["Author"],"plugin_dependencies":[{"repo_url":"https://github.com/org/native","version":">=1.0.0"}],"mod_dependencies":[{"repo_url":"https://github.com/org/pak","version":"^1.0.0"}]}
        """;

    private LocalModRegistry Registry() {
        var packages = Path.Combine(_directory, "dll_db", "packages", "org");
        Directory.CreateDirectory(packages);
        string Release(string tag, int day) => $$"""
            {"tag":"{{tag}}","hash":"{{new string('a', 64)}}","dll_file_name":"Example.dll","download_url":"https://example.test/Example.dll","size":12345,"release_date":"2026-01-0{{day}}T00:00:00Z","info":{{Info}},"release_notes_markdown":"Notes"}
            """;
        File.WriteAllText(Path.Combine(packages, "plugin.json"), $$"""
            {"latest_release_info":{{Info}},"releases":[{{Release("v1.0.0", 1)}},{{Release("v2.0.0", 2)}},{{Release("v3.0.0-rc.1", 3)}}]}
            """);
        return new LocalModRegistry(_directory);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("versions")]
    public void DllCommandsReuseOneParserAndHidePermissionInHelp(string operation) {
        var count = 0;
        var help = CommandLineOptions.Parse(["dll-query", operation, "--help"], (root, tokens, config) => {
            count++;
            return root.Parse(tokens, config);
        });
        Assert.Equal(1, count);
        Assert.True(help.Help);
        Assert.DoesNotContain(UnsafeDllLoading.FlagName, help.HelpText);
        Assert.Contains("--add-registry", help.HelpText);
    }

    [Fact]
    public async Task DllQueryReadsExistingSchemaAndShowsIndependentDependencyKinds() {
        var local = Registry();
        var registry = new AggregateModRegistry(local, local);
        var list = Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(["dll-query", "list"]));
        Assert.Equal(RegistryPackageKind.Dll, list.Kind);
        var output = new StringWriter();
        Assert.Equal(0, await DllQuery.RunAsync(list, registry, output, new StringWriter(), CancellationToken.None));
        Assert.Contains("org/plugin\tExample plugin\tv2.0.0", output.ToString());
        var detail = Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(["dll-query", "get", "org/plugin", "--version", "v1.0.0"]));
        output = new StringWriter();
        Assert.Equal(0, await DllQuery.RunAsync(detail, registry, output, new StringWriter(), CancellationToken.None));
        foreach (var expected in new[] { "Version: v1.0.0", "Target: shared", "DLL file: Example.dll", "Hash (SHA-256): " + new string('a', 64), "Size: 12345 bytes", "org/native: >=1.0.0", "org/pak: ^1.0.0", "Release notes: Notes" })
            Assert.Contains(expected, output.ToString());
        var versions = Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(["dll-query", "versions", "org/plugin"]));
        output = new StringWriter();
        Assert.Equal(0, await DllQuery.RunAsync(versions, registry, output, new StringWriter(), CancellationToken.None));
        Assert.Contains("v3.0.0-rc.1", output.ToString());
        Assert.Equal(3, (await DllCatalog.ReadAsync(registry, CancellationToken.None)).Packages.Single().Releases.Count);
    }

    [Fact]
    public async Task MissingAndMalformedDllMetadataAreReportedWithoutLoadingAnything() {
        var registry = new AggregateModRegistry(Registry());
        var query = Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(["dll-query", "get", "org/missing"]));
        Assert.Equal(1, await DllQuery.RunAsync(query, registry, new StringWriter(), new StringWriter(), CancellationToken.None));
        File.WriteAllText(Path.Combine(_directory, "dll_db", "packages", "org", "plugin.json"), "{}");
        var catalog = await DllCatalog.ReadAsync(registry, CancellationToken.None);
        Assert.Empty(catalog.Packages);
        Assert.Single(catalog.Errors);
    }

    [Fact]
    public void DllQueryUsesConfiguredRegistriesAndQueryYamlCannotGrantUnsafePermission() {
        Registry();
        var path = Path.Combine(_directory, "query.yaml");
        File.WriteAllText(path, "mod-setup: {registries: ['local:.']}\n");
        var query = Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(["dll-query", "get", "org/plugin", "--config-path", path, "--version", "v1.0.0"]));
        Assert.Equal(RegistryPackageKind.Dll, query.Kind);
        Assert.Equal(_directory, Assert.Single(query.RegistryOptions.Registries).Location.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Equal("v1.0.0", query.Version);
        Assert.Equal(2, query.RegistryOptions.CreateRegistry().ModRegistries.Count);
        File.AppendAllText(path, "unsafe-very-dangerous-load-all-dlls: true\n");
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["dll-query", "list", "--config-path", path]));
    }

    [Theory]
    [InlineData("\"latest_release_info\":null,\"releases\":[]")]
    [InlineData("\"latest_release_info\":{},\"releases\":null")]
    public async Task InvalidNullMetadataIsReportedAsCatalogError(string body) {
        var registry = new AggregateModRegistry(Registry());
        File.WriteAllText(Path.Combine(_directory, "dll_db", "packages", "org", "plugin.json"), "{" + body + "}");
        var catalog = await DllCatalog.ReadAsync(registry, CancellationToken.None);
        Assert.Single(catalog.Errors);
        Assert.Empty(catalog.Packages);
    }
}
