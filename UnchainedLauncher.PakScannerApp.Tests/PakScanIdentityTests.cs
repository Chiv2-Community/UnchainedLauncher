using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.Pak.Objects;
using Moq;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;
using UnchainedLauncher.UnrealModScanner.PakScanning.Processors;
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
    public async Task ProviderTracksInterfaceMissesPayloadsAndParallelSourceScopes() {
        using var provider = FilteredFileProvider.CreateFromOptions(GameScanOptions.Chivalry2, ScanMode.Mods, _root);
        IFileProvider resolver = provider;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async index => {
            using var scope = provider.TrackScan($"owner{index}.pak");
            await Task.Yield();
            Assert.False(resolver.TryLoadPackage($"TBL/Content/Import{index}", out _));
            Assert.False(resolver.TryGetGameFile($"TBL/Content/Target{index}.uasset", out _));
            Assert.False(resolver.TrySaveAsset($"TBL/Content/Hash{index}.uasset", out _));
        }));
        for (var index = 0; index < 8; index++) {
            var paths = provider.GetScanLookups($"owner{index}.pak");
            Assert.Contains($"TBL/Content/Import{index}.uasset", paths);
            Assert.Contains($"TBL/Content/Import{index}.umap", paths);
            Assert.Contains($"TBL/Content/Import{index}.uexp", paths);
            Assert.Contains($"TBL/Content/Import{index}.ubulk", paths);
            Assert.Contains($"TBL/Content/Import{index}.uptnl", paths);
            Assert.Contains($"TBL/Content/Target{index}.uasset", paths);
            Assert.Contains($"TBL/Content/Hash{index}.uasset", paths);
            Assert.DoesNotContain($"TBL/Content/Import{(index + 1) % 8}.uasset", paths);
        }
        Assert.False(resolver.TryLoadPackage("TBL/Content/Outside", out _));
        Assert.All(Enumerable.Range(0, 8), index => Assert.DoesNotContain("TBL/Content/Outside.uasset", provider.GetScanLookups($"owner{index}.pak")));
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
    public async Task MissingDeclaredReferencesRemainFailures() {
        const string assetPath = "TBL/Content/Mods/Referenced.uasset";
        var provider = new Mock<IFileProvider>();
        var scan = new ModScanResult();
        var pak = scan.GetOrAddPak(_reader);
        pak.GenericMarkers["DA_ModMarker_C"] = new() { ["Marker.uasset"] = new GenericMarkerEntry() };
        var task = new SecondPassOrchestrator(provider.Object, GameScanOptions.Chivalry2).ResolveReferencesAsync(
            new ConcurrentBag<PendingBlueprintReference>([new() {
                TargetBlueprintPath = assetPath, SourcePakFile = _reader.Name,
                SourceMarkerPath = "Marker.uasset", SourceMarkerClassName = "DA_ModMarker_C"
            }]), scan, failOnErrors: true);
        await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Empty(pak.GenericMarkers["DA_ModMarker_C"]["Marker.uasset"].Children);
    }

    [Fact]
    public async Task ExistingUnreadableReferencesRemainFailures() {
        var entry = (FPakEntry)RuntimeHelpers.GetUninitializedObject(typeof(FPakEntry));
        var provider = new Mock<IFileProvider>();
        GameFile gameFile = entry;
        provider.Setup(p => p.TryGetGameFile("/Game/Referenced.uasset", out gameFile)).Returns(true);
        provider.Setup(p => p.LoadPackage(entry)).Throws(new InvalidDataException("broken target"));
        var scan = new ModScanResult();
        var pak = scan.GetOrAddPak(_reader);
        pak.GenericMarkers["DA_ModMarker_C"] = new() { ["Marker.uasset"] = new GenericMarkerEntry() };
        await Assert.ThrowsAsync<IOException>(() => new SecondPassOrchestrator(provider.Object, GameScanOptions.Chivalry2)
            .ResolveReferencesAsync(new ConcurrentBag<PendingBlueprintReference>([new() {
                TargetBlueprintPath = "/Game/Referenced.Referenced_C", SourcePakFile = _reader.Name,
                SourceMarkerPath = "Marker.uasset", SourceMarkerClassName = "DA_ModMarker_C"
            }]), scan, failOnErrors: true));
        provider.Verify(p => p.LoadPackage(entry), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReferencesRequireSourceMarker(bool sourcePakPresent) {
        var provider = new Mock<IFileProvider>();
        var scan = new ModScanResult();
        if (sourcePakPresent) scan.GetOrAddPak(_reader);
        await Assert.ThrowsAsync<IOException>(() => new SecondPassOrchestrator(provider.Object, GameScanOptions.Chivalry2)
            .ResolveReferencesAsync(new ConcurrentBag<PendingBlueprintReference>([new() {
                TargetBlueprintPath = "/Game/Referenced.Referenced_C", SourcePakFile = _reader.Name,
                SourceMarkerPath = "Marker.uasset", SourceMarkerClassName = "DA_ModMarker_C"
            }]), scan, failOnErrors: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbsentOrEmptyModActorsRetainMapAndMarkerWithoutGuessingActors(bool emptyMap) {
        const string markerPath = "TBL/Content/Mods/MapOnly/ModMarker.uasset";
        const string mapPath = "TBL/Content/Mods/MapOnly/Arena.umap";
        var provider = new Mock<IFileProvider>();
        var entry = (FPakEntry)RuntimeHelpers.GetUninitializedObject(typeof(FPakEntry));
        SetField(entry, "<Vfs>k__BackingField", _reader);
        var markerExport = new UObject();
        if (emptyMap) {
            var property = (MapProperty)RuntimeHelpers.GetUninitializedObject(typeof(MapProperty));
            SetField(property, "<Value>k__BackingField", new UScriptMap {
                Properties = new Dictionary<FPropertyTagType, FPropertyTagType?>()
            });
            var tag = (FPropertyTag)RuntimeHelpers.GetUninitializedObject(typeof(FPropertyTag));
            tag.Name = new FName("ModActors");
            tag.Tag = property;
            markerExport.Properties.Add(tag);
        }
        var scan = new ModScanResult();
        var pak = scan.GetOrAddPak(_reader);
        var processor = new ReferenceDiscoveryProcessor("DA_ModMarker_C", "ModActors");
        var markerContext = new ScanContext(provider.Object, CreatePackage("DA_ModMarker_C", markerExport), markerPath, entry);
        processor.Process(markerContext, pak);
        new MapProcessor().Process(new ScanContext(provider.Object, CreatePackage("World", new UObject()), mapPath, entry), pak);

        Assert.Empty(markerContext.ProcessingErrors);
        Assert.Empty(processor.DiscoveredReferences);
        await new SecondPassOrchestrator(provider.Object, GameScanOptions.Chivalry2)
            .ResolveReferencesAsync(processor.DiscoveredReferences, scan, failOnErrors: true);
        provider.Verify(p => p.LoadPackage(It.IsAny<GameFile>()), Times.Never);
        Assert.Empty(pak.GenericMarkers["DA_ModMarker_C"][markerPath].Children);
        var inventory = Assert.Single(ModManifestConverter.ProcessModScan(scan).Paks).Inventory;
        Assert.Equal(markerPath, Assert.Single(inventory.Markers).Path);
        Assert.Empty(Assert.Single(inventory.Markers).AssociatedBlueprints);
        Assert.Equal(mapPath, Assert.Single(inventory.Maps).Path);
        Assert.Empty(inventory.Blueprints);
    }

    private static Package CreatePackage(string className, UObject export) {
        var package = (Package)RuntimeHelpers.GetUninitializedObject(typeof(Package));
        package.Name = "/Game/Mods/MapOnly/Asset";
        SetField(package, "<ImportMap>k__BackingField", new[] {
            new FObjectImport { ObjectName = new FName(className), OuterIndex = new FPackageIndex() }
        });
        SetField(package, "<ExportMap>k__BackingField", new[] {
            new FObjectExport {
                ObjectName = new FName("Asset"), ClassName = className, ClassIndex = new FPackageIndex(package, -1),
                OuterIndex = new FPackageIndex(), SuperIndex = new FPackageIndex()
            }
        });
        SetField(package, "<ExportsLazy>k__BackingField", new[] { new Lazy<UObject>(() => export) });
        return package;
    }

    [Fact]
    public void SuppliedHashAvoidsAnotherPakRead() {
        using var exclusive = File.Open(_path, FileMode.Open, FileAccess.Read, FileShare.None);
        var reads = 0;
        var scan = new ModScanResult(path => {
            Assert.Equal(_path, path);
            reads++;
            return "already-hashed";
        });
        Assert.Equal("already-hashed", scan.GetOrAddPak(_reader).PakHash);
        scan.GetOrAddPak(_reader);
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtraPakFilterRunsAfterConfiguredFilter(bool include) {
        var entry = (FPakEntry)RuntimeHelpers.GetUninitializedObject(typeof(FPakEntry));
        SetField(entry, "<Vfs>k__BackingField", _reader);
        var calls = 0;
        using var provider = FilteredFileProvider.CreateFromOptions(GameScanOptions.Chivalry2, ScanMode.Mods, _root,
            _ => { calls++; return include; });
        provider.Files.AddFiles(new Dictionary<string, GameFile> { ["mod.uasset"] = entry, ["mod.txt"] = entry }, 0);
        Assert.Equal(include ? 1 : 0, provider.FilteredFiles.Count());
        Assert.Equal(1, calls);
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
