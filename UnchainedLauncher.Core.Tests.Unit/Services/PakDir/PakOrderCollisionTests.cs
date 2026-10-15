using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using ManagedPakDirectory = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

public sealed class PakOrderCollisionTests : IDisposable {
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pak-order-" + Guid.NewGuid().ToString("N"));
    private readonly ReleaseCoordinates _dependency = new("org", "Dependency", "1.0.0");
    private readonly ReleaseCoordinates _dependent = new("org", "Dependent", "1.0.0");
    private readonly Dictionary<string, byte[]> _unmanaged = new();
    private int _writes;

    public PakOrderCollisionTests() {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "pakchunk0-WindowsNoEditor.sig"), "signature");
    }

    private void Occupy(string name) {
        foreach (var file in new[] { name, Path.ChangeExtension(name, ".sig") }) {
            var bytes = System.Text.Encoding.UTF8.GetBytes("unmanaged " + file);
            _unmanaged.Add(file, bytes);
            File.WriteAllBytes(Path.Combine(_dir, file), bytes);
        }
    }

    private ValueTask<List<Either<Error, ManagedPak>>> Install(ManagedPakDirectory dir, params ReleaseCoordinates[] coords) =>
        dir.InstallModSet(coords.Select(c => new ModInstallRequest(c, path => {
            _writes++;
            return PakDirTest.StaticMkWriter(path);
        })), Option<AccumulatedMemoryProgress>.None).ToListAsync();

    private void AssertUnmanagedUnchanged() {
        foreach (var (name, bytes) in _unmanaged) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_dir, name)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrowthAndReorderingPreserveBytesAndMatchFreshInstallNames(bool uppercaseCollision) {
        Occupy("q__-__Dependency.pak");
        Occupy(uppercaseCollision ? "QZ__-__DEPENDENCY.pak" : "qz__-__Dependency.pak");
        Occupy("qz__-__Dependency (1).pak");
        Occupy("qy__-__Dependency.pak");
        Occupy("qz__-__Dependent.pak");
        var dir = new ManagedPakDirectory(_dir, []);
        (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        var original = Assert.Single(dir.ManagedPaks);
        Assert.Equal("q__-__Dependency (1).pak", original.PakFileName);
        var originalPath = Path.Combine(_dir, original.PakFileName);
        var bytes = File.ReadAllBytes(originalPath);
        var sigBytes = File.ReadAllBytes(Path.ChangeExtension(originalPath, ".sig"));

        (await Install(dir, _dependency, _dependent)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(2, _writes);
        Assert.Equal(new[] { _dependent, _dependency }, dir.ManagedPaks.OrderBy(p => p.PakFileName, StringComparer.OrdinalIgnoreCase).Select(p => p.Coordinates));
        var moved = dir.ManagedPaks.Single(p => p.Coordinates == _dependency);
        Assert.Equal("qz__-__Dependency (2).pak", moved.PakFileName);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_dir, moved.PakFileName)));
        Assert.Equal(sigBytes, File.ReadAllBytes(Path.ChangeExtension(Path.Combine(_dir, moved.PakFileName), ".sig")));
        Assert.False(File.Exists(originalPath));
        Assert.False(File.Exists(Path.ChangeExtension(originalPath, ".sig")));
        var names = dir.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.PakFileName).ToArray();
        // Reconstructing persisted inventory and retrying must not rename or download anything.
        dir = new ManagedPakDirectory(_dir, dir.ManagedPaks);
        (await Install(dir, _dependency, _dependent)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(names, dir.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.PakFileName));
        Assert.Equal(2, _writes);

        var freshPath = Path.Combine(_dir, "fresh");
        Directory.CreateDirectory(freshPath);
        foreach (var (name, content) in _unmanaged) File.WriteAllBytes(Path.Combine(freshPath, name), content);
        File.WriteAllText(Path.Combine(freshPath, "pakchunk0-WindowsNoEditor.sig"), "signature");
        var fresh = new ManagedPakDirectory(freshPath, []);
        (await Install(fresh, _dependency, _dependent)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(names, fresh.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.PakFileName));

        (await Install(dir, _dependent, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(new[] { _dependency, _dependent }, dir.ManagedPaks.OrderBy(p => p.PakFileName, StringComparer.OrdinalIgnoreCase).Select(p => p.Coordinates));
        Assert.Equal("qy__-__Dependency (1).pak", dir.ManagedPaks.Single(p => p.Coordinates == _dependency).PakFileName);
        Assert.Equal("qz__-__Dependent (1).pak", dir.ManagedPaks.Single(p => p.Coordinates == _dependent).PakFileName);
        (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(original.PakFileName, Assert.Single(dir.ManagedPaks).PakFileName);
        Assert.Equal(4, _writes); // Only the two original downloads and two fresh-install downloads.
        AssertUnmanagedUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollisionRelocationFailureReturnsErrorWithoutPublishingNewInventory(bool signatureFailure) {
        Occupy("q__-__Dependency.pak");
        Occupy("qz__-__Dependency.pak");
        var dir = new ManagedPakDirectory(_dir, []);
        (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        var original = Assert.Single(dir.ManagedPaks);
        var blocked = Path.Combine(_dir, "qz__-__Dependency (1).pak");
        Directory.CreateDirectory(signatureFailure ? Path.ChangeExtension(blocked, ".sig") : blocked);
        var results = await Install(dir, _dependency, _dependent);
        Assert.Contains(results, r => r.IsLeft);
        Assert.Equal(original, dir.ManagedPaks.Single(p => p.Coordinates == _dependency));
        AssertUnmanagedUnchanged();
    }

    public void Dispose() => Directory.Delete(_dir, true);
}