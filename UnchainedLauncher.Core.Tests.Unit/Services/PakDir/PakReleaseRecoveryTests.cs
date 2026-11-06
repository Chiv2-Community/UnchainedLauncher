using System.Security.Cryptography;
using System.Text;
using LanguageExt;
using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

public class PakReleaseRecoveryTests : IDisposable {
    private readonly string _dir = Path.Join("TestDirectories", nameof(PakReleaseRecoveryTests), Guid.NewGuid().ToString());

    public PakReleaseRecoveryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static Release Release(string tag) => new(tag,
        Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(tag))), "Mod1.pak", DateTime.UtcNow,
        new ModInfo("https://github.com/Org/Mod1", "Mod1", "", null, null, [], [], []), null!, null);

    private ModManager Manager(Core.Services.Mods.PakDir paks, Mock<IModRegistry> registry, params Release[] releases) =>
        new(registry.Object, paks, [ReleaseCoordinates.FromRelease(releases[0])], [new Mod(releases[0].Info, releases.ToList())]);

    private static Mock<IUserDialogueSpawner> ConfirmFileChanges() {
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(UserDialogueChoice.Yes);
        return dialogue;
    }

    [Fact]
    public async Task RequestedHash_RecoversMissingMetadata_OnlyAfterConfirmation() {
        var wanted = Release("v1.0.0");
        var newer = Release("v2.0.0") with { ReleaseHash = wanted.ReleaseHash };
        File.WriteAllText(Path.Join(_dir, "q__-__Mod1.pak"), wanted.Tag);
        File.WriteAllText(Path.Join(_dir, "q__-__Mod1 (9).pak"), wanted.Tag);
        File.WriteAllText(Path.Join(_dir, "q__-__Mod1.sig"), "matching signature");
        File.WriteAllText(Path.Join(_dir, "q__-__Mod1 (9).sig"), "matching signature");
        var unrelated = Path.Join(_dir, "q__-__Other.pak");
        File.WriteAllText(unrelated, wanted.Tag);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var dialogue = ConfirmFileChanges();
        for (var attempt = 0; attempt < 2; attempt++) {
            var paks = new Core.Services.Mods.PakDir(_dir, []);
            var manager = Manager(paks, registry, wanted, newer);
            using var exclusive = new FileStream(unrelated, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
            var results = await manager.InstallMods([wanted], Option<AccumulatedMemoryProgress>.None).ToListAsync();
            results.ForEach(result => result.IfLeft(error => throw error));
            var installed = Assert.Single(paks.ManagedPaks);
            Assert.Equal(ReleaseCoordinates.FromRelease(wanted), installed.Coordinates);
            Assert.Equal("q__-__Mod1.pak", installed.PakFileName);
            Assert.Equal(2, Directory.GetFiles(_dir, "*.pak").Length);
            Assert.Equal("matching signature", File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "*.sig"))));
        }
        Assert.Equal(wanted.Tag, File.ReadAllText(unrelated));
        registry.VerifyNoOtherCalls();
        dialogue.Verify(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("q__-__Mod1.pak")]
    [InlineData("q__-__Mod1 (9).pak")]
    public async Task RequestedHash_TransfersDisabledModOwnership_BeforeInstallation(string name) {
        var wanted = Release("v1.0.0");
        var old = wanted with { Info = wanted.Info with { RepoUrl = "https://github.com/OldOrg/Mod1" } };
        var path = Path.Join(_dir, name);
        File.WriteAllText(path, wanted.Tag);
        File.WriteAllText(Path.ChangeExtension(path, ".sig"), "verified signature");
        var paks = new Core.Services.Mods.PakDir(_dir, [new ManagedPak(ReleaseCoordinates.FromRelease(old), name, 0)]);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var dialogue = ConfirmFileChanges();
        var manager = new ModManager(registry.Object, paks, [ReleaseCoordinates.FromRelease(wanted)],
            [new Mod(wanted.Info, [wanted]), new Mod(old.Info, [old])]);

        Assert.True(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        Assert.Equal(ReleaseCoordinates.FromRelease(wanted), Assert.Single(paks.ManagedPaks).Coordinates);
        var results = await manager.InstallMods([wanted], Option<AccumulatedMemoryProgress>.None).ToListAsync();

        Assert.True(Assert.Single(results).IsRight);
        Assert.Equal(wanted.Tag, File.ReadAllText(Path.Join(_dir, "q__-__Mod1.pak")));
        Assert.Equal("verified signature", File.ReadAllText(Path.Join(_dir, "q__-__Mod1.sig")));
        Assert.Equal("q__-__Mod1.pak", Assert.Single(paks.ManagedPaks).PakFileName);
        Assert.Single(Directory.GetFiles(_dir, "*.pak"));
        registry.VerifyNoOtherCalls();
        dialogue.VerifyAll();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Relocation_RejectsConflictingSignatures_AndCoalescesIdenticalOnes(bool recoverFirst, bool identical) {
        var wanted = Release("v1.0.0");
        var source = Path.Join(_dir, "q__-__Mod1 (9).pak");
        var destination = Path.Join(_dir, "q__-__Mod1.pak");
        var destinationSignature = identical ? "verified signature" : "unrelated signature";
        File.WriteAllText(source, wanted.Tag);
        File.WriteAllText(Path.ChangeExtension(source, ".sig"), "verified signature");
        File.WriteAllText(Path.ChangeExtension(destination, ".sig"), destinationSignature);
        var tracked = new ManagedPak(ReleaseCoordinates.FromRelease(wanted), Path.GetFileName(source), 7);
        var paks = new Core.Services.Mods.PakDir(_dir, [tracked]);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        if (recoverFirst && !identical) dialogue.Setup(d => d.DisplayMessage(It.IsAny<string>()));
        var manager = Manager(paks, registry, wanted);

        if (recoverFirst)
            Assert.Equal(identical, await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        if (!recoverFirst || identical) {
            var results = await manager.InstallMods([wanted], Option<AccumulatedMemoryProgress>.None).ToListAsync();
            Assert.Equal(identical, Assert.Single(results).IsRight);
        }
        Assert.Equal(wanted.Tag, File.ReadAllText(identical ? destination : source));
        Assert.Equal(!identical, File.Exists(source));
        Assert.Equal(identical, File.Exists(destination));
        Assert.Equal(!identical, File.Exists(Path.ChangeExtension(source, ".sig")));
        Assert.Equal(destinationSignature, File.ReadAllText(Path.ChangeExtension(destination, ".sig")));
        if (!identical) {
            Assert.Equal("verified signature", File.ReadAllText(Path.ChangeExtension(source, ".sig")));
            Assert.Equal(tracked, Assert.Single(paks.ManagedPaks));
        }
        else Assert.Equal("q__-__Mod1.pak", Assert.Single(paks.ManagedPaks).PakFileName);
        Assert.Single(Directory.GetFiles(_dir, "*.pak"));
        registry.VerifyNoOtherCalls();
        dialogue.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedHash_IdentifiesLatestMatchingVersion_RegardlessOfRegistryOrder(bool newestFirst) {
        var wanted = Release("v9.0.0");
        var older = Release("v1.0.0");
        var latest = Release("v10.0.0") with { ReleaseHash = older.ReleaseHash, ReleaseDate = older.ReleaseDate.AddDays(-1) };
        var path = Path.Join(_dir, "q__-__Mod1.pak");
        File.WriteAllText(path, older.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), newestFirst ? [wanted, latest, older] : [wanted, older, latest]);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, updates) => {
                var update = Assert.Single(updates);
                Assert.Equal(latest.Tag, update.CurrentVersion);
                Assert.Equal("Downgrade", update.Reason);
            }).Returns(UserDialogueChoice.No);

        Assert.False(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        Assert.Equal(older.Tag, File.ReadAllText(path));
        dialogue.VerifyAll();
    }

    [Theory]
    [InlineData("q__-__Mod1.pak")]
    [InlineData("custom-tracked.pak")]
    public async Task UnknownDestinationHash_DecliningPreservesFileAndMetadata(string name) {
        var wanted = Release("v1.0.0");
        var path = Path.Join(_dir, name);
        File.WriteAllText(path, "unknown file");
        var tracked = new ManagedPak(ReleaseCoordinates.FromRelease(wanted), Path.GetFileName(path), 0);
        var paks = new Core.Services.Mods.PakDir(_dir, [tracked]);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), wanted);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(UserDialogueChoice.No);

        Assert.False(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        Assert.Equal("unknown file", File.ReadAllText(path));
        Assert.Equal(tracked, Assert.Single(paks.ManagedPaks));
    }

    [Theory]
    [InlineData("v1.0.0", "v2.0.0", "Upgrade")]
    [InlineData("v2.0.0", "v1.0.0", "Downgrade")]
    [InlineData("corrupt tracked content", "v2.0.0", null)]
    public async Task ApprovedReplacement_InstallsRequestedReleaseWithoutNumberedCopy(string currentContent, string targetTag, string? reason) {
        var wanted = Release(targetTag);
        var path = Path.Join(_dir, "q__-__Mod1.pak");
        File.WriteAllText(path, currentContent);
        var coordinates = ReleaseCoordinates.FromRelease(wanted);
        var installedCoordinates = reason != null ? ReleaseCoordinates.FromRelease(Release(currentContent)) : coordinates;
        var paks = new Core.Services.Mods.PakDir(_dir, [new ManagedPak(installedCoordinates, Path.GetFileName(path), 0)]);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.DownloadPak(coordinates, It.IsAny<string>()))
            .Returns((ReleaseCoordinates _, string output) => {
                Assert.EndsWith(".partial", output);
                var bytes = Encoding.UTF8.GetBytes(wanted.Tag);
                return Prelude.RightAsync<ModPakStreamAcquisitionFailure, FileWriter>(new FileWriter(output, new MemoryStream(bytes), bytes.Length));
            });
        var manager = Manager(paks, registry, reason != null ? [wanted, Release(currentContent)] : [wanted]);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, updates) => {
                var update = Assert.Single(updates);
                Assert.Equal(reason != null ? currentContent : null, update.CurrentVersion);
                Assert.Equal(targetTag, update.LatestVersion);
                if (reason != null) Assert.Equal(reason, update.Reason);
                else Assert.Contains("Unrecognized file hash", update.Reason);
                Assert.Equal(currentContent, File.ReadAllText(path));
                Assert.Equal(installedCoordinates, Assert.Single(paks.ManagedPaks).Coordinates);
            }).Returns(UserDialogueChoice.Yes);

        Assert.True(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        var results = await manager.InstallMods([wanted], Option<AccumulatedMemoryProgress>.None).ToListAsync();

        results.ForEach(result => result.IfLeft(error => throw error));
        Assert.Equal(wanted.Tag, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir, "*.pak"));
        Assert.Empty(Directory.GetFiles(_dir, "*.partial"));
        registry.Verify(r => r.DownloadPak(coordinates, It.IsAny<string>()), Times.Once);
        dialogue.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictingDuplicateSignature_PreservesFilesAndMetadata(bool referenceSignatureExists) {
        var wanted = Release("v1.0.0");
        var canonical = Path.Join(_dir, "q__-__Mod1.pak");
        var duplicate = Path.Join(_dir, "q__-__Mod1 (9).pak");
        File.WriteAllText(canonical, wanted.Tag);
        File.WriteAllText(duplicate, wanted.Tag);
        if (referenceSignatureExists) File.WriteAllText(Path.ChangeExtension(canonical, ".sig"), "reference signature");
        File.WriteAllText(Path.ChangeExtension(duplicate, ".sig"), "conflicting signature");
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), wanted);
        var dialogue = new Mock<IUserDialogueSpawner>();

        Assert.False(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        Assert.Empty(paks.ManagedPaks);
        Assert.Equal(wanted.Tag, File.ReadAllText(canonical));
        Assert.Equal(wanted.Tag, File.ReadAllText(duplicate));
        Assert.Equal("conflicting signature", File.ReadAllText(Path.ChangeExtension(duplicate, ".sig")));
        if (referenceSignatureExists) Assert.Equal("reference signature", File.ReadAllText(Path.ChangeExtension(canonical, ".sig")));
    }

    [Fact]
    public async Task IdenticalPayloadsForDifferentRequestedMods_KeepSeparateFiles() {
        var first = Release("v1.0.0");
        var second = first with { Info = first.Info with { RepoUrl = "https://github.com/Org/Mod2", Name = "Mod2" } };
        File.WriteAllText(Path.Join(_dir, "qz__-__Mod1.pak"), first.Tag);
        File.WriteAllText(Path.Join(_dir, "qy__-__Mod2.pak"), second.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), first, second);

        Assert.True(await new PakReleaseRecovery(manager, ConfirmFileChanges().Object).Recover([first, second]));

        Assert.Equal(2, paks.ManagedPaks.Select(pak => pak.PakFileName).Distinct().Count());
        Assert.Equal(2, Directory.GetFiles(_dir, "*.pak").Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterCandidateChangedWhileConfirming_PreservesEarlierReplacementAndMetadata(bool retained) {
        var wanted = Release("v2.0.0");
        var current = Release("v1.0.0");
        var second = wanted with { Info = wanted.Info with { RepoUrl = "https://github.com/Org/Mod2", Name = "Mod2" } };
        var firstPath = Path.Join(_dir, "qz__-__Mod1.pak");
        var secondPath = Path.Join(_dir, "qy__-__Mod2.pak");
        File.WriteAllText(firstPath, current.Tag);
        File.WriteAllText(secondPath, retained ? wanted.Tag : current.Tag);
        File.WriteAllText(Path.ChangeExtension(firstPath, ".sig"), "first signature");
        File.WriteAllText(Path.ChangeExtension(secondPath, ".sig"), "second signature");
        var tracked = new ManagedPak(ReleaseCoordinates.FromRelease(current), Path.GetFileName(firstPath), 7);
        var paks = new Core.Services.Mods.PakDir(_dir, [tracked]);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var manager = new ModManager(registry.Object, paks,
            [ReleaseCoordinates.FromRelease(wanted), ReleaseCoordinates.FromRelease(second)],
            [new Mod(wanted.Info, [wanted, current]), new Mod(second.Info, [second])]);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback(() => File.WriteAllText(secondPath, "changed during confirmation")).Returns(UserDialogueChoice.Yes);
        dialogue.Setup(d => d.DisplayMessage(It.Is<string>(message => message.Contains("Pak changed during release confirmation"))));

        Assert.False(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted, second]));
        Assert.Equal(current.Tag, File.ReadAllText(firstPath));
        Assert.Equal("changed during confirmation", File.ReadAllText(secondPath));
        Assert.Equal("first signature", File.ReadAllText(Path.ChangeExtension(firstPath, ".sig")));
        Assert.Equal("second signature", File.ReadAllText(Path.ChangeExtension(secondPath, ".sig")));
        Assert.Equal(tracked, Assert.Single(paks.ManagedPaks));
        registry.VerifyNoOtherCalls();
        dialogue.VerifyAll();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LaunchInstallation_ReusesRecoveryDependencyOrderAndNames_WithoutDownload(bool sameModuleName, bool reorder) {
        var dependent = Release("v2.0.0");
        var dependency = Release("v1.0.0") with {
            Info = dependent.Info with {
                RepoUrl = sameModuleName ? "https://github.com/OtherOrg/Mod1" : "https://github.com/Org/Mod2",
                Name = sameModuleName ? "Mod1" : "Mod2"
            }
        };
        dependent = dependent with { Info = dependent.Info with { Dependencies = [new Dependency(dependency.Info.RepoUrl, "*")] } };
        var releases = new[] { dependency, dependent };
        var stems = sameModuleName ? new[] { "OtherOrg_Mod1", "Org_Mod1" } : new[] { "Mod2", "Mod1" };
        var names = new[] { $"qz__-__{stems[0]}.pak", $"qy__-__{stems[1]}.pak" };
        var sourceNames = reorder ? new[] { $"qy__-__{stems[0]}.pak", $"qz__-__{stems[1]}.pak" } : names;
        for (var i = 0; i < releases.Length; i++) {
            File.WriteAllText(Path.Join(_dir, sourceNames[i]), releases[i].Tag);
            File.WriteAllText(Path.Join(_dir, Path.ChangeExtension(sourceNames[i], ".sig")), releases[i].Tag + " signature");
        }
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var manager = new ModManager(registry.Object, paks, [ReleaseCoordinates.FromRelease(dependent)],
            [new Mod(dependent.Info, [dependent]), new Mod(dependency.Info, [dependency])]);
        var dialogue = ConfirmFileChanges();
        ManagedPak[] recovered = [];
        var progressClosed = false;
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>()))
            .Callback(() => {
                recovered = paks.ManagedPaks.OrderBy(pak => pak.Priority).ToArray();
                Assert.Equal(releases.Select(ReleaseCoordinates.FromRelease), recovered.Select(pak => pak.Coordinates));
                Assert.Equal(new[] { 0, 1 }, recovered.Select(pak => pak.Priority));
                Assert.Equal(sourceNames, recovered.Select(pak => pak.PakFileName));
            }).Returns(() => progressClosed = true);
        var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);

        Assert.Equal(Prelude.Some(options), await new Chivalry2ModsInstaller(manager, dialogue.Object).PrepareLaunch(options));

        var installed = paks.ManagedPaks.OrderBy(pak => pak.Priority).ToArray();
        Assert.Equal(recovered.Select(pak => (pak.Coordinates, pak.Priority)), installed.Select(pak => (pak.Coordinates, pak.Priority)));
        Assert.Equal(names, installed.Select(pak => pak.PakFileName));
        for (var i = 0; i < releases.Length; i++) {
            Assert.Equal(releases[i].Tag, File.ReadAllText(Path.Join(_dir, names[i])));
            Assert.Equal(releases[i].Tag + " signature", File.ReadAllText(Path.Join(_dir, Path.ChangeExtension(names[i], ".sig"))));
        }
        Assert.Equal(2, Directory.GetFiles(_dir, "*.pak").Length);
        Assert.Equal(2, Directory.GetFiles(_dir, "*.sig").Length);
        Assert.True(progressClosed);
        registry.VerifyNoOtherCalls();
        dialogue.VerifyAll();
    }

    [Fact]
    public async Task ValidManagedAlternateName_PreservesUnmanagedDestinationWithoutPrompt() {
        var wanted = Release("v1.0.0");
        var path = Path.Join(_dir, "q__-__Mod1 (9).pak");
        var unmanaged = Path.Join(_dir, "q__-__Mod1.pak");
        File.WriteAllText(path, wanted.Tag);
        File.WriteAllText(Path.ChangeExtension(path, ".sig"), "signature");
        File.WriteAllText(unmanaged, "user pak");
        File.WriteAllText(Path.ChangeExtension(unmanaged, ".sig"), "user signature");
        var paks = new Core.Services.Mods.PakDir(_dir, [new ManagedPak(ReleaseCoordinates.FromRelease(wanted), Path.GetFileName(path), 0)]);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        var manager = Manager(paks, registry, wanted);

        for (var attempt = 0; attempt < 2; attempt++) {
            Assert.True(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
            var results = await manager.InstallMods([wanted], Option<AccumulatedMemoryProgress>.None).ToListAsync();
            Assert.True(Assert.Single(results).IsRight);
        }

        Assert.Equal(wanted.Tag, File.ReadAllText(path));
        Assert.Equal("user pak", File.ReadAllText(unmanaged));
        Assert.Equal("user signature", File.ReadAllText(Path.ChangeExtension(unmanaged, ".sig")));
        Assert.Equal(Path.GetFileName(path), Assert.Single(paks.ManagedPaks).PakFileName);
        registry.VerifyNoOtherCalls();
        dialogue.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, UserDialogueChoice.No)]
    [InlineData(false, UserDialogueChoice.Cancel)]
    [InlineData(true, UserDialogueChoice.No)]
    [InlineData(true, UserDialogueChoice.Cancel)]
    public async Task DeclinedAdoptionOrDuplicateRemoval_PreservesUnmanagedFiles(bool duplicate, UserDialogueChoice choice) {
        var wanted = Release("v1.0.0");
        var path = Path.Join(_dir, "q__-__Mod1.pak");
        var duplicatePath = Path.Join(_dir, "q__-__Mod1 (9).pak");
        File.WriteAllText(path, wanted.Tag);
        if (duplicate) File.WriteAllText(duplicatePath, wanted.Tag);
        File.WriteAllText(Path.ChangeExtension(path, ".sig"), "user signature");
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), wanted);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, changes) => {
                Assert.Contains(changes, change => change.Name == Path.GetFileName(path) && change.Reason!.Contains("Adopt"));
                Assert.Equal(duplicate ? 2 : 1, changes.Count());
            }).Returns(choice);

        Assert.False(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));

        Assert.Empty(paks.ManagedPaks);
        Assert.Equal(wanted.Tag, File.ReadAllText(path));
        Assert.Equal("user signature", File.ReadAllText(Path.ChangeExtension(path, ".sig")));
        Assert.Equal(duplicate, File.Exists(duplicatePath));
        dialogue.VerifyAll();
    }

    [Theory]
    [InlineData("v1.0.0", "v2.0.0", UserDialogueChoice.No, "Upgrade")]
    [InlineData("v2.0.0", "v1.0.0", UserDialogueChoice.Cancel, "Downgrade")]
    public async Task DeclinedVersionChange_CancelsLaunchBeforeInstallation(string currentTag, string wantedTag, UserDialogueChoice choice, string reason) {
        var wanted = Release(wantedTag);
        var current = Release(currentTag);
        File.WriteAllText(Path.Join(_dir, "q__-__Mod1.pak"), current.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), wanted, current);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, updates) => {
                var update = Assert.Single(updates);
                Assert.Equal(currentTag, update.CurrentVersion);
                Assert.Equal(wantedTag, update.LatestVersion);
                Assert.Equal(reason, update.Reason);
            }).Returns(choice);
        var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);
        var signaturesPrepared = false;
        var pipeline = Chivalry2LaunchPreparer.Create<LaunchOptions>(value => Prelude.Some(value))
            .AndThen(new Chivalry2ModsInstaller(manager, dialogue.Object))
            .Sub(Chivalry2LaunchPreparer.Create<LanguageExt.Unit>(value => {
                signaturesPrepared = true;
                return Prelude.Some(value);
            }));

        Assert.True((await pipeline.PrepareLaunch(options)).IsNone);
        Assert.False(signaturesPrepared);
        Assert.Empty(paks.ManagedPaks);
        Assert.Equal(current.Tag, File.ReadAllText(Path.Join(_dir, "q__-__Mod1.pak")));
        dialogue.Verify(d => d.DisplayProgress(It.IsAny<MemoryProgress>()), Times.Never);
        dialogue.VerifyAll();
    }

    [Fact]
    public async Task LaunchPreparerChain_PassesModifiedOptionsToNextStage() {
        var received = 0;
        var pipeline = Chivalry2LaunchPreparer.Create<int>(value => Prelude.Some(value + 1))
            .AndThen(Chivalry2LaunchPreparer.Create<int>(value => {
                received = value;
                return Prelude.Some(value * 2);
            }));

        Assert.Equal(Prelude.Some(6), await pipeline.PrepareLaunch(2));
        Assert.Equal(3, received);
    }
}