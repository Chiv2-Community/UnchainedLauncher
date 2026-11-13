using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.Pak.Objects;
using Moq;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;
using UnchainedLauncher.UnrealModScanner.Services;
using Xunit;

namespace UnchainedLauncher.PakScannerApp.Tests;

public class PakScanIdentityTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scanner-identity-" + Guid.NewGuid());
    private readonly string _path;
    private readonly PakFileReader _reader;

    public PakScanIdentityTests() {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "ScannedMod.pak");
        File.WriteAllText(_path, "pak contents");
        _reader = (PakFileReader)RuntimeHelpers.GetUninitializedObject(typeof(PakFileReader));
        SetField(_reader, "<Name>k__BackingField", Path.GetFileName(_path));
        SetField(_reader, "<Path>k__BackingField", _path);
    }

    [Fact]
    public void PakIdentityRetainsFullPathAndHashInManifest() {
        var scan = new ModScanResult();
        var pak = scan.GetOrAddPak(_reader);
        Assert.Same(pak, scan.GetOrAddPak(_reader));
        Assert.Equal(Path.GetFileName(_path), Assert.Single(scan.Paks).Key);
        var manifest = Assert.Single(ModManifestConverter.ProcessModScan(scan).Paks);
        Assert.Equal(_path, manifest.PakPath);
        Assert.True(File.Exists(manifest.PakPath));
        Assert.Equal(Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(_path))), manifest.PakHash, ignoreCase: true);
    }

    [Fact]
    public async Task SecondPassInitializesReferencedPakWithFullPathAndHash() {
        var entry = (FPakEntry)RuntimeHelpers.GetUninitializedObject(typeof(FPakEntry));
        SetField(entry, "<Vfs>k__BackingField", _reader);
        const string assetPath = "TBL/Content/Mods/Referenced.uasset";
        var provider = new Mock<IFileProvider>();
        using var fileIndex = FilteredFileProvider.CreateFromOptions(GameScanOptions.Chivalry2, ScanMode.Mods, _root);
        provider.SetupGet(p => p.Files).Returns(fileIndex.Files);
        provider.Object.Files.AddFiles(new Dictionary<string, GameFile> { [assetPath] = entry }, 0);
        var scan = new ModScanResult();
        await new SecondPassOrchestrator(provider.Object, GameScanOptions.Chivalry2).ResolveReferencesAsync(
            new ConcurrentBag<PendingBlueprintReference>([new() { TargetBlueprintPath = assetPath, SourcePakFile = "Marker.pak" }]), scan);
        var pak = Assert.Single(scan.Paks).Value;
        Assert.Equal(_path, pak.PakPath);
        Assert.Equal(Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(_path))), pak.PakHash, ignoreCase: true);
        Assert.Same(pak, scan.GetOrAddPak(_reader));
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
