using LanguageExt;
using log4net;
using log4net.Appender;
using log4net.Core;
using Moq;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

[Collection("Home launch UI")]
public sealed class SavedPakMigrationTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SavedPakMigration_" + Guid.NewGuid().ToString("N"));
    private readonly string _cwd = Directory.GetCurrentDirectory();
    private readonly string? _appData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private readonly ReleaseCoordinates _coordinates = new("Org", "Mod", "v1");
    private string Target => Path.GetFullPath(FilePaths.PakDir);
    private string Source => SavedPakMigration.SourceDirectories().Last();

    public SavedPakMigrationTests() {
        Directory.CreateDirectory(_root);
        Directory.SetCurrentDirectory(_root);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(_root, "LocalAppData"));
    }

    private static void Write(string directory, string name, string bytes) {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), bytes);
    }

    private string Cached(string directory) => new ModManagerCodec(_registry).Serialize(new ModManager(_registry,
        new Core.Services.Mods.PakDir(directory, [new(_coordinates, "managed.pak", 0)]), [_coordinates]));

    [Fact]
    public void MissingSourcesAreANoop() {
        var dir = ModManagerCodec.CreateInstallationPakDir();
        Assert.Equal(Target, dir.DirPath);
        Assert.Empty(dir.ManagedPaks);
        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public void VolumeRootsAreBoundariesNotDirectoryLinks() {
        // Includes Wine/Proton drive mappings, whose roots can report ReparsePoint.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            Assert.Empty(SavedPakMigration.Migrate(drive.RootDirectory.FullName, []));
    }

    [Theory]
    [InlineData("source-ancestor")]
    [InlineData("destination")]
    [InlineData("destination-ancestor")]
    [InlineData("source-file")]
    [InlineData("destination-file")]
    public void LinksBelowTheVolumeBoundaryAreRejected(string kind) {
        Write(Source, "unknown.pak", "preserved");
        var external = Path.Combine(_root, "external");
        Write(external, "unknown.pak", "preserved");
        var source = Source;
        var target = Target;
        var link = Path.Combine(_root, "link");
        var fileLink = kind.EndsWith("file");
        if (fileLink) File.CreateSymbolicLink(link, Path.Combine(external, "unknown.pak"));
        else Directory.CreateSymbolicLink(link, external);
        try {
            switch (kind) {
                case "source-ancestor": source = Path.Combine(link, "missing", "Paks"); break;
                case "destination": target = link; break;
                case "destination-ancestor": target = Path.Combine(link, "missing", "Paks"); break;
                case "source-file": File.Move(link, Path.Combine(Source, "linked.pak")); link = Path.Combine(Source, "linked.pak"); break;
                case "destination-file": Directory.CreateDirectory(Target); File.Move(link, Path.Combine(Target, "unknown.pak")); link = Path.Combine(Target, "unknown.pak"); break;
            }
            var ex = Assert.Throws<IOException>(() => SavedPakMigration.Migrate(target, [source]));
            Assert.Contains("link", ex.Message);
            Assert.Equal("preserved", File.ReadAllText(Path.Combine(Source, "unknown.pak")));
            Assert.Equal("preserved", File.ReadAllText(Path.Combine(external, "unknown.pak")));
        }
        finally {
            if (fileLink) File.Delete(link);
            else Directory.Delete(link);
        }
    }

    [Fact]
    public void JunctionAncestorIsRejectedWithoutFollowingIt() {
        var external = Path.Combine(_root, "external");
        Write(external, "unknown.pak", "preserved");
        var link = Path.Combine(_root, "junction");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, external }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try {
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            Assert.Throws<IOException>(() => SavedPakMigration.Migrate(Target, [Path.Combine(link, "missing", "Paks")]));
            Assert.Equal("preserved", File.ReadAllText(Path.Combine(external, "unknown.pak")));
            Assert.False(Directory.Exists(Target));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task LaunchMigrationFailureLogsExceptionAndPathsBeforeShowingDialogue() {
        Write(Source, "unknown.pak", "source bytes");
        Write(Target, "unknown.pak", "different target bytes");
        var logger = (log4net.Repository.Hierarchy.Logger)LogManager.GetLogger(typeof(SavedPaksPreparer)).Logger;
        var repository = logger.Repository!;
        var appender = new MemoryAppender();
        var level = logger.Level;
        var configured = repository.Configured;
        logger.Level = Level.Error;
        repository.Configured = true;
        logger.AddAppender(appender);
        try {
            var dialogue = new Mock<IUserDialogueSpawner>();
            dialogue.Setup(d => d.DisplayMessage(It.IsAny<string>())).Callback(() => {
                var logged = Assert.Single(appender.GetEvents());
                Assert.Equal(typeof(SavedPaksPreparer).FullName, logged.LoggerName);
                Assert.Equal(Level.Error, logged.Level);
                var exception = Assert.IsType<IOException>(logged.ExceptionObject);
                Assert.Contains("Launch cancelled", logged.RenderedMessage);
                Assert.Contains(Target, logged.RenderedMessage);
                Assert.Contains(Path.Combine(Source, "unknown.pak"), exception.Message);
                Assert.Contains(Path.Combine(Target, "unknown.pak"), exception.Message);
            });
            var preparer = new SavedPaksPreparer(new Core.Services.Mods.PakDir(Target, []), dialogue.Object);
            Assert.True((await preparer.PrepareLaunch(new([], Prelude.None, "", false, false, Prelude.None, Prelude.None))).IsNone);
            dialogue.Verify(d => d.DisplayMessage(It.IsAny<string>()), Times.Once);
            Assert.Equal("source bytes", File.ReadAllText(Path.Combine(Source, "unknown.pak")));
            Assert.Equal("different target bytes", File.ReadAllText(Path.Combine(Target, "unknown.pak")));
            Assert.Empty(Directory.GetFiles(Target, "*.partial"));
        }
        finally {
            logger.RemoveAppender(appender);
            logger.Level = level;
            repository.Configured = configured;
            appender.Close();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NewStartupMovesEveryPakAndSignatureButLeavesOtherFiles(int sourceIndex) {
        var source = SavedPakMigration.SourceDirectories().ElementAt(sourceIndex);
        Write(source, "unknown.pak", "unknown");
        Write(source, "unknown.sig", "signature");
        Write(source, "orphan.sig", "orphan signature");
        Write(source, "other.PAK", "other pak");
        Write(source, "keep.txt", "not a pak");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        Write(Path.Combine(source, "nested"), "nested.pak", "nested pak");
        Write(Path.Combine(source, "nested"), "nested.sig", "nested signature");
        Write(Path.Combine(source, "nested"), "keep.txt", "unrelated nested file");
        var dir = ModManagerCodec.CreateInstallationPakDir();
        Assert.Empty(dir.ManagedPaks);
        Assert.Equal("unknown", File.ReadAllText(Path.Combine(Target, "unknown.pak")));
        Assert.Equal("signature", File.ReadAllText(Path.Combine(Target, "unknown.sig")));
        Assert.Equal("orphan signature", File.ReadAllText(Path.Combine(Target, "orphan.sig")));
        Assert.Equal("other pak", File.ReadAllText(Path.Combine(Target, "other.PAK")));
        Assert.Equal(new[] { "keep.txt" }, Directory.GetFiles(source).Select(Path.GetFileName));
        Assert.Equal("nested pak", File.ReadAllText(Path.Combine(Target, "nested.pak")));
        Assert.Equal("nested signature", File.ReadAllText(Path.Combine(Target, "nested.sig")));
        Assert.Equal(new[] { "keep.txt" }, Directory.GetFiles(Path.Combine(source, "nested")).Select(Path.GetFileName));
        Assert.Empty(ModManagerCodec.CreateInstallationPakDir().ManagedPaks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedSavedOwnershipTransfersOnlyWithVerifiedSourceBytes(bool profile) {
        var source = profile ? SavedPakMigration.SourceDirectories("Server_1").Last() : Source;
        Write(source, "managed.pak", "owned");
        Write(source, "unknown.pak", "unknown");
        var json = Cached(source);
        var manager = ModManagerCodec.ForInstallation(_registry).Deserialize(json).Result!;
        Assert.Equal(_coordinates, Assert.Single(manager.PakDir.ManagedPaks).Coordinates);
        Assert.Equal("managed.pak", Assert.Single(manager.PakDir.ManagedPaks).PakFileName);
        Assert.Equal(new[] { _coordinates }, manager.EnabledModReleaseCoordinates);
        Assert.Empty(Directory.GetFiles(source));
        var saved = new ModManagerCodec(_registry).Serialize(manager);
        Assert.Single(ModManagerCodec.ForInstallation(_registry).Deserialize(saved).Result!.PakDir.ManagedPaks);
        // A crash before publishing the new directory cannot turn an unrelated same-name file into owned data.
        Assert.Empty(ModManagerCodec.ForInstallation(_registry).Deserialize(json).Result!.PakDir.ManagedPaks);
    }

    [Fact]
    public void IdenticalDuplicatesAreVerifiedAndRemovedFromSaved() {
        foreach (var directory in new[] { Source, Target }) {
            Write(directory, "managed.pak", "owned");
            Write(directory, "managed.sig", "signature");
        }
        var manager = ModManagerCodec.ForInstallation(_registry).Deserialize(Cached(Source)).Result!;
        Assert.Single(manager.PakDir.ManagedPaks);
        Assert.Empty(Directory.GetFiles(Source));
        Assert.Equal("signature", File.ReadAllText(Path.Combine(Target, "managed.sig")));
    }

    [Theory]
    [InlineData("managed.pak")]
    [InlineData("managed.sig")]
    public void DifferentPakOrSignatureCancelsBeforeChangingAnyFiles(string conflict) {
        foreach (var directory in new[] { Source, Target }) {
            Write(directory, "managed.pak", "owned");
            Write(directory, "managed.sig", "signature");
        }
        Write(Source, "another.pak", "also preserved");
        Write(Target, conflict, "conflict");
        Assert.Throws<IOException>(() => ModManagerCodec.ForInstallation(_registry).Deserialize(Cached(Source)));
        Assert.Equal("owned", File.ReadAllText(Path.Combine(Source, "managed.pak")));
        Assert.Equal("signature", File.ReadAllText(Path.Combine(Source, "managed.sig")));
        Assert.Equal("conflict", File.ReadAllText(Path.Combine(Target, conflict)));
        Assert.False(File.Exists(Path.Combine(Target, "another.pak")));
    }

    [Fact]
    public void UnknownIncomingPakDoesNotReviveStaleInstallationOwnership() {
        Write(Target, "managed.pak", "previous managed data");
        var json = Cached(Target);
        File.Delete(Path.Combine(Target, "managed.pak"));
        Write(Source, "managed.pak", "unknown replacement");
        var manager = ModManagerCodec.ForInstallation(_registry).Deserialize(json).Result!;
        Assert.Empty(manager.PakDir.ManagedPaks);
        Assert.Equal("unknown replacement", File.ReadAllText(Path.Combine(Target, "managed.pak")));
    }

    [Fact]
    public void CrossSourceConflictsPreserveBothSources() {
        Write(Source, "unknown.pak", "one");
        var other = SavedPakMigration.SourceDirectories().First();
        Write(other, "unknown.pak", "two");
        Assert.Throws<IOException>(() => ModManagerCodec.CreateInstallationPakDir());
        Assert.False(Directory.Exists(Target));
        Assert.Equal("one", File.ReadAllText(Path.Combine(Source, "unknown.pak")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(other, "unknown.pak")));
    }

    [Fact]
    public void NestedBasenameConflictsCancelInsteadOfOverwriting() {
        Write(Source, "unknown.pak", "top-level");
        Write(Path.Combine(Source, "nested"), "unknown.pak", "nested conflict");
        Assert.Throws<IOException>(() => ModManagerCodec.CreateInstallationPakDir());
        Assert.Equal("top-level", File.ReadAllText(Path.Combine(Source, "unknown.pak")));
        Assert.Equal("nested conflict", File.ReadAllText(Path.Combine(Source, "nested", "unknown.pak")));
        Assert.False(Directory.Exists(Target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedSavedDirectoryIsRejectedWithoutTouchingItsFiles(bool nested) {
        var external = Path.Combine(_root, "other-installation");
        Write(external, "unknown.pak", "do not follow");
        var link = nested ? Path.Combine(Source, "nested") : Source;
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, external);
        Assert.Throws<IOException>(() => ModManagerCodec.CreateInstallationPakDir());
        Assert.Equal("do not follow", File.ReadAllText(Path.Combine(external, "unknown.pak")));
        Assert.False(Directory.Exists(Target));
        Directory.Delete(link);
    }

    [Fact]
    public void CleanupFailureRetriesVerifiedCopiesWithoutOverwritingOrLeavingPartialFiles() {
        Write(Source, "managed.pak", "owned");
        Write(Source, "managed.sig", "signature");
        var json = Cached(Source);
        using (var locked = new FileStream(Path.Combine(Source, "managed.pak"), FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Assert.Throws<IOException>(() => ModManagerCodec.ForInstallation(_registry).Deserialize(json));
            Assert.Equal("owned", File.ReadAllText(Path.Combine(Source, "managed.pak")));
            Assert.Equal("owned", File.ReadAllText(Path.Combine(Target, "managed.pak")));
            Assert.Empty(Directory.GetFiles(Target, "*.partial"));
        }
        Assert.Single(ModManagerCodec.ForInstallation(_registry).Deserialize(json).Result!.PakDir.ManagedPaks);
        Assert.Empty(Directory.GetFiles(Source));
    }

    [Fact]
    public void CopyFailureKeepsSourcesAndCanRetryAfterDestinationIsUnblocked() {
        Write(Source, "managed.pak", "owned");
        Write(Source, "managed.sig", "signature");
        Directory.CreateDirectory(Path.Combine(Target, "managed.sig"));
        var json = Cached(Source);
        Assert.Throws<IOException>(() => ModManagerCodec.ForInstallation(_registry).Deserialize(json));
        Assert.Equal("owned", File.ReadAllText(Path.Combine(Source, "managed.pak")));
        Assert.Equal("signature", File.ReadAllText(Path.Combine(Source, "managed.sig")));
        Assert.Empty(Directory.GetFiles(Target, "*.partial"));
        Directory.Delete(Path.Combine(Target, "managed.sig"));
        Assert.Single(ModManagerCodec.ForInstallation(_registry).Deserialize(json).Result!.PakDir.ManagedPaks);
        Assert.Empty(Directory.GetFiles(Source));
    }

    [Fact]
    public async Task LaunchCheckpointPrecedesMigrationAndMigrationPrecedesPluginRecoveryOrDownload() {
        var source = SavedPakMigration.SourceDirectories("Server_1").Last();
        Write(source, "unknown.pak", "unknown");
        Write(Target, "unknown.pak", "conflict");
        var dialogue = new Mock<IUserDialogueSpawner>();
        var dir = new Core.Services.Mods.PakDir(Target, []);
        var saved = 0;
        var downstream = 0;
        IChivalry2LaunchPreparer<LaunchOptions> checkpoint = new SavePreferencesPreparer(() => saved++, dialogue.Object);
        var pipeline = checkpoint.AndThen(new SavedPaksPreparer(dir, dialogue.Object)).AndThen(
            Chivalry2LaunchPreparer.Create<LaunchOptions>(options => {
                Assert.True(saved > 0);
                Assert.Empty(Directory.GetFiles(source));
                downstream++;
                return Option<LaunchOptions>.Some(options);
            }));
        var options = new LaunchOptions([], Prelude.None, "", false, false, "Server_1", Prelude.None);
        Assert.True((await pipeline.PrepareLaunch(options)).IsNone);
        Assert.Equal(1, saved);
        Assert.Equal(0, downstream);
        Assert.Equal("unknown", File.ReadAllText(Path.Combine(source, "unknown.pak")));
        dialogue.Verify(d => d.DisplayMessage(It.Is<string>(s => s.Contains("Saved paks") && s.Contains("cancelled"))), Times.Once);
        File.WriteAllText(Path.Combine(Target, "unknown.pak"), "unknown");
        Assert.True((await pipeline.PrepareLaunch(options)).IsSome);
        Assert.Equal(2, saved);
        Assert.Equal(1, downstream);
        Assert.Empty(dir.ManagedPaks);
    }

    [Fact]
    public async Task FailedPreferenceSaveLeavesSavedPaksUntouched() {
        Write(Source, "unknown.pak", "unknown");
        var dialogue = Mock.Of<IUserDialogueSpawner>();
        IChivalry2LaunchPreparer<LaunchOptions> checkpoint = new SavePreferencesPreparer(() => throw new IOException("locked preferences"), dialogue);
        var pipeline = checkpoint.AndThen(new SavedPaksPreparer(new Core.Services.Mods.PakDir(Target, []), dialogue));
        Assert.True((await pipeline.PrepareLaunch(new([], Prelude.None, "", false, false, Prelude.None, Prelude.None))).IsNone);
        Assert.True(File.Exists(Path.Combine(Source, "unknown.pak")));
        Assert.False(Directory.Exists(Target));
    }

    public void Dispose() {
        Directory.SetCurrentDirectory(_cwd);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _appData);
        Directory.Delete(_root, true);
    }
}