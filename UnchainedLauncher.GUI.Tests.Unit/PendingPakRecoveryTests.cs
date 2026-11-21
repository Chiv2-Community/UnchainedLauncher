using LanguageExt;
using Moq;
using System.Security.Cryptography;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class PendingPakRecoveryTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PendingPaks_" + Guid.NewGuid().ToString("N"));
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private string Paks => Path.Combine(_root, "Paks");
    private string Config => Path.Combine(_root, "config.json");
    private static readonly byte[] Payload = [1, 2, 3];
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA512.HashData(bytes));
    private static Release Release => new("v1", Hash(Payload), "A.pak", DateTime.UtcNow,
        new("https://github.com/org/A", "A", "", null, null, [], [], []),
        new([], [], [], [], []), "");
    private static ReleaseCoordinates Coordinates => ReleaseCoordinates.FromRelease(Release);

    public static IEnumerable<object[]> Cases() {
        foreach (var cached in new[] { false, true })
        foreach (var operation in new[] { "collision", "published", "rename", "rename-source-present" })
        foreach (var identical in new[] { false, true })
        foreach (var choice in new[] { UserDialogueChoice.Yes, UserDialogueChoice.No, UserDialogueChoice.Cancel })
            yield return [cached, operation, identical, choice];
    }

    private PakDir NewPaks() => new(Paks, [], cachePath: _cached ? Path.Combine(_root, "cache") : null);
    private bool _cached;
    private ModManager Restart() => LegacyOwnershipMigration.LoadProfile(Config, _registry, NewPaks(), []).Manager;

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task LastSuccessfulIntentRequiresRecoveryChoice(bool cached, string operation, bool identical, UserDialogueChoice choice) {
        _cached = cached;
        Directory.CreateDirectory(Paks);
        var destination = Path.Combine(Paks, "A_0_P.pak");
        var evidence = Config + ".last-success";
        var foreign = identical ? Payload : new byte[] { 8, 9 };
        var retainedSource = operation == "rename-source-present";
        var confirmedCount = retainedSource ? 1 : 0;
        var paks = NewPaks();
        var old = new ManagedPak(Coordinates, "old.pak", 0);
        if (operation.StartsWith("rename", StringComparison.Ordinal)) {
            File.WriteAllBytes(Path.Combine(Paks, old.PakFileName), Payload);
            paks = new PakDir(Paks, [old], cachePath: cached ? Path.Combine(_root, "cache") : null);
        }
        var manager = new ModManager(_registry, paks, []);
        void BlockSave() {
            File.Move(Config, evidence);
            Directory.CreateDirectory(Config);
        }
        // Obstruct the final checkpoint after the real move. The intent checkpoint
        // remains the actual last successful config, not a synthetic inventory.
        if (operation.StartsWith("rename", StringComparison.Ordinal)) paks.OwnershipChanged += (_, _) => BlockSave();
        new ModManagerPersistence(manager, Config).Save();
        var request = new ModInstallRequest(Coordinates, path => {
            if (operation == "collision") File.WriteAllBytes(destination, foreign);
            BlockSave();
            return new FileWriter(path, new MemoryStream(Payload), Payload.Length);
        }, Hash(Payload));
        var failure = await Record.ExceptionAsync(async () => {
            await foreach (var result in paks.InstallModSet([request], Prelude.None))
                Assert.Equal(operation != "collision", result.IsRight);
        });
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        if (operation != "collision") {
            // An occupied intent cannot prove ownership, including replacement of
            // the destination after an interrupted successful publication/move.
            File.Delete(destination);
            File.WriteAllBytes(destination, foreign);
        }
        if (retainedSource) File.WriteAllBytes(Path.Combine(Paks, old.PakFileName), Payload);
        Directory.Delete(Config);
        File.Move(evidence, Config);
        if (operation == "collision" && !identical) {
            var metadata = Path.Combine(Paks, ".unchained");
            Directory.CreateDirectory(metadata);
            File.WriteAllText(Path.Combine(metadata, "kept-unmanaged-paks.json"),
                System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["A_0_P.pak"] = Hash(foreign) }));
        }
        manager = Restart();
        Assert.Equal(confirmedCount, ((PakDir)manager.PakDir).ManagedPaks.Count());
        Assert.Empty(manager.EnabledModReleaseCoordinates);
        var before = File.ReadAllBytes(Config);
        var calls = 0;
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel",
            It.IsAny<IEnumerable<DependencyUpdate>>())).Returns(() => {
                calls++;
                Assert.Equal(foreign, File.ReadAllBytes(destination));
                Assert.Equal(before, File.ReadAllBytes(Config));
                Assert.Equal(confirmedCount, ((PakDir)manager.PakDir).ManagedPaks.Count());
                return choice;
            });
        // Even AlwaysKeep cannot authorize ambiguous publication evidence.
        var adopted = new List<Release>();
        IReadOnlyList<Release>? roots = null;
        Assert.Equal(choice != UserDialogueChoice.Cancel,
            await manager.PakDir.RecoverReleases([], [Release], dialogue.Object, adopted, alwaysKeepUnmanagedPaks: true,
                resolvePlan: selected => { roots = selected; return selected; }));
        Assert.Equal(1, calls);
        Assert.Empty(manager.EnabledModReleaseCoordinates); // CLI request remains empty.
        if (cached) {
            Assert.Empty(adopted);
            if (roots != null) Assert.Empty(roots);
        }
        if (choice == UserDialogueChoice.Cancel) {
            Assert.Equal(before, File.ReadAllBytes(Config));
            Assert.Equal(foreign, File.ReadAllBytes(destination));
            Assert.Equal(confirmedCount, ((PakDir)Restart().PakDir).ManagedPaks.Count());
            if (confirmedCount == 0) {
                // Even a caller bypassing recovery cannot delete by intent alone.
                await foreach (var result in Restart().PakDir.InstallModSet([], Prelude.None)) Assert.True(result.IsRight);
                Assert.Equal(foreign, File.ReadAllBytes(destination));
                Assert.Contains("PendingPublications", File.ReadAllText(Config));
                Assert.Empty(((PakDir)Restart().PakDir).ManagedPaks);
            }
            return;
        }
        Assert.DoesNotContain("PendingPublications", File.ReadAllText(Config));
        if (choice == UserDialogueChoice.No) {
            Assert.False(File.Exists(destination));
            Assert.Equal(confirmedCount, ((PakDir)Restart().PakDir).ManagedPaks.Count());
            return;
        }
        Assert.Equal(foreign, File.ReadAllBytes(destination));
        Assert.Equal(confirmedCount + (identical ? 1 : 0), ((PakDir)Restart().PakDir).ManagedPaks.Count());
        if (!identical) {
            // Remembered unknown Keep survives restart without another prompt.
            Assert.True(await Restart().PakDir.RecoverReleases([], [Release], new Mock<IUserDialogueSpawner>(MockBehavior.Strict).Object));
        }
        await foreach (var result in Restart().PakDir.InstallModSet([], Prelude.None)) Assert.True(result.IsRight);
        Assert.Equal(!identical, File.Exists(destination));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RecoveryRejectsTamperingOrDuplicateSignatureConflict(bool cached, bool signatureConflict) {
        _cached = cached;
        Directory.CreateDirectory(Paks);
        var destination = Path.Combine(Paks, "A_0_P.pak");
        var paks = NewPaks();
        new ModManagerPersistence(new ModManager(_registry, paks, []), Config).Save();
        var evidence = Config + ".last-success";
        var request = new ModInstallRequest(Coordinates, path => {
            File.WriteAllBytes(destination, Payload);
            File.Move(Config, evidence);
            Directory.CreateDirectory(Config);
            return new FileWriter(path, new MemoryStream(Payload), Payload.Length);
        }, Hash(Payload));
        var failure = await Record.ExceptionAsync(async () => {
            await foreach (var _ in paks.InstallModSet([request], Prelude.None)) { }
        });
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Directory.Delete(Config);
        File.Move(evidence, Config);
        var other = Path.Combine(Paks, "other.pak");
        if (signatureConflict) {
            File.WriteAllBytes(other, Payload);
            File.WriteAllText(Path.ChangeExtension(other, ".sig"), "other signature");
            File.WriteAllText(Path.ChangeExtension(destination, ".sig"), "pending signature");
        }
        var manager = Restart();
        var before = File.ReadAllBytes(Config);
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel",
            It.IsAny<IEnumerable<DependencyUpdate>>())).Returns(() => {
                if (!signatureConflict) File.WriteAllBytes(destination, [7, 8, 9, 10]);
                return UserDialogueChoice.No;
            });
        Assert.False(await manager.PakDir.RecoverReleases([], [Release], dialogue.Object));
        Assert.Equal(before, File.ReadAllBytes(Config));
        Assert.Empty(((PakDir)manager.PakDir).ManagedPaks);
        Assert.True(File.Exists(destination));
        if (signatureConflict) {
            Assert.Equal(Payload, File.ReadAllBytes(other));
            Assert.Equal("other signature", File.ReadAllText(Path.ChangeExtension(other, ".sig")));
            Assert.Equal("pending signature", File.ReadAllText(Path.ChangeExtension(destination, ".sig")));
        }
        else Assert.Equal(new byte[] { 7, 8, 9, 10 }, File.ReadAllBytes(destination));
    }

    [Theory]
    [InlineData(false, UserDialogueChoice.Yes)]
    [InlineData(false, UserDialogueChoice.No)]
    [InlineData(false, UserDialogueChoice.Cancel)]
    [InlineData(true, UserDialogueChoice.Yes)]
    [InlineData(true, UserDialogueChoice.No)]
    [InlineData(true, UserDialogueChoice.Cancel)]
    public async Task IdenticalConfirmedDuplicateDoesNotAuthorizePendingFile(bool cached, UserDialogueChoice choice) {
        _cached = cached;
        Directory.CreateDirectory(Paks);
        var confirmed = new ManagedPak(new ReleaseCoordinates("org", "B", "v1"), "B_0_P.pak", 0);
        var confirmedPath = Path.Combine(Paks, confirmed.PakFileName);
        File.WriteAllBytes(confirmedPath, Payload);
        var paks = new PakDir(Paks, [confirmed], cachePath: cached ? Path.Combine(_root, "cache") : null);
        new ModManagerPersistence(new ModManager(_registry, paks, []), Config).Save();
        var destination = Path.Combine(Paks, "A_1_P.pak");
        var evidence = Config + ".last-success";
        var existing = new ModInstallRequest(confirmed.Coordinates, _ => throw new Exception("Must not download confirmed file"));
        var request = new ModInstallRequest(Coordinates, path => {
            File.WriteAllBytes(destination, Payload);
            File.Move(Config, evidence);
            Directory.CreateDirectory(Config);
            return new FileWriter(path, new MemoryStream(Payload), Payload.Length);
        }, Hash(Payload));
        var failure = await Record.ExceptionAsync(async () => {
            await foreach (var _ in paks.InstallModSet([existing, request], Prelude.None)) { }
        });
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Directory.Delete(Config);
        File.Move(evidence, Config);
        var manager = Restart();
        Assert.Equal(confirmed, Assert.Single(((PakDir)manager.PakDir).ManagedPaks));
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel",
            It.IsAny<IEnumerable<DependencyUpdate>>())).Returns(choice);
        Assert.Equal(choice != UserDialogueChoice.Cancel,
            await manager.PakDir.RecoverReleases([], [Release], dialogue.Object, alwaysKeepUnmanagedPaks: true));
        dialogue.Verify(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel",
            It.IsAny<IEnumerable<DependencyUpdate>>()), Times.Once);
        Assert.Equal(Payload, File.ReadAllBytes(confirmedPath));
        Assert.Equal(choice != UserDialogueChoice.No, File.Exists(destination));
        Assert.Contains(confirmed, ((PakDir)Restart().PakDir).ManagedPaks);
    }

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
