using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyAbsentInferredReferencesAreOptional(bool inferred) {
        const string assetPath = "TBL/Content/Mods/Referenced.uasset";
        var provider = new Mock<IFileProvider>();
        var scan = new ModScanResult();
        var pak = scan.GetOrAddPak(_reader);
        pak.GenericMarkers["DA_ModMarker_C"] = new() { ["Marker.uasset"] = new GenericMarkerEntry() };
        var task = new SecondPassOrchestrator(provider.Object, GameScanOptions.Chivalry2).ResolveReferencesAsync(
            new ConcurrentBag<PendingBlueprintReference>([new() {
                TargetBlueprintPath = assetPath, SourcePakFile = _reader.Name,
                SourceMarkerPath = "Marker.uasset", SourceMarkerClassName = "DA_ModMarker_C", IsInferred = inferred
            }]), scan, failOnErrors: true);
        if (inferred) await task;
        else await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Empty(pak.GenericMarkers["DA_ModMarker_C"]["Marker.uasset"].Children);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingUnreadableReferencesRemainFailures(bool inferred) {
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
                SourceMarkerPath = "Marker.uasset", SourceMarkerClassName = "DA_ModMarker_C", IsInferred = inferred
            }]), scan, failOnErrors: true));
        provider.Verify(p => p.LoadPackage(entry), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InferredReferenceDoesNotHideMissingSourceMarker(bool sourcePakPresent) {
        var provider = new Mock<IFileProvider>();
        var scan = new ModScanResult();
        if (sourcePakPresent) scan.GetOrAddPak(_reader);
        await Assert.ThrowsAsync<IOException>(() => new SecondPassOrchestrator(provider.Object, GameScanOptions.Chivalry2)
            .ResolveReferencesAsync(new ConcurrentBag<PendingBlueprintReference>([new() {
                TargetBlueprintPath = "/Game/Referenced.Referenced_C", SourcePakFile = _reader.Name,
                SourceMarkerPath = "Marker.uasset", SourceMarkerClassName = "DA_ModMarker_C", IsInferred = true
            }]), scan, failOnErrors: true));
    }

    [Fact]
    public async Task InferredReferenceDoesNotHideDeclaredReferenceInSameGroup() {
        var provider = new Mock<IFileProvider>();
        var scan = new ModScanResult();
        var pak = scan.GetOrAddPak(_reader);
        pak.GenericMarkers["DA_ModMarker_C"] = new() { ["Marker.uasset"] = new GenericMarkerEntry() };
        var references = new[] { true, false }.Select(inferred => new PendingBlueprintReference {
            TargetBlueprintPath = "/Game/Referenced.Referenced_C",
            SourcePakFile = _reader.Name,
            SourceMarkerPath = "Marker.uasset",
            SourceMarkerClassName = "DA_ModMarker_C",
            IsInferred = inferred
        });
        await Assert.ThrowsAsync<IOException>(() => new SecondPassOrchestrator(provider.Object, GameScanOptions.Chivalry2)
            .ResolveReferencesAsync(new ConcurrentBag<PendingBlueprintReference>(references), scan, failOnErrors: true));
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