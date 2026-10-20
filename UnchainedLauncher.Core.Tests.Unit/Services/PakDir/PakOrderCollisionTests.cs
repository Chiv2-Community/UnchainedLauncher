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
        Occupy("Dependency_0_P.pak");
        Occupy(uppercaseCollision ? "DEPENDENCY_1_P.pak" : "Dependency_1_P.pak");
        Occupy("Dependency (1)_1_P.pak");
        Occupy("Dependent_0_P.pak");
        Occupy("Dependent_1_P.pak");
        var dir = new ManagedPakDirectory(_dir, []);
        (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        var original = Assert.Single(dir.ManagedPaks);
        Assert.Equal("Dependency (1)_0_P.pak", original.PakFileName);
        var originalPath = Path.Combine(_dir, original.PakFileName);
        var bytes = File.ReadAllBytes(originalPath);
        var sigBytes = File.ReadAllBytes(Path.ChangeExtension(originalPath, ".sig"));

        (await Install(dir, _dependent, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(2, _writes);
        Assert.Equal(new[] { _dependent, _dependency }, dir.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.Coordinates));
        var moved = dir.ManagedPaks.Single(p => p.Coordinates == _dependency);
        Assert.Equal("Dependency (2)_1_P.pak", moved.PakFileName);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_dir, moved.PakFileName)));
        Assert.Equal(sigBytes, File.ReadAllBytes(Path.ChangeExtension(Path.Combine(_dir, moved.PakFileName), ".sig")));
        Assert.False(File.Exists(originalPath));
        Assert.False(File.Exists(Path.ChangeExtension(originalPath, ".sig")));
        var names = dir.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.PakFileName).ToArray();
        // Reconstructing persisted inventory and retrying must not rename or download anything.
        dir = new ManagedPakDirectory(_dir, dir.ManagedPaks);
        (await Install(dir, _dependent, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(names, dir.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.PakFileName));
        Assert.Equal(2, _writes);

        var freshPath = Path.Combine(_dir, "fresh");
        Directory.CreateDirectory(freshPath);
        foreach (var (name, content) in _unmanaged) File.WriteAllBytes(Path.Combine(freshPath, name), content);
        File.WriteAllText(Path.Combine(freshPath, "pakchunk0-WindowsNoEditor.sig"), "signature");
        var fresh = new ManagedPakDirectory(freshPath, []);
        (await Install(fresh, _dependent, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(names, fresh.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.PakFileName));

        (await Install(dir, _dependency, _dependent)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(new[] { _dependency, _dependent }, dir.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.Coordinates));
        Assert.Equal("Dependency (1)_0_P.pak", dir.ManagedPaks.Single(p => p.Coordinates == _dependency).PakFileName);
        Assert.Equal("Dependent (1)_1_P.pak", dir.ManagedPaks.Single(p => p.Coordinates == _dependent).PakFileName);
        (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(original.PakFileName, Assert.Single(dir.ManagedPaks).PakFileName);
        Assert.Equal(4, _writes); // Only the two original downloads and two fresh-install downloads.
        AssertUnmanagedUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollisionRelocationFailureReturnsErrorWithoutPublishingNewInventory(bool signatureFailure) {
        Occupy("Dependency_0_P.pak");
        Occupy("Dependency_1_P.pak");
        var dir = new ManagedPakDirectory(_dir, []);
        (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        var original = Assert.Single(dir.ManagedPaks);
        var originalPath = Path.Combine(_dir, original.PakFileName);
        var bytes = File.ReadAllBytes(originalPath);
        var sigBytes = File.ReadAllBytes(Path.ChangeExtension(originalPath, ".sig"));
        var blocked = Path.Combine(_dir, "Dependency (1)_1_P.pak");
        var obstruction = signatureFailure ? Path.ChangeExtension(blocked, ".sig") : blocked;
        Directory.CreateDirectory(obstruction);
        var results = await Install(dir, _dependent, _dependency);
        Assert.Contains(results, r => r.IsLeft);
        Assert.Equal(original, dir.ManagedPaks.Single(p => p.Coordinates == _dependency));
        Assert.Equal(bytes, File.ReadAllBytes(originalPath));
        Assert.Equal(sigBytes, File.ReadAllBytes(Path.ChangeExtension(originalPath, ".sig")));
        if (!signatureFailure) Assert.False(File.Exists(Path.ChangeExtension(blocked, ".sig")));
        dir = new ManagedPakDirectory(_dir, dir.ManagedPaks);
        Assert.Equal(original, dir.ManagedPaks.Single(p => p.Coordinates == _dependency));
        Directory.Delete(obstruction);
        (await Install(dir, _dependent, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        var moved = dir.ManagedPaks.Single(p => p.Coordinates == _dependency);
        Assert.Equal("Dependency (1)_1_P.pak", moved.PakFileName);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_dir, moved.PakFileName)));
        Assert.Equal(sigBytes, File.ReadAllBytes(Path.ChangeExtension(Path.Combine(_dir, moved.PakFileName), ".sig")));
        Assert.False(File.Exists(originalPath));
        Assert.False(File.Exists(Path.ChangeExtension(originalPath, ".sig")));
        Assert.Single(Directory.GetFiles(_dir, "Dependency*.pak").Where(p => !_unmanaged.ContainsKey(Path.GetFileName(p))));
        Assert.Equal(2, _writes);
        AssertUnmanagedUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyMigrationPreservesSignatureOnlyCollisionsAcrossRestart(bool uppercaseCollision) {
        var legacy = new ManagedPak(_dependency, "q__-__Dependency.pak", 0);
        var source = Path.Combine(_dir, legacy.PakFileName);
        File.WriteAllText(source, "owned payload");
        File.WriteAllText(Path.ChangeExtension(source, ".sig"), "owned signature");
        var unmanagedSig = uppercaseCollision ? "DEPENDENCY_0_P.sig" : "Dependency_0_P.sig";
        var unmanagedBytes = System.Text.Encoding.UTF8.GetBytes("unmanaged signature");
        _unmanaged.Add(unmanagedSig, unmanagedBytes);
        File.WriteAllBytes(Path.Combine(_dir, unmanagedSig), unmanagedBytes);
        var dir = new ManagedPakDirectory(_dir, [legacy]);
        (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        var migrated = Assert.Single(dir.ManagedPaks);
        Assert.Equal("Dependency (1)_0_P.pak", migrated.PakFileName);
        dir = new ManagedPakDirectory(_dir, dir.ManagedPaks);
        (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal(migrated, Assert.Single(dir.ManagedPaks));
        Assert.Single(Directory.GetFiles(_dir, "*.pak"));
        Assert.Equal("owned payload", File.ReadAllText(Path.Combine(_dir, migrated.PakFileName)));
        Assert.Equal("owned signature", File.ReadAllText(Path.ChangeExtension(Path.Combine(_dir, migrated.PakFileName), ".sig")));
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(Path.ChangeExtension(source, ".sig")));
        Assert.Equal(0, _writes);
        AssertUnmanagedUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedLegacyPayloadSurvivesFailedMigrationAndRestart(bool signatureFailure) {
        var bytes = System.Text.Encoding.UTF8.GetBytes("immutable cache payload");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(bytes)).ToLowerInvariant();
        var request = new ModInstallRequest(_dependency, path => {
            _writes++;
            return LanguageExt.Prelude.RightAsync<Error, FileWriter>(new FileWriter(path, new MemoryStream(bytes), bytes.Length));
        }, hash);
        var cache = new PakCache(Path.Combine(_dir, "cache"));
        var legacy = new ManagedPak(_dependency, "q__-__Dependency.pak", 0);
        var source = Path.Combine(_dir, legacy.PakFileName);
        (await cache.Install(request, source, LanguageExt.Prelude.None)).IfLeft(e => throw e);
        File.WriteAllText(Path.ChangeExtension(source, ".sig"), "owned signature");
        var target = Path.Combine(_dir, "Dependency_0_P.pak");
        var obstruction = signatureFailure ? Path.ChangeExtension(target, ".sig") : target;
        Directory.CreateDirectory(obstruction);
        var dir = new ManagedPakDirectory(_dir, [legacy], cachePath: cache.Root);
        var failed = await dir.InstallModSet([request], LanguageExt.Prelude.None).ToListAsync();
        Assert.Contains(failed, r => r.IsLeft);
        Assert.Equal(legacy, Assert.Single(dir.ManagedPaks));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.Equal("owned signature", File.ReadAllText(Path.ChangeExtension(source, ".sig")));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(cache.Root, hash, "mod.pak")));
        dir = new ManagedPakDirectory(_dir, dir.ManagedPaks, cachePath: cache.Root);
        Directory.Delete(obstruction);
        (await dir.InstallModSet([request], LanguageExt.Prelude.None).ToListAsync()).ForEach(r => r.IfLeft(e => throw e));
        Assert.Equal("Dependency_0_P.pak", Assert.Single(dir.ManagedPaks).PakFileName);
        Assert.Single(Directory.GetFiles(_dir, "*.pak"));
        Assert.Equal(bytes, File.ReadAllBytes(target));
        Assert.Equal("owned signature", File.ReadAllText(Path.ChangeExtension(target, ".sig")));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(cache.Root, hash, "mod.pak")));
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(Path.ChangeExtension(source, ".sig")));
        Assert.Equal(1, _writes);
    }

    [Fact]
    public async Task SignatureCleanupFailureKeepsRelocatedPayloadOwnedAcrossRestart() {
        // Windows permits copying a read-only signature but rejects deleting it.
        if (!OperatingSystem.IsWindows()) return;
        var legacy = new ManagedPak(_dependency, "q__-__Dependency.pak", 0);
        var source = Path.Combine(_dir, legacy.PakFileName);
        var sourceSig = Path.ChangeExtension(source, ".sig");
        File.WriteAllText(source, "owned payload");
        File.WriteAllText(sourceSig, "owned signature");
        File.SetAttributes(sourceSig, FileAttributes.ReadOnly);
        try {
            var dir = new ManagedPakDirectory(_dir, [legacy]);
            Assert.Contains(await Install(dir, _dependency), r => r.IsLeft);
            var moved = Assert.Single(dir.ManagedPaks);
            Assert.Equal("Dependency_0_P.pak", moved.PakFileName);
            Assert.False(File.Exists(source));
            Assert.Equal("owned signature", File.ReadAllText(sourceSig));
            dir = new ManagedPakDirectory(_dir, dir.ManagedPaks);
            (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
            Assert.Equal(moved, Assert.Single(dir.ManagedPaks));
            Assert.Single(Directory.GetFiles(_dir, "*.pak"));
            Assert.Equal("owned payload", File.ReadAllText(Path.Combine(_dir, moved.PakFileName)));
            Assert.Equal("owned signature", File.ReadAllText(Path.Combine(_dir, "Dependency_0_P.sig")));
            Assert.Equal(0, _writes);
        }
        finally {
            foreach (var signature in Directory.GetFiles(_dir, "*.sig")) File.SetAttributes(signature, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task FailedSignatureRollbackPreservesSourceOwnershipAndBothSignatures() {
        if (!OperatingSystem.IsWindows()) return;
        var legacy = new ManagedPak(_dependency, "q__-__Dependency.pak", 0);
        var source = Path.Combine(_dir, legacy.PakFileName);
        var sourceSig = Path.ChangeExtension(source, ".sig");
        var target = Path.Combine(_dir, "Dependency_0_P.pak");
        var targetSig = Path.ChangeExtension(target, ".sig");
        File.WriteAllText(source, "owned payload");
        File.WriteAllText(sourceSig, "owned signature");
        // File.Copy retains ReadOnly, so cleanup of the staged signature also fails.
        File.SetAttributes(sourceSig, FileAttributes.ReadOnly);
        Directory.CreateDirectory(target);
        try {
            var dir = new ManagedPakDirectory(_dir, [legacy]);
            var failed = await Install(dir, _dependency);
            Assert.Contains(failed, r => r.IsLeft);
            failed.ForEach(r => r.IfLeft(e => Assert.Contains("signature cleanup also failed", e.ToString())));
            Assert.Equal(legacy, Assert.Single(dir.ManagedPaks));
            Assert.Equal("owned payload", File.ReadAllText(source));
            Assert.Equal("owned signature", File.ReadAllText(sourceSig));
            Assert.Equal("owned signature", File.ReadAllText(targetSig));

            dir = new ManagedPakDirectory(_dir, dir.ManagedPaks);
            Directory.Delete(target);
            File.SetAttributes(sourceSig, FileAttributes.Normal);
            (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
            var moved = Assert.Single(dir.ManagedPaks);
            Assert.Equal("Dependency (1)_0_P.pak", moved.PakFileName);
            Assert.Single(Directory.GetFiles(_dir, "*.pak"));
            Assert.Equal("owned payload", File.ReadAllText(Path.Combine(_dir, moved.PakFileName)));
            Assert.Equal("owned signature", File.ReadAllText(Path.Combine(_dir, "Dependency (1)_0_P.sig")));
            Assert.Equal("owned signature", File.ReadAllText(targetSig));
            Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(targetSig) & FileAttributes.ReadOnly);
            dir = new ManagedPakDirectory(_dir, dir.ManagedPaks);
            (await Install(dir, _dependency)).ForEach(r => r.IfLeft(e => throw e));
            Assert.Equal(moved, Assert.Single(dir.ManagedPaks));
            Assert.Equal(0, _writes);
        }
        finally {
            foreach (var signature in Directory.GetFiles(_dir, "*.sig")) File.SetAttributes(signature, FileAttributes.Normal);
        }
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
