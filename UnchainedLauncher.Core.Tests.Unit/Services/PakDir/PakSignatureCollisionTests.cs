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
        var stem = uppercase ? "Q__-__DEPENDENCY" : "q__-__Dependency";
        if (basePak) Occupy(stem + ".pak");
        Occupy(stem + ".sig");
        Occupy(stem + " (1).sig");
        var directory = new ManagedPakDirectory(_directory, []);

        await Install(directory, _dependency);

        var installed = Assert.Single(directory.ManagedPaks);
        Assert.Equal("q__-__Dependency (2).pak", installed.PakFileName);
        var path = Path.Combine(_directory, installed.PakFileName);
        Assert.Equal("Dependency", File.ReadAllText(path));
        Assert.Equal("installation signature", File.ReadAllText(Path.ChangeExtension(path, ".sig")));
        AssertUnrelatedUnchanged();

        // Shared inventory, not a host snapshot, must reuse the collision-safe name and signature.
        directory = new ManagedPakDirectory(_directory, []);
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
        if (basePak) Occupy("qz__-__Dependency.pak");
        Occupy("qz__-__Dependency.sig");
        Occupy("qz__-__Dependency (1).sig");

        await Install(directory, _dependency, _dependent);

        var relocated = directory.ManagedPaks.Single(pak => pak.Coordinates == _dependency);
        Assert.Equal("qz__-__Dependency (2).pak", relocated.PakFileName);
        var relocatedPath = Path.Combine(_directory, relocated.PakFileName);
        Assert.Equal(contents, File.ReadAllBytes(relocatedPath));
        Assert.Equal(signature, File.ReadAllBytes(Path.ChangeExtension(relocatedPath, ".sig")));
        Assert.False(File.Exists(originalPath));
        Assert.False(File.Exists(Path.ChangeExtension(originalPath, ".sig")));
        Assert.Equal(new[] { _dependent, _dependency }, directory.ManagedPaks
            .OrderBy(pak => pak.PakFileName, StringComparer.OrdinalIgnoreCase).Select(pak => pak.Coordinates));

        directory = new ManagedPakDirectory(_directory, []);
        await Install(directory, _dependency, _dependent);
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
}