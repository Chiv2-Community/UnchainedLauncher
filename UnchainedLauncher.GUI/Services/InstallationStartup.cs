using System;
using System.IO;
using UnchainedLauncher.Core.Services.Processes;

namespace UnchainedLauncher.GUI.Services {
    public static class InstallationStartup {
        public static bool TryAcquire(out InstallationInstanceGuard? guard, out string? error, DirectoryInfo? root = null) {
            try {
                if (InstallationInstanceGuard.TryAcquire(root ?? new DirectoryInfo(Environment.CurrentDirectory), out guard)) {
                    error = null;
                    return true;
                }
                error = "Unchained Launcher is already running for this installation. Close it before trying again.";
            }
            catch (Exception) {
                guard = null;
                error = "Cannot protect this installation. Check that its folder is accessible, then try again.";
            }
            return false;
        }
    }
}