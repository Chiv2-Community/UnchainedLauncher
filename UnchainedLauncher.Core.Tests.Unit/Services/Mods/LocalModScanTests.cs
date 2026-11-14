using Moq;
using System.Security.Cryptography;
using System.Text;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

[Collection("Home launch UI")]
public class LocalModScanTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "local-mod-scan-" + Guid.NewGuid());
    private string Pak => Path.Combine(directory, "local.pak");

    public LocalModScanTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private LocalModScanVM Create(params ManagedPak[] managed) {
        var manager = new ModManager(Mock.Of<IModRegistry>(), new Core.Services.Mods.PakDir(directory, managed), [], []);
        return new LocalModScanVM(manager, new ModScanTabVM { DefaultPakDirectory = directory });
    }

    [Fact]
    public async Task NoUnmanagedPaksHidesScanAction() {
        File.WriteAllText(Path.Combine(directory, GameScanOptions.Chivalry2.VanillaPakNames.First()), "vanilla must not open");
        var vm = Create();
        await vm.RefreshAsync();
        Assert.False(vm.HasUnmanagedPaks);
        Assert.False(vm.CanScan);
        Assert.Equal(0, vm.LocalModActorCount);
    }

    [Fact]
    public async Task UnscannedPakEnablesScanAndRemovalHidesIt() {
        File.WriteAllText(Pak, "unscanned");
        var vm = Create();
        await vm.RefreshAsync();
        Assert.True(vm.HasUnmanagedPaks);
        Assert.True(vm.CanScan);
        File.Delete(Pak);
        await vm.RefreshAsync();
        Assert.False(vm.HasUnmanagedPaks);
        Assert.False(vm.CanScan);
    }

    [Fact]
    public async Task UnknownManagedOwnershipIsNotUnmanaged() {
        File.WriteAllText(Pak, "managed");
        var vm = Create(new ManagedPak(new ReleaseCoordinates("Org", "Managed", "1.0.0"), Path.GetFileName(Pak), 0));
        await vm.RefreshAsync();
        Assert.False(vm.HasUnmanagedPaks);
        Assert.False(vm.CanScan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagedOwnershipMustMatchTheCurrentPak(bool replaced) {
        File.WriteAllText(Pak, "managed");
        var hash = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(Pak)));
        var release = new Release("v1.0.0", hash, Path.GetFileName(Pak), DateTime.UtcNow,
            new("Org/Managed", "Managed", "", null, null, [], [], []), new([], [], [], [], []), "");
        var manager = new ModManager(Mock.Of<IModRegistry>(), new Core.Services.Mods.PakDir(directory,
            [new ManagedPak(ReleaseCoordinates.FromRelease(release), Path.GetFileName(Pak), 0)]), [],
            [new Mod(release.Info, [release])]);
        if (replaced) File.WriteAllText(Pak, "replacement");
        var vm = new LocalModScanVM(manager, new ModScanTabVM());
        await vm.RefreshAsync();
        Assert.Equal(replaced, vm.HasUnmanagedPaks);
        Assert.Equal(replaced, vm.CanScan);
    }

    [Fact]
    public async Task SharedScanDisablesActionWhileRunningAndRefreshesItAfterward() {
        WriteEmptyPak();
        var vm = Create();
        await vm.RefreshAsync();
        var scan = vm.Scanner.ScanAsync(directory);
        Assert.True(vm.Scanner.IsScanning);
        Assert.False(vm.CanScan);
        await scan;
        await vm.RefreshAsync();
        Assert.False(vm.CanScan);
    }

    [Fact]
    public async Task SuccessfulScanDisablesActionAndReusesResultsAcrossRestart() {
        WriteEmptyPak();
        var vm = Create();
        await vm.RefreshAsync();
        Assert.True(vm.CanScan);
        await vm.ScanAsync();
        Assert.True(vm.HasUnmanagedPaks);
        Assert.False(vm.CanScan);
        Assert.NotNull(vm.Scanner.LastScanResult);
        var restarted = Create();
        await restarted.RefreshAsync();
        Assert.True(restarted.HasUnmanagedPaks);
        Assert.False(restarted.CanScan);
        File.AppendAllText(Pak, "changed");
        await restarted.RefreshAsync();
        Assert.True(restarted.CanScan);
    }

    [Fact]
    public async Task FailedScanKeepsActionAvailable() {
        File.WriteAllText(Pak, "not a pak");
        var vm = Create();
        await vm.RefreshAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => vm.ScanAsync());
        Assert.True(vm.HasUnmanagedPaks);
        Assert.True(vm.CanScan);
    }

    [Fact]
    public async Task ActorCountUsesOnlyCurrentServerSelectableLocalActors() {
        File.WriteAllText(Pak, "scanned");
        await PakScanCache.ScanAsync(directory, GameScanOptions.Chivalry2, hashes => {
            var result = new ModScanResult();
            var pak = new PakScanResult { PakPath = Pak, PakHash = hashes[Pak] };
            foreach (var visible in new[] { true, false }) {
                var actor = new GenericAssetEntry { AssetPath = $"Actor{visible}.uasset" };
                actor.Properties["ModName"] = "Local";
                actor.Properties["bShowInGUI"] = visible;
                pak.AddGenericEntry(actor, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            }
            result.Paks[Path.GetFileName(Pak)] = pak;
            return Task.FromResult(result);
        });
        var vm = Create();
        await vm.RefreshAsync();
        Assert.False(vm.CanScan);
        Assert.Equal(1, vm.LocalModActorCount);
        File.Delete(Pak);
        await vm.RefreshAsync();
        Assert.Equal(0, vm.LocalModActorCount);
    }

    private void WriteEmptyPak() {
        using var stream = File.Create(Pak);
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[512]);
        var indexOffset = stream.Position;
        var mount = Encoding.UTF8.GetBytes("../../../\0");
        writer.Write(mount.Length);
        writer.Write(mount);
        writer.Write(0);
        var indexSize = stream.Position - indexOffset;
        writer.Write(0x5A6F12E1u);
        writer.Write(3);
        writer.Write(indexOffset);
        writer.Write(indexSize);
        writer.Write(new byte[20]);
    }
}