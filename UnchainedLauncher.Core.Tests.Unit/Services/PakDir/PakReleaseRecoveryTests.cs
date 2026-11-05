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

    [Fact]
    public async Task RequestedHash_RecoversMissingMetadata_WithoutDownloadOrPrompt() {
        var wanted = Release("v1.0.0");
        File.WriteAllText(Path.Join(_dir, "q__-__Mod1.pak"), wanted.Tag);
        File.WriteAllText(Path.Join(_dir, "q__-__Mod1 (9).pak"), wanted.Tag);
        File.WriteAllText(Path.Join(_dir, "q__-__Other.pak"), "unrecognized manual mod");
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        for (var attempt = 0; attempt < 3; attempt++) {
            var paks = new Core.Services.Mods.PakDir(_dir, []);
            var manager = Manager(paks, registry, wanted);
            Assert.True(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
            var results = await manager.InstallMods(Option<AccumulatedMemoryProgress>.None).ToListAsync();
            results.ForEach(result => result.IfLeft(error => throw error));
            Assert.Equal("q__-__Mod1.pak", Assert.Single(paks.ManagedPaks).PakFileName);
            Assert.Equal(2, Directory.GetFiles(_dir, "*.pak").Length);
        }
        Assert.Equal("unrecognized manual mod", File.ReadAllText(Path.Join(_dir, "q__-__Other.pak")));
        registry.VerifyNoOtherCalls();
        dialogue.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("v1.0.0", "v2.0.0", UserDialogueChoice.Yes, "Upgrade")]
    [InlineData("v2.0.0", "v1.0.0", UserDialogueChoice.Yes, "Downgrade")]
    [InlineData("v1.0.0", "v2.0.0", UserDialogueChoice.No, "Upgrade")]
    [InlineData("v2.0.0", "v1.0.0", UserDialogueChoice.Cancel, "Downgrade")]
    public async Task DifferentReleaseHash_UsesStandardVersionChangeConfirmation(string currentTag, string wantedTag, UserDialogueChoice choice, string reason) {
        var wanted = Release(wantedTag);
        var current = Release(currentTag);
        var path = Path.Join(_dir, "q__-__Mod1.pak");
        File.WriteAllText(path, currentTag);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), wanted, current);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, updates) => {
                var update = Assert.Single(updates);
                Assert.Equal(currentTag, update.CurrentVersion);
                Assert.Equal(wantedTag, update.LatestVersion);
                Assert.Equal(reason, update.Reason);
                Assert.Equal(currentTag, File.ReadAllText(path));
                Assert.Empty(paks.ManagedPaks);
            }).Returns(choice);

        Assert.Equal(choice == UserDialogueChoice.Yes, await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        Assert.Equal(choice != UserDialogueChoice.Yes, File.Exists(path));
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
    [InlineData("v1.0.0", "v2.0.0", true)]
    [InlineData("v2.0.0", "v1.0.0", true)]
    [InlineData("corrupt tracked content", "v2.0.0", false)]
    public async Task ApprovedReplacement_InstallsRequestedReleaseWithoutNumberedCopy(string currentContent, string targetTag, bool knownRelease) {
        var wanted = Release(targetTag);
        var path = Path.Join(_dir, "q__-__Mod1.pak");
        File.WriteAllText(path, currentContent);
        var coordinates = ReleaseCoordinates.FromRelease(wanted);
        var installedCoordinates = knownRelease ? ReleaseCoordinates.FromRelease(Release(currentContent)) : coordinates;
        var paks = new Core.Services.Mods.PakDir(_dir, [new ManagedPak(installedCoordinates, Path.GetFileName(path), 0)]);
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.DownloadPak(coordinates, It.IsAny<string>()))
            .Returns((ReleaseCoordinates _, string output) => {
                Assert.EndsWith(".partial", output);
                var bytes = Encoding.UTF8.GetBytes(wanted.Tag);
                return Prelude.RightAsync<ModPakStreamAcquisitionFailure, FileWriter>(new FileWriter(output, new MemoryStream(bytes), bytes.Length));
            });
        var manager = Manager(paks, registry, knownRelease ? [wanted, Release(currentContent)] : [wanted]);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(UserDialogueChoice.Yes);

        Assert.True(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        var results = await manager.InstallMods(Option<AccumulatedMemoryProgress>.None).ToListAsync();

        results.ForEach(result => result.IfLeft(error => throw error));
        Assert.Equal(wanted.Tag, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir, "*.pak"));
        Assert.Empty(Directory.GetFiles(_dir, "*.partial"));
        registry.Verify(r => r.DownloadPak(coordinates, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task MatchingHashOutsideDestinationFamily_IsNotAdoptedOrDeleted() {
        var wanted = Release("v1.0.0");
        var path = Path.Join(_dir, "q__-__Other.pak");
        File.WriteAllText(path, wanted.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), wanted);

        Assert.True(await new PakReleaseRecovery(manager, new Mock<IUserDialogueSpawner>(MockBehavior.Strict).Object).Recover([wanted]));
        Assert.Empty(paks.ManagedPaks);
        Assert.Equal(wanted.Tag, File.ReadAllText(path));
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

        Assert.True(await new PakReleaseRecovery(manager, new Mock<IUserDialogueSpawner>(MockBehavior.Strict).Object).Recover([first, second]));

        Assert.Equal(2, paks.ManagedPaks.Select(pak => pak.PakFileName).Distinct().Count());
        Assert.Equal(2, Directory.GetFiles(_dir, "*.pak").Length);
    }

    [Fact]
    public async Task FileChangedWhileConfirming_IsNotDeleted() {
        var wanted = Release("v2.0.0");
        var current = Release("v1.0.0");
        var path = Path.Join(_dir, "q__-__Mod1.pak");
        File.WriteAllText(path, current.Tag);
        var paks = new Core.Services.Mods.PakDir(_dir, []);
        var manager = Manager(paks, new Mock<IModRegistry>(MockBehavior.Strict), wanted, current);
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback(() => File.WriteAllText(path, "changed during confirmation")).Returns(UserDialogueChoice.Yes);

        Assert.False(await new PakReleaseRecovery(manager, dialogue.Object).Recover([wanted]));
        Assert.Equal("changed during confirmation", File.ReadAllText(path));
        Assert.Empty(paks.ManagedPaks);
    }

    [Fact]
    public async Task DeclinedVersionChange_CancelsLaunchBeforeInstallation() {
        var wanted = Release("v2.0.0");
        var current = Release("v1.0.0");
        File.WriteAllText(Path.Join(_dir, "q__-__Mod1.pak"), current.Tag);
        var manager = Manager(new Core.Services.Mods.PakDir(_dir, []), new Mock<IModRegistry>(MockBehavior.Strict), wanted, current);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Yes", "No", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(UserDialogueChoice.No);
        var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);

        Assert.True((await new Chivalry2ModsInstaller(manager, dialogue.Object).PrepareLaunch(options)).IsNone);
        Assert.Equal(current.Tag, File.ReadAllText(Path.Join(_dir, "q__-__Mod1.pak")));
        dialogue.Verify(d => d.DisplayProgress(It.IsAny<MemoryProgress>()), Times.Never);
    }
}
