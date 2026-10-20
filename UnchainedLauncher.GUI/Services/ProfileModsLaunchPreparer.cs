using LanguageExt;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.GUI.Services;

public sealed class ProfileModsLaunchPreparer(
    ModManager manager, IUserDialogueSpawner dialogue, string installation, string? localAppData = null)
    : IChivalry2LaunchPreparer<LaunchOptions> {
    public Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
        var assets = new ProfileAssetPaths(options.SavedDirSuffix.IfNone(ProfileAssetPaths.SharedSuffix), localAppData);
        var profileManager = assets.Rebind(manager, installation);
        // DLL selection and injection stay bound to the shared manager and plugin directory.
        IChivalry2LaunchPreparer<LaunchOptions> installer = new Chivalry2ModsInstaller(profileManager, dialogue);
        return installer
            .Sub(SigPreparer.Create(profileManager.PakDir, dialogue))
            .PrepareLaunch(options with { SavedDirSuffix = assets.Suffix });
    }
}