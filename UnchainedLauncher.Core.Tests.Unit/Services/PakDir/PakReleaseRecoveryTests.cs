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
        dialogue.Setup(d => d.DisplayMessage(It.IsAny<string>()));
        return dialogue;
    }

    [Theory]
    [InlineData(UserDialogueChoice.Yes, false)]
    [InlineData(UserDialogueChoice.Yes, true)]
    [InlineData(UserDialogueChoice.No, false)]
    [InlineData(UserDialogueChoice.Cancel, false)]
    public async Task UnmanagedAdoptionAndDuplicateRemovalRequireConfirmation(UserDialogueChoice choice, bool transferOwnership) {
        var wanted = Release("v1.0.0");
        var newer = Release("v2.0.0") with { ReleaseHash = wanted.ReleaseHash };
        var path = Write("q__-__Mod1.pak", wanted.Tag);
        var duplicate = Write("q__-__Mod1 (9).pak", wanted.Tag);
        Write("q__-__Mod1.sig", "signature");
        Write("q__-__Mod1 (9).sig", "signature");
        var unrelated = Write("q__-__Other.pak", wanted.Tag);
        ManagedPak[] previous = transferOwnership
            ? [new ManagedPak(new ReleaseCoordinates("PreviousOrg", "Mod1", wanted.Tag), Path.GetFileName(path), 0)] : [];
        var paks = new Core.Services.Mods.PakDir(_dir, previous);
        var sibling = new Core.Services.Mods.PakDir(_dir, []);
        var dialogue = Dialogue(choice);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, changes) => {
                Assert.Equal(2, changes.Count());
                Assert.Contains(changes, change => change.Name == Path.GetFileName(path) && change.Reason!.Contains("Adopt"));
                Assert.Contains(changes, change => change.Name == Path.GetFileName(duplicate) && change.Reason!.Contains("Remove duplicate"));
                Assert.Equal(previous, paks.ManagedPaks);
                Assert.True(File.Exists(duplicate));
            }).Returns(choice);
        using var exclusive = new FileStream(unrelated, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(choice == UserDialogueChoice.Yes, await paks.RecoverReleases([wanted], [wanted, newer], dialogue.Object));

        Assert.Equal(wanted.Tag, File.ReadAllText(path));
        if (choice == UserDialogueChoice.Yes) {
            Assert.False(File.Exists(duplicate));
            Assert.False(File.Exists(Path.ChangeExtension(duplicate, ".sig")));
            Assert.Equal(ReleaseCoordinates.FromRelease(wanted), Assert.Single(await sibling.GetManagedPaks()).Coordinates);
            Assert.True(await sibling.RecoverReleases([wanted], [wanted], new Mock<IUserDialogueSpawner>(MockBehavior.Strict).Object));
        }
        else {
            Assert.True(File.Exists(duplicate));
            Assert.Equal("signature", File.ReadAllText(Path.ChangeExtension(duplicate, ".sig")));
            Assert.Empty(await sibling.GetManagedPaks());
        }
        dialogue.Verify(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()), Times.Once);
    }

    [Theory]
    [InlineData("v1.0.0", "v2.0.0", UserDialogueChoice.Yes)]
    [InlineData("v2.0.0", "v1.0.0", UserDialogueChoice.No)]
    [InlineData("unknown", "v2.0.0", UserDialogueChoice.Cancel)]
    public async Task ReplacementConfirmationControlsLaunchAndPublishesOnlyCompletedPaks(string content, string tag, UserDialogueChoice choice) {
        var wanted = Release(tag);
        var current = Release(content);
        var path = Write(content == "unknown" ? "custom-tracked.pak" : "q__-__Mod1.pak", content);
        var paks = new Core.Services.Mods.PakDir(_dir, content == "unknown"
            ? [new ManagedPak(ReleaseCoordinates.FromRelease(wanted), Path.GetFileName(path), 0)] : []);
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
        var dialogue = Dialogue(UserDialogueChoice.No);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, changes) => {
                var change = Assert.Single(changes);
                Assert.Equal(latest.Tag, change.CurrentVersion);
                Assert.Equal("Downgrade", change.Reason);
            }).Returns(UserDialogueChoice.No);

        Assert.False(await paks.RecoverReleases([wanted], newestFirst ? [latest, older] : [older, latest], dialogue.Object));
        Assert.Equal(older.Tag, File.ReadAllText(path));
        Assert.Empty(await paks.GetManagedPaks());
        dialogue.Verify(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()), Times.Once);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DependencySnapshotRecoversAndReordersWithoutDownloading(bool sameModuleName, bool sharedPayload) {
        var dependent = Release("v2.0.0");
        var dependency = Release("v1.0.0") with {
            Info = dependent.Info with {
                RepoUrl = sameModuleName ? "https://github.com/OtherOrg/Mod1" : "https://github.com/Org/Mod2"
            }
        };
        if (sharedPayload) dependency = dependency with { ReleaseHash = dependent.ReleaseHash };
        dependent = dependent with { Info = dependent.Info with { Dependencies = [new Dependency(dependency.Info.RepoUrl, "*")] } };
        var stems = sameModuleName ? new[] { "OtherOrg_Mod1", "Org_Mod1" } : new[] { "Mod2", "Mod1" };
        var dependencyContent = sharedPayload ? dependent.Tag : dependency.Tag;
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
            dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
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