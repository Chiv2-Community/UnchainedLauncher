using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using Moq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using Xunit;

namespace UnchainedLauncher.PakScannerApp.Tests;

public class BlueprintVersionScannerTests {
    private static void SetField(Package package, string property, object value) {
        for (var type = typeof(Package); type != null; type = type.BaseType) {
            var field = type.GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(package, value);
            return;
        }
        throw new InvalidOperationException($"No backing field for {property}");
    }

    private static UObject Version(string? value, string property = "ModVersion") {
        var result = new UObject();
        if (value != null) {
            var tag = (FPropertyTag)RuntimeHelpers.GetUninitializedObject(typeof(FPropertyTag));
            tag.Name = new FName(property);
            tag.Tag = new StrProperty(value);
            result.Properties.Add(tag);
        }
        return result;
    }

    private static Package Package(params (string Class, string Super, bool Default, Func<UObject> Load)[] exports) {
        var package = (Package)RuntimeHelpers.GetUninitializedObject(typeof(Package));
        package.Name = $"Package{Guid.NewGuid():N}";
        var names = exports.SelectMany(export => new[] { export.Class, export.Super }).Where(name => name != "").Distinct().ToArray();
        SetField(package, nameof(CUE4Parse.UE4.Assets.Package.ImportMap),
            names.Select(name => new FObjectImport { ObjectName = new FName(name), OuterIndex = new FPackageIndex() }).ToArray());
        FPackageIndex Index(string name) => new(package, name == "" ? 0 : -Array.IndexOf(names, name) - 1);
        SetField(package, nameof(CUE4Parse.UE4.Assets.Package.ExportMap),
            exports.Select((export, index) => new FObjectExport {
                ObjectName = new FName(export.Default ? $"Default__Export{index}" : $"Export{index}"),
                ClassIndex = Index(export.Class),
                SuperIndex = Index(export.Super),
                ObjectFlags = export.Default ? (uint)EObjectFlags.RF_ClassDefaultObject : 0
            }).ToArray());
        SetField(package, nameof(CUE4Parse.UE4.Assets.Package.ExportsLazy),
            exports.Select(export => new Lazy<UObject>(export.Load)).ToArray());
        return package;
    }

