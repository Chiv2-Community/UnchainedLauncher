using System;
using System.IO;
using System.Threading.Tasks;
using LanguageExt;
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
        var sameProfile = Path.GetFullPath(manager.PakDir.DirPath).Equals(
            Path.GetFullPath(assets.Paks), StringComparison.OrdinalIgnoreCase);
        var profileManager = manager.WithPakDir(assets.CreatePakDir(installation,
            sameProfile && manager.PakDir is PakDir existing ? existing.ManagedPaks : []));
        // DLL selection and injection stay bound to the shared manager and plugin directory.
        IChivalry2LaunchPreparer<LaunchOptions> installer = new Chivalry2ModsInstaller(profileManager, dialogue);
        return installer
            .Sub(SigPreparer.Create(profileManager.PakDir, dialogue))
            .PrepareLaunch(options with { SavedDirSuffix = assets.Suffix });
    }
}
