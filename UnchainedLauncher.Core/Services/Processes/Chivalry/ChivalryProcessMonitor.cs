using log4net;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public interface IChivalryProcessWatcher {
        /// <summary>
        /// Given an initial started process (which may be an anti-cheat bootstrapper),
        /// attempt to resolve and return the actual Chivalry 2 game process.
        /// Returns null if not found within a short polling window.
        /// </summary>
        Task<Process?> ResolveActualProcess(Process initial);

        /// <summary>
        /// Whether an exit code should be treated as acceptable/non-error.
        /// </summary>
        bool IsAcceptableExitCode(int code);

        /// <summary>
        /// Target process name without extension.
        /// </summary>
        string TargetProcessNameNoExt { get; }

        /// <summary>
        /// Resolve the actual game process (handles middle-man) and attach an exit callback.
        /// Returns true if the callback was attached; false if resolution failed.
        /// </summary>
        Task<bool> OnExit(Process initial, Action<int, bool> onExit);
    }

    public class ChivalryProcessWatcher : IChivalryProcessWatcher {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ChivalryProcessWatcher));

        public string TargetProcessNameNoExt => "Chivalry2-Win64-Shipping";

        private static readonly HashSet<int> AcceptableExitCodes = new() {
            0,                // Normal Exit
            -1073741510       // Exit via DLL Window
        };

        public bool IsAcceptableExitCode(int code) => AcceptableExitCodes.Contains(code);

        public async Task<Process?> ResolveActualProcess(Process initial) {
            try {
                var name = initial.ProcessName;
                var isTarget = string.Equals(name, TargetProcessNameNoExt, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(name, TargetProcessNameNoExt + ".exe", StringComparison.OrdinalIgnoreCase);

                if (isTarget) return initial;

                Logger.Info($"EAC MiddleMan ({name}) detected... Polling for Chiv2.");

                // Poll briefly for the actual game process after the middle-man exits
                for (var i = 0; i < 50; i++) { // ~25 seconds total
                    var candidates = Process.GetProcessesByName(TargetProcessNameNoExt);
                    var gameProc = candidates
                        .OrderByDescending(c => {
                            try { return c.StartTime; }
                            catch { return DateTime.MinValue; }
                        })
                        .FirstOrDefault();
                    if (gameProc is not null) return gameProc;
                    await Task.Delay(500);
                }

                Logger.Warn($"Failed to locate actual vanilla game process '{TargetProcessNameNoExt}'.");
                return null;
            }
            catch (Exception ex) {
                Logger.Warn("Failed to poll for game process", ex);
                return null;
            }
        }

        public async Task<bool> OnExit(Process initial, Action<int, bool> onExit) {
            var resolved = await ResolveActualProcess(initial);
            if (resolved is null) {
                Logger.Warn($"Failed to locate actual vanilla game process '{TargetProcessNameNoExt}'.");
                return false;
            }
 
            try { resolved.EnableRaisingEvents = true; }
            catch (Exception ex) {
                Logger.Warn("Failed to enable process exit events", ex);
                return false;
            }

            resolved.Exited += (_, _) => {
                var code = resolved.ExitCode;
                var ok = IsAcceptableExitCode(code);
                Logger.Info($"Chivalry 2 exited. ({code}) acceptable={ok}");
                try { onExit(code, ok); } catch (Exception cbEx) { Logger.Warn("OnExit callback threw", cbEx); }
            };

            Logger.Debug($"Attached exit handler to process {resolved.Id} ({resolved.ProcessName})");
            return true;
        }
    }
}
