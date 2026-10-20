using LanguageExt.Common;
using System.Text;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using Xunit;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.CLI.Tests;

public sealed class NumericPakOrderingTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "numeric-paks-" + Guid.NewGuid().ToString("N"));
    private int _downloads;

    public NumericPakOrderingTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ModInstallRequest Request(string module, string org = "org") =>
        new(new ReleaseCoordinates(org, module, "v1.0.0"), path => {
            _downloads++;
            var bytes = Encoding.UTF8.GetBytes(org + "/" + module);
            return RightAsync<Error, FileWriter>(new FileWriter(path, new MemoryStream(bytes), bytes.Length));
        });

    private static async Task Install(PakDir directory, params ModInstallRequest[] requests) {
        await foreach (var result in directory.InstallModSet(requests, None))
            Assert.True(result.IsRight, result.ToString());
    }

    [Fact]
    public async Task NumericOrdersFollowInputRatherThanAlphabeticalNamesPastTenMods() {
        var directory = new PakDir(_root, []);
        var requests = Enumerable.Range(0, 12).Select(i => Request($"Mod{11 - i}")).ToArray();
        await Install(directory, requests);
        var installed = directory.ManagedPaks.OrderBy(pak => pak.Priority).ToArray();
        Assert.Equal(12, installed.Length);
        for (var order = 0; order < installed.Length; order++) {
            Assert.Equal(requests[order].Coordinates, installed[order].Coordinates);
            Assert.Equal($"Mod{11 - order}_{order}_P.pak", installed[order].PakFileName);
        }
        await Install(directory, requests);
        Assert.Equal(installed, directory.ManagedPaks.OrderBy(pak => pak.Priority));
        Assert.Equal(12, _downloads);
    }

    [Fact]
    public async Task ReorderingPreservesPayloadsAndSignaturesAndKeepsCollisionCountersBeforeOrder() {
        var directory = new PakDir(_root, []);
        var first = Request("First");
        var second = Request("Second");
        await Install(directory, first, second);
        File.WriteAllText(Path.Combine(_root, "First_0_P.sig"), "first signature");
        var occupied = Path.Combine(_root, "First_1_P.pak");
        File.WriteAllText(occupied, "unmanaged");

        await Install(directory, second, first);
        var installed = directory.ManagedPaks.OrderBy(pak => pak.Priority).ToArray();
        Assert.Equal(["Second_0_P.pak", "First (1)_1_P.pak"], installed.Select(pak => pak.PakFileName));
        Assert.Equal("org/First", File.ReadAllText(Path.Combine(_root, installed[1].PakFileName)));
        Assert.Equal("first signature", File.ReadAllText(Path.Combine(_root, "First (1)_1_P.sig")));
        Assert.False(File.Exists(Path.Combine(_root, "First_0_P.pak")));
        Assert.Equal("unmanaged", File.ReadAllText(occupied));
        await Install(directory, second, first);
        Assert.Equal(installed, directory.ManagedPaks.OrderBy(pak => pak.Priority));
        Assert.Equal(2, _downloads);

        File.Delete(occupied);
        await Install(directory, second, first);
        Assert.Contains(directory.ManagedPaks, pak => pak.PakFileName == "First_1_P.pak");
        Assert.False(File.Exists(Path.Combine(_root, "First (1)_1_P.pak")));
        Assert.Equal("first signature", File.ReadAllText(Path.Combine(_root, "First_1_P.sig")));
        Assert.Equal(2, _downloads);
    }

    [Theory]
    [InlineData("q__-__Legacy_0_P.pak", false)]
    [InlineData("q__-__Legacy.pak", false)]
    [InlineData("q__-__Legacy_0_P.pak", true)]
    [InlineData("q__-__Legacy.pak", true)]
    public async Task LegacyManagedNamesMigrateWithoutDownloading(string legacyName, bool collision) {
        var request = Request("Legacy");
        File.WriteAllText(Path.Combine(_root, legacyName), "legacy payload");
        File.WriteAllText(Path.ChangeExtension(Path.Combine(_root, legacyName), ".sig"), "legacy signature");
        if (collision) File.WriteAllText(Path.Combine(_root, "Legacy_0_P.pak"), "unmanaged");
        var directory = new PakDir(_root, [new ManagedPak(request.Coordinates, legacyName, 0)]);
        await Install(directory, request);
        var expected = collision ? "Legacy (1)_0_P.pak" : "Legacy_0_P.pak";
        Assert.Equal(expected, Assert.Single(directory.ManagedPaks).PakFileName);
        Assert.Equal("legacy payload", File.ReadAllText(Path.Combine(_root, expected)));
        Assert.Equal("legacy signature", File.ReadAllText(Path.ChangeExtension(Path.Combine(_root, expected), ".sig")));
        if (collision) Assert.Equal("unmanaged", File.ReadAllText(Path.Combine(_root, "Legacy_0_P.pak")));
        Assert.False(File.Exists(Path.Combine(_root, legacyName)));
        Assert.Equal(0, _downloads);
    }

    [Fact]
    public async Task DuplicateModuleNamesRetainOrganizationDisambiguation() {
        var directory = new PakDir(_root, []);
        await Install(directory, Request("Common", "one"), Request("Common", "two"));
        Assert.Equal(["one_Common_0_P.pak", "two_Common_1_P.pak"],
            directory.ManagedPaks.OrderBy(pak => pak.Priority).Select(pak => pak.PakFileName));
    }
}