    [Theory]
    [InlineData("DA_ModMarker_V2_C")]
    [InlineData("DA_ModMarker_V3_C")]
    [InlineData("DA_ModMarker_V12_C")]
    public void MarkersTakePriorityAcrossPackagesWithoutLoadingFallbacks(string markerClass) {
        var fallback = Package(("ArgonSDKModBase_C", "", true, () => throw new Exception("Fallback must not load")));
        var marker = Package((markerClass, "", false, () => Version("2.3.4")));
        Assert.Equal(["2.3.4"], BlueprintVersionScanner.ReadVersions([fallback, marker]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMarkerVersionsFallBackToActors(string? version) {
        var marker = Package(("DA_ModMarker_V2_C", "", false, () => Version(version)));
        var actor = Package(("ArgonSDKModBase_C", "", true, () => Version("1.2.3")));
        Assert.Equal(["1.2.3"], BlueprintVersionScanner.ReadVersions([actor, marker]));
    }

    [Fact]
    public void AnyNonemptyMarkerVersionSuppressesActorFallback() {
        var package = Package(
            ("DA_ModMarker_V2_C", "", false, () => Version(null)),
            ("DA_ModMarker_V3_C", "", false, () => Version("2.0.0")),
            ("ArgonSDKModBase_C", "", true, () => throw new Exception("Fallback must not load")));
        Assert.Equal(["2.0.0"], BlueprintVersionScanner.ReadVersions([package]));
    }

    [Fact]
    public void NoMarkerFallsBackWithoutLoadingUnrelatedExports() {
        var package = Package(
            ("Texture2D", "", false, () => throw new Exception("Texture must not load")),
            ("DA_ModMarker_C", "", false, () => throw new Exception("Legacy marker has no own version")),
            ("DA_ModMarker_V2_C_Extra", "", false, () => throw new Exception("Not a marker class")),
            ("ArgonSDKModBase_C", "", true, () => Version("1.2.3")),
            ("ArgonSDKModBase_C", "", false, () => throw new Exception("Actor instance must not load")));
        Assert.Equal(["1.2.3"], BlueprintVersionScanner.ReadVersions([package]));
    }

    [Fact]
    public void ReadsDefaultExportsWithoutLoadingGeneratedActorClasses() {
        var package = Package(
            ("BlueprintGeneratedClass", "ArgonSDKModBase_C", false,
                () => throw new Exception("Generated actor class must not load")),
            ("CustomMod_C", "", true, () => Version("3.2.1")),
            ("BlueprintGeneratedClass", "Actor", false, () => throw new Exception("Unrelated class must not load")));
        package.ExportMap[1].ClassIndex = new FPackageIndex(package, 1);
        Assert.Equal(["3.2.1"], BlueprintVersionScanner.ReadVersions([package]));
    }

    [Fact]
    public void ActorFallbackUsesDefaultNameRatherThanOnlyObjectFlags() {
        var package = Package(
            ("ArgonSDKModBase_C", "", true, () => Version("1.2.3")),
            ("ArgonSDKModBase_C", "", true, () => throw new Exception("Non-default name must not load")));
        package.ExportMap[0].ObjectFlags = 0;
        package.ExportMap[1].ObjectName = new FName("ActorInstance");
        Assert.Equal(["1.2.3"], BlueprintVersionScanner.ReadVersions([package]));
    }

    [Fact]
    public void ReadsVersionsFromIndirectActorSubclassDefaults() {
        var package = Package(
            ("BlueprintGeneratedClass", "ArgonSDKModBase_C", false,
                () => throw new Exception("Generated base class must not load")),
            ("BlueprintGeneratedClass", "", false,
                () => throw new Exception("Generated subclass must not load")),
            ("SharedModBase_C", "", true, () => Version("1.0.0")),
            ("ConcreteMod_C", "", true, () => Version("2.0.0")));
        package.ExportMap[1].SuperIndex = new FPackageIndex(package, 1);
        package.ExportMap[2].ClassIndex = new FPackageIndex(package, 1);
        package.ExportMap[3].ClassIndex = new FPackageIndex(package, 2);
        Assert.Equal(["1.0.0", "2.0.0"], BlueprintVersionScanner.ReadVersions([package]));
    }

    [Fact]
    public void ReadsSubclassWithParentInAnotherPackageWithoutSdkPackage() {
        var parent = Package(("BlueprintGeneratedClass", "ArgonSDKModBase_C", false,
            () => throw new Exception("Parent class payload must not load")));
        parent.ExportMap[0].ObjectName = new FName("SharedModBase_C");
        var child = Package(
            ("BlueprintGeneratedClass", "SharedModBase_C", false,
                () => throw new Exception("Generated subclass must not load")),
            ("ConcreteMod_C", "", true, () => Version("2.0.0")));
        child.ExportMap[1].ClassIndex = new FPackageIndex(child, 1);
        var imports = child.ImportMap.Append(new FObjectImport {
            ObjectName = new FName("/Game/SharedModBase"),
            OuterIndex = new FPackageIndex()
        }).ToArray();
        imports[1].OuterIndex = new FPackageIndex(child, -imports.Length);
        SetField(child, nameof(CUE4Parse.UE4.Assets.Package.ImportMap), imports);
        var provider = new Mock<IFileProvider>();
        IPackage loaded = parent;
        provider.Setup(value => value.TryLoadPackage("/Game/SharedModBase", out loaded)).Returns(true);
        SetField(child, nameof(CUE4Parse.UE4.Assets.Package.Provider), provider.Object);
        Assert.Equal(["2.0.0"], BlueprintVersionScanner.ReadVersions([child]));
    }

    [Fact]
    public void CrossPackageCyclesTerminateWhenProviderReturnsFreshPackages() {
        var provider = new Mock<IFileProvider>();
        var loads = 0;
        Package Create(string name, string parent) {
            var package = Package(
                ("BlueprintGeneratedClass", parent + "_C", false, () => throw new Exception("Class must not load")),
                (name + "_C", "", true, () => throw new Exception("Unrelated CDO must not load")));
            package.Name = name;
            package.ExportMap[0].ObjectName = new FName(name + "_C");
            package.ExportMap[1].ClassIndex = new FPackageIndex(package, 1);
            var imports = package.ImportMap.Append(new FObjectImport {
                ObjectName = new FName("/Game/" + parent),
                OuterIndex = new FPackageIndex()
            }).ToArray();
            imports[1].OuterIndex = new FPackageIndex(package, -imports.Length);
            SetField(package, nameof(CUE4Parse.UE4.Assets.Package.ImportMap), imports);
            SetField(package, nameof(CUE4Parse.UE4.Assets.Package.Provider), provider.Object);
            return package;
        }
        provider.Setup(value => value.TryLoadPackage(It.IsAny<string>(), out It.Ref<IPackage>.IsAny))
            .Returns((string name, out IPackage loaded) => {
                Assert.True(++loads <= 8, "Cyclic metadata must not reload indefinitely");
                loaded = name == "/Game/A" ? Create("A", "B") : Create("B", "A");
                return true;
            });
        Assert.Empty(BlueprintVersionScanner.ReadVersions([Create("A", "B")]));
    }

    [Fact]
    public void CyclicClassMetadataDoesNotLoopOrLoadExports() {
        var package = Package(
            ("BlueprintGeneratedClass", "", false, () => throw new Exception("Unrelated class must not load")));
        package.ExportMap[0].SuperIndex = new FPackageIndex(package, 1);
        Assert.Empty(BlueprintVersionScanner.ReadVersions([package]));
    }

    [Fact]
    public void IgnoresMarkerClassDefaultsAndUnrelatedVersionProperties() {
        var package = Package(
            ("DA_ModMarker_V2_C", "", true, () => throw new Exception("SDK defaults must not load")),
            ("ArgonSDKModBase_C", "", true, () => Version("99.0.0", "BlueprintSystemVersion")));
        Assert.Empty(BlueprintVersionScanner.ReadVersions([package]));
    }

    [Fact]
    public void ReadsAllMarkerVersionsWithoutLoadingActorFallback() {
        var package = Package(
            ("DA_ModMarker_V2_C", "", false, () => Version("1.0.0")),
            ("DA_ModMarker_V3_C", "", false, () => Version("2.0.0")),
            ("ArgonSDKModBase_C", "", true, () => throw new Exception("Fallback must not load")));
        Assert.Equal(["1.0.0", "2.0.0"], BlueprintVersionScanner.ReadVersions([package]));
    }

    [Fact]
    public void FailedMarkerReadIsNotACompletedEmptyScan() {
        var failure = new IOException("Cannot parse marker");
        var package = Package(("DA_ModMarker_V2_C", "", false, () => throw failure));
        Assert.Same(failure, Assert.Throws<IOException>(() => BlueprintVersionScanner.ReadVersions([package])));
    }
}