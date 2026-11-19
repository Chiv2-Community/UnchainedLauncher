using LanguageExt;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using ManagedPakDirectory = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

public sealed class PakSignatureCollisionTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pak-signature-" + Guid.NewGuid().ToString("N"));
    private readonly ReleaseCoordinates _dependency = new("org", "Dependency", "1.0.0");
    private readonly ReleaseCoordinates _dependent = new("org", "Dependent", "1.0.0");
    private readonly Dictionary<string, string> _unrelated = new();
    private int _downloads;

    public PakSignatureCollisionTests() {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "pakchunk0-WindowsNoEditor.sig"), "installation signature");
    }

    private void Occupy(string name) {
        var contents = "unrelated " + name;
        _unrelated.Add(name, contents);
        File.WriteAllText(Path.Combine(_directory, name), contents);
    }

    private async Task Install(ManagedPakDirectory directory, params ReleaseCoordinates[] coordinates) {
        var results = await directory.InstallModSet(coordinates.Select(coordinate => new ModInstallRequest(coordinate, path => {
            _downloads++;
            var contents = System.Text.Encoding.UTF8.GetBytes(coordinate.ModuleName);
            return new FileWriter(path, new MemoryStream(contents), contents.Length);
        })), Option<AccumulatedMemoryProgress>.None).ToListAsync();
        Assert.Equal(coordinates.Length, results.Count);
        foreach (var result in results) result.IfLeft(error => throw error);
    }

    private void AssertUnrelatedUnchanged() {
        foreach (var (name, contents) in _unrelated)
            Assert.Equal(contents, File.ReadAllText(Path.Combine(_directory, name)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DownloadsSkipBaseAndSuffixSignaturesAndReuseManagedPak(bool basePak, bool uppercase) {
        var stem = uppercase ? "DEPENDENCY" : "Dependency";
        if (basePak) Occupy(stem + "_0_P.pak");
        Occupy(stem + "_0_P.sig");
        Occupy(stem + " (1)_0_P.sig");
        var directory = new ManagedPakDirectory(_directory, []);

        await Install(directory, _dependency);

        var installed = Assert.Single(directory.ManagedPaks);
        Assert.Equal("Dependency (2)_0_P.pak", installed.PakFileName);
        var path = Path.Combine(_directory, installed.PakFileName);
        Assert.Equal("Dependency", File.ReadAllText(path));
        Assert.Equal("installation signature", File.ReadAllText(Path.ChangeExtension(path, ".sig")));
        AssertUnrelatedUnchanged();

        // Restart from the persisted config snapshot, not a second inventory authority.
        directory = Reload(directory);
        await Install(directory, _dependency);
        Assert.Equal(installed, Assert.Single(directory.ManagedPaks));
        Assert.Equal(1, _downloads);
        AssertUnrelatedUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelocationSkipsSignaturesAndPreservesLoadOrderAndManagedReuse(bool basePak) {
        var directory = new ManagedPakDirectory(_directory, []);
        await Install(directory, _dependency);
        var original = Assert.Single(directory.ManagedPaks);
        var originalPath = Path.Combine(_directory, original.PakFileName);
        var contents = File.ReadAllBytes(originalPath);
        var signature = File.ReadAllBytes(Path.ChangeExtension(originalPath, ".sig"));
        if (basePak) Occupy("Dependency_1_P.pak");
        Occupy("Dependency_1_P.sig");
        Occupy("Dependency (1)_1_P.sig");

        await Install(directory, _dependent, _dependency);

        var relocated = directory.ManagedPaks.Single(pak => pak.Coordinates == _dependency);
        Assert.Equal("Dependency (2)_1_P.pak", relocated.PakFileName);
        var relocatedPath = Path.Combine(_directory, relocated.PakFileName);
        Assert.Equal(contents, File.ReadAllBytes(relocatedPath));
        Assert.Equal(signature, File.ReadAllBytes(Path.ChangeExtension(relocatedPath, ".sig")));
        Assert.False(File.Exists(originalPath));
        Assert.False(File.Exists(Path.ChangeExtension(originalPath, ".sig")));
        Assert.Equal(new[] { _dependent, _dependency }, directory.ManagedPaks
            .OrderBy(pak => pak.Priority).Select(pak => pak.Coordinates));

        directory = Reload(directory);
        await Install(directory, _dependent, _dependency);
        Assert.Equal(relocated, directory.ManagedPaks.Single(pak => pak.Coordinates == _dependency));
        Assert.Equal(2, _downloads);
        AssertUnrelatedUnchanged();

        await Install(directory, _dependency);
        Assert.Equal(original, Assert.Single(directory.ManagedPaks));
        Assert.Equal(contents, File.ReadAllBytes(originalPath));
        Assert.Equal(signature, File.ReadAllBytes(Path.ChangeExtension(originalPath, ".sig")));
        Assert.Equal(2, _downloads);
        AssertUnrelatedUnchanged();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ManagedPakDirectory Reload(ManagedPakDirectory directory) {
        var registry = Moq.Mock.Of<IModRegistry>();
        var config = Path.Combine(_directory, ".mod_cache", "mod_manager_config.json");
        new ModManagerPersistence(new ModManager(registry, directory, []), config).Save();
        return (ManagedPakDirectory)LegacyOwnershipMigration.Load(config, registry,
            installationPakDirectory: _directory).Manager.PakDir;
    }
}
