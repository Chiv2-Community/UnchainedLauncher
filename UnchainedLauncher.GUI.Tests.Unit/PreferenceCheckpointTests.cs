using LanguageExt;
using Moq;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.GUI.Services;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class PreferenceCheckpointTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCheckpointPrecedesSavedMutationAndDownstreamPlugins(bool server) {
        var root = Path.Combine(Path.GetTempPath(), "Checkpoint_" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "TBL", server ? "Saved_Server" : "Saved", "Paks");
        var target = Path.Combine(root, "TBL", "Content", "Paks");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "manual.pak"), "preserved");
        try {
            var registry = Mock.Of<IModRegistry>();
            var manager = ModManagerCodec.ToClassType(new StandardModManagerMetadata([], [], null), registry, target);
            var persistence = new ModManagerPersistence(manager, Path.Combine(root, ".mod_cache", "mod_manager_config.json"));
            var dialogue = new Mock<IUserDialogueSpawner>();
            var fail = true;
            IChivalry2LaunchPreparer<LaunchOptions> checkpoint = new SavePreferencesPreparer(() => {
                if (fail) throw new IOException("checkpoint");
                persistence.Save();
            }, dialogue.Object);
            var plugin = false;
            var pipeline = checkpoint.AndThen(new SavedPaksPreparer(manager.PakDir, dialogue.Object))
                .AndThen(Chivalry2LaunchPreparer.Create<LaunchOptions>(options => {
                    Assert.False(File.Exists(Path.Combine(source, "manual.pak")));
                    plugin = true;
                    return Prelude.Some(options);
                }));
            var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);
            if (server) options = options with { SavedDirSuffix = "Server" };
            Assert.True((await pipeline.PrepareLaunch(options)).IsNone);
            Assert.True(File.Exists(Path.Combine(source, "manual.pak")));
            Assert.False(plugin);
            fail = false;
            Assert.True((await pipeline.PrepareLaunch(options)).IsSome);
            Assert.True(plugin);
        }
        finally { Directory.Delete(root, true); }
    }
}
