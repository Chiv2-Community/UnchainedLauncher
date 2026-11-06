using LanguageExt;
using Moq;
using System.Security.Cryptography;
using System.Text;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

[Collection("Pak installation files")]
public class PakReleaseRecoveryTests : IDisposable {
    private readonly string _dir = Path.Join("TestDirectories", nameof(PakReleaseRecoveryTests), Guid.NewGuid().ToString());
    public PakReleaseRecoveryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static Release Release(string tag) => new(tag,
        Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(tag))), "Mod1.pak", DateTime.UtcNow,
        new ModInfo("https://github.com/Org/Mod1", "Mod1", "", null, null, [], [], []), null!, null);

    private string Write(string name, string content) {
        var path = Path.Join(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static Mock<IUserDialogueSpawner> Dialogue(UserDialogueChoice choice) {
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(choice);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(choice);
        dialogue.Setup(d => d.DisplayMessage(It.IsAny<string>()));
        return dialogue;
    }

    [Fact]
    public async Task ConfirmationAndManagedRecoveryReuseHashWithoutReopeningPak() {
        var wanted = Release("v1.0.0");
        var path = Write("q__-__Mod1.pak", wanted.Tag);
        FileStream? exclusive = null;
        var dialogue = Dialogue(UserDialogueChoice.Yes);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback(() => exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            .Returns(UserDialogueChoice.Yes);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        try {
            Assert.True(await paks.RecoverReleases([wanted], [wanted], dialogue.Object));
            Assert.NotNull(exclusive);
            Assert.True(await paks.RecoverReleases([wanted], [wanted], new Mock<IUserDialogueSpawner>(MockBehavior.Strict).Object));
            Assert.Equal(ReleaseCoordinates.FromRelease(wanted), Assert.Single(paks.ManagedPaks).Coordinates);
        }
        finally {
            exclusive?.Dispose();
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("https://example.test/release", true)]
    public void ReleaseInformationIsAvailableOnlyForKnownReleases(string url, bool available) {
        var row = new UnchainedLauncher.GUI.ViewModels.DependencyUpdateViewModel(
            new DependencyUpdate("mod.pak", null, "Unknown", url, "Unrecognized pak"));
        Assert.Equal(available, row.HasReleaseInformation);
        Assert.Equal(available, row.HyperlinkCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("1.9.0|1.10.0|invalid", "1.10.0")]
    [InlineData("2.0.0-beta.2|2.0.0-beta.10|1.0.0", "2.0.0-beta.10")]
    [InlineData("2.0.0-beta.10|2.0.0|9.0", "2.0.0")]
    [InlineData("1.0.0+build.7|1.0.0-rc.1", "1.0.0+build.7")]
    [InlineData("v0.0.0|v0.0.1|0.0.1-alpha", "0.0.1")]
    [InlineData("unknown|1.2|01.2.3|1.2.3 notes", null)]
    [InlineData("", null)]
    public void BlueprintVersionUsesHighestStrictSemverPrecedence(string fields, string? expected) =>
        Assert.Equal(expected, PakBlueprintVersion.Latest(fields.Split('|')));

    [Theory]
    [InlineData(UserDialogueChoice.Yes, false)]
    [InlineData(UserDialogueChoice.Yes, true)]
    [InlineData(UserDialogueChoice.No, false)]
    [InlineData(UserDialogueChoice.Cancel, false)]
    public async Task KeepRemoveAndCancelApplyToUnmanagedPaksWithoutListingDuplicates(UserDialogueChoice choice, bool alwaysKeep) {
        var wanted = Release("v1.0.0");
        var path = Write("q__-__Mod1.pak", wanted.Tag);
        var duplicate = Write("q__-__Mod1 (9).pak", wanted.Tag);
        Write("q__-__Mod1.sig", "signature");
        Write("q__-__Mod1 (9).sig", "signature");
        var unrelated = Write("manual.pak", "unknown content");
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var sibling = new Core.Services.Mods.PakDir(_dir, []);
        var dialogue = Dialogue(choice);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), "Unmanaged paks detected. Keep them or remove them?", "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, message, _, _, _, changes) => {
                Assert.Equal(2, changes.Count());
                Assert.DoesNotContain("Directory:", message);
                Assert.Contains(changes, change => change.Name == Path.GetFileName(path) && change.Reason == wanted.Info.Name);
                Assert.Contains(changes, change => change.Name == Path.GetFileName(unrelated) && change.LatestVersion == "Unknown");
                Assert.DoesNotContain(changes, change => change.Name == Path.GetFileName(duplicate));
                Assert.Empty(paks.ManagedPaks);
                Assert.True(File.Exists(duplicate));
            }).Returns(choice);

        Assert.Equal(choice != UserDialogueChoice.Cancel, await paks.RecoverReleases([wanted], [wanted], dialogue.Object, alwaysKeepUnmanagedPaks: alwaysKeep));

        Assert.Equal(choice != UserDialogueChoice.No, File.Exists(path));
        Assert.Equal(choice != UserDialogueChoice.No, File.Exists(unrelated));
        if (choice == UserDialogueChoice.Yes) {
            Assert.False(File.Exists(duplicate));
            Assert.False(File.Exists(Path.ChangeExtension(duplicate, ".sig")));
            Assert.Equal(ReleaseCoordinates.FromRelease(wanted), Assert.Single(await sibling.GetManagedPaks()).Coordinates);
            Assert.True(await sibling.RecoverReleases([wanted], [wanted], new Mock<IUserDialogueSpawner>(MockBehavior.Strict).Object));
        }
        else if (choice == UserDialogueChoice.Cancel) {
            Assert.True(File.Exists(duplicate));
            Assert.Equal("signature", File.ReadAllText(Path.ChangeExtension(duplicate, ".sig")));
            Assert.Empty(await sibling.GetManagedPaks());
        }
        else {
            Assert.False(File.Exists(duplicate));
            Assert.Empty(await sibling.GetManagedPaks());
        }
        dialogue.Verify(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()), alwaysKeep ? Times.Never : Times.Once);
    }

    [Theory]
    [InlineData("v1.0.0", "v2.0.0", UserDialogueChoice.Yes)]
    [InlineData("v2.0.0", "v1.0.0", UserDialogueChoice.No)]
    [InlineData("unknown", "v2.0.0", UserDialogueChoice.Cancel)]
    public async Task ReplacementConfirmationControlsLaunchAndPublishesOnlyCompletedPaks(string content, string tag, UserDialogueChoice choice) {
        var wanted = Release(tag);
        var current = Release(content);
        var path = Write(content == "unknown" ? "custom-tracked.pak" : "q__-__Mod1.pak", content);
        var paks = new Core.Services.Mods.PakDir(_dir,
            [new ManagedPak(ReleaseCoordinates.FromRelease(content == "unknown" ? wanted : current), Path.GetFileName(path), 0)]);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.DownloadPak(ReleaseCoordinates.FromRelease(wanted), It.IsAny<string>()))
            .Returns((ReleaseCoordinates _, string output) => {
                Assert.EndsWith(".partial", output);
                Assert.False(File.Exists(path));
                var bytes = Encoding.UTF8.GetBytes(wanted.Tag);
                return Prelude.RightAsync<ModPakStreamAcquisitionFailure, FileWriter>(new FileWriter(output, new MemoryStream(bytes), bytes.Length));
            });
        var manager = new ModManager(registry.Object, paks, [ReleaseCoordinates.FromRelease(wanted)],
            [new Mod(wanted.Info, content == "unknown" ? [wanted] : [wanted, current])]);
        var dialogue = Dialogue(choice);
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => { });
        var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);
        var nextStage = false;
        var pipeline = Chivalry2LaunchPreparer.Create<LaunchOptions>(value => Prelude.Some(value))
            .AndThen(new Chivalry2ModsInstaller(manager, dialogue.Object))
            .AndThen(Chivalry2LaunchPreparer.Create<LaunchOptions>(value => { nextStage = true; return Prelude.Some(value); }));

        Assert.Equal(choice == UserDialogueChoice.Yes, (await pipeline.PrepareLaunch(options)).IsSome);
        Assert.Equal(choice == UserDialogueChoice.Yes, nextStage);
        Assert.Equal(choice == UserDialogueChoice.Yes ? wanted.Tag : content, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.partial"));
        registry.Verify(r => r.DownloadPak(It.IsAny<ReleaseCoordinates>(), It.IsAny<string>()), choice == UserDialogueChoice.Yes ? Times.Once : Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedHashIdentifiesLatestMatchingVersionRegardlessOfRegistryOrder(bool newestFirst) {
        var wanted = Release("v9.0.0");
        var older = Release("v1.0.0");
        var latest = Release("v10.0.0") with { ReleaseHash = older.ReleaseHash, ReleaseDate = older.ReleaseDate.AddDays(-1) };
        var path = Write("q__-__Mod1.pak", older.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var dialogue = Dialogue(UserDialogueChoice.Yes);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, changes) => {
                var change = Assert.Single(changes);
                Assert.Null(change.CurrentVersion);
                Assert.Equal(latest.Tag, change.LatestVersion);
            }).Returns(UserDialogueChoice.Yes);

        var adopted = new List<Release>();
        Assert.True(await paks.RecoverReleases([wanted], newestFirst ? [latest, older] : [older, latest], dialogue.Object, adopted));
        Assert.Equal(older.Tag, File.ReadAllText(path));
        Assert.Equal(latest, Assert.Single(adopted));
        Assert.Equal(latest.Tag, Assert.Single(await paks.GetManagedPaks()).Coordinates.Version);
        dialogue.Verify(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()), Times.Once);
    }

    [Theory]
    [InlineData(UserDialogueChoice.Yes, false)]
    [InlineData(UserDialogueChoice.Yes, true)]
    [InlineData(UserDialogueChoice.No, false)]
    [InlineData(UserDialogueChoice.Cancel, false)]
    public async Task UnmanagedDecisionEnablesDetectedVersionAndOnlyCancelStopsLaunch(UserDialogueChoice choice, bool previouslyManaged) {
        var detected = Release("v1.0.0");
        var otherVersion = Release("v2.0.0");
        Write("manual-name.pak", detected.Tag);
        if (previouslyManaged) Write("q__-__Mod1.pak", otherVersion.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir, previouslyManaged
            ? [new ManagedPak(ReleaseCoordinates.FromRelease(otherVersion), "q__-__Mod1.pak", 0)] : []);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var manager = new ModManager(registry.Object, paks, previouslyManaged ? [ReleaseCoordinates.FromRelease(otherVersion)] : [], [new Mod(detected.Info, [otherVersion, detected])]);
        var dialogue = Dialogue(choice);
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => { });
        var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);

        Assert.Equal(choice != UserDialogueChoice.Cancel,
            (await new Chivalry2ModsInstaller(manager, dialogue.Object).PrepareLaunch(options)).IsSome);

        if (choice == UserDialogueChoice.Yes) {
            Assert.Equal(ReleaseCoordinates.FromRelease(detected), Assert.Single(manager.EnabledModReleaseCoordinates));
            Assert.Equal(detected.Tag, Assert.Single(await paks.GetManagedPaks()).Coordinates.Version);
            Assert.Single(Directory.GetFiles(_dir, "*.pak"));
        }
        else Assert.Empty(manager.EnabledModReleaseCoordinates);
        registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DuplicateOfManagedPakIsRemovedWithoutPrompt() {
        var wanted = Release("v1.0.0");
        var path = Write("q__-__Mod1.pak", wanted.Tag);
        var duplicate = Write("unrelated-name.pak", wanted.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir,
            [new ManagedPak(ReleaseCoordinates.FromRelease(wanted), Path.GetFileName(path), 0)]);

        Assert.True(await paks.RecoverReleases([wanted], [wanted], new Mock<IUserDialogueSpawner>(MockBehavior.Strict).Object));

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(duplicate));
        Assert.Single(await paks.GetManagedPaks());
    }

    [Fact]
    public async Task BaseGamePakIsNeverAnUnmanagedCandidate() {
        var path = Write("pakchunk0-WindowsNoEditor.pak", "game content");
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.True(await paks.RecoverReleases([], [], new Mock<IUserDialogueSpawner>(MockBehavior.Strict).Object));

        Assert.Empty(await paks.GetManagedPaks());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencySnapshotRecoversAndReordersWithoutDownloading(bool sameModuleName) {
        var dependent = Release("v2.0.0");
        var dependency = Release("v1.0.0") with {
            Info = dependent.Info with {
                RepoUrl = sameModuleName ? "https://github.com/OtherOrg/Mod1" : "https://github.com/Org/Mod2"
            }
        };
        dependent = dependent with { Info = dependent.Info with { Dependencies = [new Dependency(dependency.Info.RepoUrl, "*")] } };
        var stems = sameModuleName ? new[] { "OtherOrg_Mod1", "Org_Mod1" } : new[] { "Mod2", "Mod1" };
        var dependencyContent = dependency.Tag;
        Write($"qy__-__{stems[0]}.pak", dependencyContent);
        Write($"qz__-__{stems[1]}.pak", dependent.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var manager = new ModManager(registry.Object, paks, [ReleaseCoordinates.FromRelease(dependent)],
            [new Mod(dependent.Info, [dependent]), new Mod(dependency.Info, [dependency])]);
        var dialogue = Dialogue(UserDialogueChoice.Yes);
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => { });
        var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);

        Assert.True((await new Chivalry2ModsInstaller(manager, dialogue.Object).PrepareLaunch(options)).IsSome);

        Assert.Equal(new[] { $"qz__-__{stems[0]}.pak", $"qy__-__{stems[1]}.pak" },
            (await paks.GetManagedPaks()).OrderBy(pak => pak.Priority).Select(pak => pak.PakFileName));
        Assert.Equal(dependencyContent, File.ReadAllText(Path.Join(_dir, $"qz__-__{stems[0]}.pak")));
        Assert.Equal(2, Directory.GetFiles(_dir, "*.pak").Length);
        registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedCandidatesOrConflictingSignaturesPreventAllMutations(bool changeDuringPrompt) {
        var wanted = Release("v1.0.0");
        var path = Write("q__-__Mod1.pak", wanted.Tag);
        var duplicate = Write("q__-__Mod1 (9).pak", wanted.Tag);
        Write("q__-__Mod1.sig", "signature");
        Write("q__-__Mod1 (9).sig", changeDuringPrompt ? "signature" : "conflict");
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var dialogue = Dialogue(UserDialogueChoice.Yes);
        if (changeDuringPrompt)
            dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
                .Callback(() => File.WriteAllText(duplicate, "changed")).Returns(UserDialogueChoice.Yes);

        Assert.False(await paks.RecoverReleases([wanted], [wanted], dialogue.Object));

        Assert.Empty(await paks.GetManagedPaks());
        Assert.Equal(wanted.Tag, File.ReadAllText(path));
        Assert.True(File.Exists(duplicate));
        Assert.False(File.Exists(Path.Join(_dir, ".unchained", "pending")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignatureRelocationCoalescesOnlyIdenticalSignatures(bool identical) {
        var wanted = Release("v1.0.0");
        var source = Write("q__-__Mod1 (9).pak", wanted.Tag);
        Write("q__-__Mod1 (9).sig", "signature");
        Write("q__-__Mod1.sig", identical ? "signature" : "conflict");
        var paks = new Core.Services.Mods.PakDir(_dir, [new ManagedPak(ReleaseCoordinates.FromRelease(wanted), Path.GetFileName(source), 7)]);
        var results = await paks.InstallModSet([new ModInstallRequest(ReleaseCoordinates.FromRelease(wanted), _ => throw new Exception("Unexpected download"))], Prelude.None).ToListAsync();
        Assert.Equal(identical, Assert.Single(results).IsRight);
        Assert.Equal(!identical, File.Exists(source));
        Assert.Equal("signature", File.ReadAllText(Path.ChangeExtension(Path.Join(_dir, Assert.Single(await paks.GetManagedPaks()).PakFileName), ".sig")));
    }

    [Fact]
    public async Task FailedRecoveryMutationRetainsTheInterruptionMarker() {
        var wanted = Release("v1.0.0");
        var path = Write("q__-__Mod1.pak", wanted.Tag);
        var duplicate = Write("q__-__Mod1 (9).pak", wanted.Tag);
        Directory.CreateDirectory(Path.ChangeExtension(duplicate, ".sig")); // Deny signature deletion after confirmation.
        var paks = new Core.Services.Mods.PakDir(_dir, []);

        Assert.False(await paks.RecoverReleases([wanted], [wanted], Dialogue(UserDialogueChoice.Yes).Object));

        Assert.True(File.Exists(Path.Join(_dir, ".unchained", "pending")));
        Assert.Equal(wanted.Tag, File.ReadAllText(path));
        Assert.Equal(wanted.Tag, File.ReadAllText(duplicate));
        await Assert.ThrowsAsync<InvalidDataException>(() => new Core.Services.Mods.PakDir(_dir, []).GetManagedPaks());
    }
}