using LanguageExt;
using System;
using System.IO;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.GUI.Services;

public sealed class ProfileModsLaunchPreparer(
    ModManager manager, IUserDialogueSpawner dialogue, string installation, string? localAppData = null)
    : IChivalry2LaunchPreparer<LaunchOptions> {
    public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
        ProfileAssetPaths assets;
        try {
            assets = new ProfileAssetPaths(options.SavedDirSuffix.IfNone(ProfileAssetPaths.SharedSuffix), localAppData);
        }
        catch (ArgumentException e) {
            dialogue.DisplayMessage($"Could not prepare profile assets: {e.Message}\nLaunch cancelled.");
            return Option<LaunchOptions>.None;
        }
        IDisposable lease;
        try { lease = assets.AcquireLease(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            dialogue.DisplayMessage($"Could not prepare profile assets: {e.Message}\nLaunch cancelled.");
            return Option<LaunchOptions>.None;
        }
        using (lease) {
            var profileManager = assets.Rebind(manager, installation);
            // DLL selection and injection stay bound to the shared manager and plugin directory.
            IChivalry2LaunchPreparer<LaunchOptions> installer = new Chivalry2ModsInstaller(profileManager, dialogue);
            return await installer
                .Sub(SigPreparer.CreateForProfile(profileManager.PakDir, dialogue))
                .PrepareLaunch(options with { SavedDirSuffix = assets.Suffix });
        }
    }
}