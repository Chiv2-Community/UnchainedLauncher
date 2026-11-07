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

    public Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
        try {
            SavedPakMigration.Migrate(pakDir.DirPath, SavedPakMigration.SourceDirectories(options.SavedDirSuffix.IfNone("Unchained")));
            return Task.FromResult(Option<LaunchOptions>.Some(options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Log.Error($"Could not move Saved paks to Content/Paks '{Path.GetFullPath(pakDir.DirPath)}'. Launch cancelled.", ex);
            dialogue.DisplayMessage($"Could not move Saved paks to Content/Paks. Launch cancelled. Resolve the conflict or file permissions, then retry.\n{ex.Message}");
            return Task.FromResult(Option<LaunchOptions>.None);
        }
    }
}