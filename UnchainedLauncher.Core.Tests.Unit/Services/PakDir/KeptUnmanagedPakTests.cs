using Moq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

[Collection("Home launch UI")]
public class KeptUnmanagedPakTests : IDisposable {
    private readonly string _dir = Path.Combine("TestDirectories", nameof(KeptUnmanagedPakTests), Guid.NewGuid().ToString());
    private string ChoicesPath => Path.Combine(_dir, ".unchained", "kept-unmanaged-paks.json");
    public KeptUnmanagedPakTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string name, string content) {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static Mock<IUserDialogueSpawner> Dialogue(UserDialogueChoice choice, bool scan = true) {
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        if (scan) dialogue.Setup(value => value.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => { });
        dialogue.Setup(value => value.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(choice);
        return dialogue;
    }

    private async Task<bool> Recover(Mock<IUserDialogueSpawner> dialogue, bool alwaysKeep = false, params Release[] known) {
        using var cache = new PakReadCache(_dir);
        return await UnmanagedPakRecovery.Recover(new Core.Services.Mods.PakDir(_dir, []), known, dialogue.Object, [], alwaysKeep, cache);
    }

    private Dictionary<string, string> Choices() => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ChoicesPath))!;
    private static string Hash(string content) => Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public async Task CancelPersistsReadResultsButRegistryTagWinsWithoutScanning() {
        var path = Write("manual.pak", "original");
        var scans = 0;
        var hashes = 0;
        var expected = "1.2.3";
        var dialogue = Dialogue(UserDialogueChoice.Cancel);
        dialogue.Setup(value => value.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, updates) =>
                Assert.Equal(expected, Assert.Single(updates).LatestVersion)).Returns(UserDialogueChoice.Cancel);
        var release = new Release("v9.0.0", Hash("original"), "Mod1.pak", DateTime.UtcNow,
            new ModInfo("https://github.com/Org/Mod1", "Mod1", "", null, null, [], [], []), null!, null);
        for (var run = 0; run < 4; run++) {
            if (run == 2) expected = release.Tag;
            if (run == 3) File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
            using var cache = new PakReadCache(_dir, _ => {
                scans++;
                return Task.FromResult<string?>("1.2.3");
            }, async stream => {
                hashes++;
                return await SHA512.HashDataAsync(stream);
            });
            Assert.False(await UnmanagedPakRecovery.Recover(new Core.Services.Mods.PakDir(_dir, []),
                run < 2 ? [] : [release], dialogue.Object, [], false, cache));
        }
        Assert.Equal(1, scans);
        Assert.Equal(2, hashes);
        Assert.False(File.Exists(ChoicesPath));
        Assert.Empty(new Core.Services.Mods.PakDir(_dir, []).ManagedPaks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepSurvivesRestartButChangedNameOrHashRequiresNewConsent(bool rename) {
        var path = Write("manual.pak", "original");
        Assert.True(await Recover(Dialogue(UserDialogueChoice.Yes)));
        Assert.Equal(Hash("original"), Choices()["manual.pak"]);
        Assert.Empty(new Core.Services.Mods.PakDir(_dir, []).ManagedPaks);
        Assert.True(await Recover(new Mock<IUserDialogueSpawner>(MockBehavior.Strict)));

        if (rename) File.Move(path, Path.Combine(_dir, "renamed.pak"));
        else File.WriteAllText(path, "changed");
        var cancel = Dialogue(UserDialogueChoice.Cancel);
        Assert.False(await Recover(cancel));
        cancel.VerifyAll();
        Assert.Equal(Hash("original"), Choices()["manual.pak"]);
        Assert.True(await Recover(new Mock<IUserDialogueSpawner>(MockBehavior.Strict), true));
        var entry = Assert.Single(Choices());
        Assert.Equal(rename ? "renamed.pak" : "manual.pak", entry.Key);
        Assert.Equal(Hash(rename ? "original" : "changed"), entry.Value);
        Assert.Empty(new Core.Services.Mods.PakDir(_dir, []).ManagedPaks);
        Assert.True(await Recover(new Mock<IUserDialogueSpawner>(MockBehavior.Strict)));
    }

    [Fact]
    public async Task RemoveOnlyDeletesNewlyPromptedFilesAndPrunesMissingChoices() {
        var retained = Write("kept.pak", "kept");
        Assert.True(await Recover(Dialogue(UserDialogueChoice.Yes)));
        var added = Write("new.pak", "new");
        var remove = Dialogue(UserDialogueChoice.No);
        remove.Setup(value => value.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, updates) =>
                Assert.Equal("new.pak", Assert.Single(updates).Name)).Returns(UserDialogueChoice.No);
        Assert.True(await Recover(remove));
        Assert.True(File.Exists(retained));
        Assert.False(File.Exists(added));
        Assert.Equal("kept.pak", Assert.Single(Choices()).Key);
        File.Delete(retained);
        Assert.True(await Recover(new Mock<IUserDialogueSpawner>(MockBehavior.Strict)));
        Assert.Empty(Choices());
    }

    [Fact]
    public async Task KeptUnknownStillRequiresConfirmationWhenRegistryRecognizesIt() {
        Write("manual.pak", "original");
        Assert.True(await Recover(Dialogue(UserDialogueChoice.Yes)));
        var release = new Release("v1.0.0", Hash("original"), "Mod1.pak", DateTime.UtcNow,
            new ModInfo("https://github.com/Org/Mod1", "Mod1", "", null, null, [], [], []), null!, null);
        var cancel = Dialogue(UserDialogueChoice.Cancel, scan: false);
        Assert.False(await Recover(cancel, false, release));
        cancel.VerifyAll();
        Assert.True(await Recover(Dialogue(UserDialogueChoice.Yes, scan: false), false, release));
        Assert.Empty(Choices());
    }

    [Fact]
    public async Task ChangedDuringConfirmationDoesNotSaveConsent() {
        var path = Write("manual.pak", "original");
        var keep = Dialogue(UserDialogueChoice.Yes);
        keep.Setup(value => value.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback(() => File.WriteAllText(path, "changed during prompt")).Returns(UserDialogueChoice.Yes);
        await Assert.ThrowsAsync<IOException>(() => Recover(keep));
        Assert.False(File.Exists(ChoicesPath));
        Assert.False(await Recover(Dialogue(UserDialogueChoice.Cancel)));
    }

    [Fact]
    public async Task KeptFilesStillUndergoDuplicateSignatureChecks() {
        Write("kept.pak", "original");
        Write("kept.sig", "signature");
        Assert.True(await Recover(Dialogue(UserDialogueChoice.Yes)));
        var duplicate = Write("duplicate-copy.pak", "original");
        var signature = Write("duplicate-copy.sig", "different signature");
        await Assert.ThrowsAsync<IOException>(() => Recover(new Mock<IUserDialogueSpawner>(MockBehavior.Strict)));
        Assert.True(File.Exists(duplicate));
        Assert.True(File.Exists(signature));
        File.WriteAllText(signature, "signature");
        Assert.True(await Recover(new Mock<IUserDialogueSpawner>(MockBehavior.Strict)));
        Assert.False(File.Exists(duplicate));
        Assert.Equal("kept.pak", Assert.Single(Choices()).Key);
    }

    [Fact]
    public async Task CorruptChoicesRequireConfirmationAgain() {
        Write("manual.pak", "original");
        Assert.True(await Recover(Dialogue(UserDialogueChoice.Yes)));
        File.WriteAllText(ChoicesPath, "{corrupt");
        var cancel = Dialogue(UserDialogueChoice.Cancel);
        Assert.False(await Recover(cancel));
        cancel.VerifyAll();
        Assert.True(await Recover(Dialogue(UserDialogueChoice.Yes)));
        Assert.Equal(Hash("original"), Choices()["manual.pak"]);
    }
}