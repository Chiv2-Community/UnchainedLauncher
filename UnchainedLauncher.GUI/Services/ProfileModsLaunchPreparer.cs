using LanguageExt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.GUI.Services;

public sealed class ProfileModsLaunchPreparer(
    ModManager manager, IUserDialogueSpawner dialogue, string installation, string? localAppData = null,
    IChivalry2LaunchPreparer<LaunchOptions>? precedingDllPreparer = null)
    : IChivalry2LaunchPreparer<LaunchOptions> {
    public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
        ProfileAssetPaths assets;
        try {
            assets = new ProfileAssetPaths(options.SavedDirSuffix.IfNone(ProfileAssetPaths.SharedSuffix), localAppData);
        }
        catch (ArgumentException e) {
            return Cancel(e);
        }
        var profiles = precedingDllPreparer is null ? new[] { assets }
            : new[] { assets, new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix, localAppData) };
        var leases = new List<IDisposable>();
        try {
            try {
                foreach (var profile in profiles.DistinctBy(p => p.Root, StringComparer.OrdinalIgnoreCase)
                             .OrderBy(p => p.Root, StringComparer.OrdinalIgnoreCase))
                    leases.Add(profile.AcquireLease());
                Directory.CreateDirectory(assets.Paks);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
                return Cancel(e);
            }
            options = options with { SavedDirSuffix = assets.Suffix };
            if (precedingDllPreparer is not null) {
                var prepared = await precedingDllPreparer.PrepareLaunch(options);
                if (prepared.IsNone) return Option<LaunchOptions>.None;
                options = prepared.IfNone(options);
            }
            ModManager profileManager;
            try { profileManager = assets.Rebind(manager, installation); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
                return Cancel(e);
            }
            // DLL selection and injection stay bound to the shared manager and plugin directory.
            IChivalry2LaunchPreparer<LaunchOptions> installer = new Chivalry2ModsInstaller(profileManager, dialogue);
            return await installer
                .Sub(SigPreparer.CreateForProfile(profileManager.PakDir, dialogue))
                .PrepareLaunch(options);
        }
        finally {
            for (var i = leases.Count - 1; i >= 0; i--) leases[i].Dispose();
        }
    }

    private Option<LaunchOptions> Cancel(Exception error) {
        dialogue.DisplayMessage($"Could not prepare profile assets: {error.Message}\nLaunch cancelled.");
        return Option<LaunchOptions>.None;
    }
}