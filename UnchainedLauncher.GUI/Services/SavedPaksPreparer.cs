using LanguageExt;
using log4net;
using System;
using System.IO;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.GUI.Services;

public sealed class SavedPaksPreparer(IPakDir pakDir, IUserDialogueSpawner dialogue) : IChivalry2LaunchPreparer<LaunchOptions> {
    private static readonly ILog Log = LogManager.GetLogger(typeof(SavedPaksPreparer));

    public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
        try {
            if (pakDir is PakDir directory)
                await directory.MigrateSavedPaks(options.SavedDirSuffix.IfNone("Unchained"));
            return Option<LaunchOptions>.Some(options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Log.Error($"Could not move Saved paks to '{Path.GetFullPath(pakDir.DirPath)}'. Launch cancelled.", ex);
            dialogue.DisplayMessage($"Could not move Saved paks. Launch cancelled. Resolve the conflict or file permissions, then retry.\n{ex.Message}");
            return Option<LaunchOptions>.None;
        }
    }
}