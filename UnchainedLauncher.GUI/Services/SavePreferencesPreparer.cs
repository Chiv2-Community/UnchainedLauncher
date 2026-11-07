using LanguageExt;
using log4net;
using System;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.GUI.Services;

public sealed class SavePreferencesPreparer(Action savePreferences, IUserDialogueSpawner dialogue) : IChivalry2LaunchPreparer<LaunchOptions> {
    private static readonly ILog Log = LogManager.GetLogger(typeof(SavePreferencesPreparer));

    public Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
        try {
            savePreferences();
            return Task.FromResult(Option<LaunchOptions>.Some(options));
        }
        catch (Exception ex) {
            Log.Error("Failed to save preferences before launch.", ex);
            dialogue.DisplayMessage($"Could not save launcher preferences. Launch cancelled before downloads or pak recovery.\n{ex.Message}\nCheck file permissions and available disk space, then try again.");
            return Task.FromResult(Option<LaunchOptions>.None);
        }
    }
}