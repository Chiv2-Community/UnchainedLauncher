using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Services.Dlls;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    public sealed class DllLaunchPreparer(DllManager manager, IUserDialogueSpawner dialogue) : IChivalry2LaunchPreparer<LaunchOptions> {
        public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
            try {
                var server = options.ServerLaunchOptions.IsSome;
                if (options.CheckForDependencyUpdates) {
                    try { await manager.RefreshAsync(); }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
                        LogManager.GetLogger(typeof(DllLaunchPreparer)).Warn("DLL refresh unavailable; retaining cached approvals.", ex);
                    }
                    foreach (var (current, latest) in manager.GetUpdateCandidates(server, options.AllowUnstablePluginReleases)) {
                        var update = new DependencyUpdate(latest.DllFileName ?? latest.Name + ".dll", current.Version, latest.Version,
                            $"https://github.com/{latest.Id}/releases/tag/{latest.Tag ?? latest.Version}", latest.Info?.Description ?? "Native plugin");
                        if (dialogue.DisplayUpdateMessage("Update plugin", $"Would you like to update {latest.Info?.Name ?? latest.Id}?",
                            "yes", "no", null, update) == UserDialogueChoice.Yes)
                            manager.SetEnabled(latest, true);
                    }
                }
                await manager.PrepareAsync(server: server, allowDownloads: options.CheckForDependencyUpdates);
                return options;
            }
            catch (Exception ex) {
                LogManager.GetLogger(typeof(DllLaunchPreparer)).Error("DLL validation failed", ex);
                dialogue.DisplayMessage($"DLL validation failed: {ex.Message}");
                return LanguageExt.Prelude.None;
            }
        }
    }
}
