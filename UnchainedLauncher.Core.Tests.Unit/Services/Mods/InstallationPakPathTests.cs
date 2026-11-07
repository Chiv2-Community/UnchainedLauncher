using LanguageExt;
using Moq;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using PakDirectory = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

[Collection("Home launch UI")]
public sealed class InstallationPakPathTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "InstallationPakPath_" + Guid.NewGuid().ToString("N"));
    private readonly string _cwd = Directory.GetCurrentDirectory();
    private readonly string? _localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private readonly ReleaseCoordinates _coordinates = new("Org", "GiantSlayers", "v1");

    public InstallationPakPathTests() {
        Directory.CreateDirectory(_root);
        Directory.SetCurrentDirectory(_root);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(_root, "LocalAppData"));
        Directory.CreateDirectory(FilePaths.PakDir);
    }

    [Fact]
    public void NewAndMissingPakMetadataUseTheInstallationContentDirectory() {
        Assert.Equal(@"TBL\Content\Paks", FilePaths.PakDir);
        var codec = ModManagerCodec.ForInstallation(_registry);
        var json = new ModManagerCodec(_registry).Serialize(new ModManager(_registry, Mock.Of<IPakDir>(), []));
        Assert.Equal(Path.GetFullPath(FilePaths.PakDir), codec.Deserialize(json).Result!.PakDir.DirPath);
        Assert.Equal(FilePaths.PakDir, new ModManagerCodec(_registry).Deserialize(json).Result!.PakDir.DirPath);
        Assert.Equal(FilePaths.PakDir, new PakDirectory(FilePaths.PakDir, []).DirPath);
    }

    [Fact]
    public async Task CachedExternalPathDoesNotRedirectInstallsOrClaimSameNamedInstallationFiles() {
        var oldPath = Path.Combine(_root, "LocalAppData", "Chivalry 2", "Paks");
        Directory.CreateDirectory(oldPath);
        var name = "q__-__GiantSlayers.pak";
        File.WriteAllText(Path.Combine(oldPath, name), "old external pak");
        File.WriteAllText(Path.Combine(FilePaths.PakDir, name), "unknown installation pak");
        File.WriteAllText(Path.Combine(FilePaths.PakDir, Path.ChangeExtension(name, ".sig")), "unknown signature");
        var original = new ModManager(_registry, new PakDirectory(oldPath, [new(_coordinates, name, 0)]), [_coordinates]);
        var json = new ModManagerCodec(_registry).Serialize(original);

        var manager = ModManagerCodec.ForInstallation(_registry).Deserialize(json).Result!;

        Assert.Equal(Path.GetFullPath(FilePaths.PakDir), manager.PakDir.DirPath);
        Assert.Equal(new[] { _coordinates }, manager.EnabledModReleaseCoordinates);
        Assert.Empty(manager.PakDir.ManagedPaks);
        var results = await manager.PakDir.InstallModSet([new ModInstallRequest(_coordinates,
            path => new FileWriter(path, new MemoryStream([1, 2, 3]), 3))], Option<AccumulatedMemoryProgress>.None).ToListAsync();
        Assert.All(results, result => Assert.True(result.IsRight, result.ToString()));
        var installed = Assert.Single(manager.PakDir.ManagedPaks);
        Assert.NotEqual(name, installed.PakFileName);
        Assert.True(File.Exists(Path.Combine(FilePaths.PakDir, installed.PakFileName)));
        Assert.Equal("old external pak", File.ReadAllText(Path.Combine(oldPath, name)));
        Assert.Equal("unknown installation pak", File.ReadAllText(Path.Combine(FilePaths.PakDir, name)));
        Assert.Equal("unknown signature", File.ReadAllText(Path.Combine(FilePaths.PakDir, Path.ChangeExtension(name, ".sig"))));
        new ModManagerPersistence(manager, Path.Combine(_root, "mods.json")).Save();
        Assert.Equal(Path.GetFullPath(FilePaths.PakDir), new ModManagerCodec(_registry).Deserialize(File.ReadAllText("mods.json")).Result!.PakDir.DirPath);
    }

    [Fact]
    public void ProductionCodecMovesKnownSavedFilesBeforeBindingAndRetainsVerifiedOwnership() {
        var oldPath = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "Chivalry 2", "Saved_Unchained", "Paks");
        Directory.CreateDirectory(oldPath);
        File.WriteAllText(Path.Combine(oldPath, "managed.pak"), "managed bytes");
        File.WriteAllText(Path.Combine(oldPath, "managed.sig"), "signature bytes");
        var original = new ModManager(_registry, new PakDirectory(oldPath, [new(_coordinates, "managed.pak", 0)]), [_coordinates]);
        var manager = ModManagerCodec.ForInstallation(_registry).Deserialize(new ModManagerCodec(_registry).Serialize(original)).Result!;
        Assert.True(File.Exists(Path.Combine(FilePaths.PakDir, "managed.pak")));
        Assert.False(File.Exists(Path.Combine(oldPath, "managed.pak")));
        Assert.Equal("signature bytes", File.ReadAllText(Path.Combine(FilePaths.PakDir, "managed.sig")));
        Assert.Single(manager.PakDir.ManagedPaks);
    }

    [Fact]
    public void CachedInstallationPathPreservesOwnershipAndUsesItsBaseSignature() {
        var name = "q__-__GiantSlayers.pak";
        File.WriteAllText(Path.Combine(FilePaths.PakDir, name), "managed pak");
        File.WriteAllText(Path.Combine(FilePaths.PakDir, "pakchunk0-WindowsNoEditor.sig"), "base signature");
        var original = new ModManager(_registry, new PakDirectory(FilePaths.PakDir, [new(_coordinates, name, 0)]), [_coordinates]);
        var manager = ModManagerCodec.ForInstallation(_registry).Deserialize(new ModManagerCodec(_registry).Serialize(original)).Result!;
        Assert.Equal(original.PakDir.ManagedPaks, manager.PakDir.ManagedPaks);
        Assert.True(manager.PakDir.SignAll().IsRight);
        Assert.Equal("base signature", File.ReadAllText(Path.Combine(FilePaths.PakDir, Path.ChangeExtension(name, ".sig"))));
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _localAppData);
        Directory.SetCurrentDirectory(_cwd);
        Directory.Delete(_root, true);
    }
}