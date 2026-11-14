using Semver;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.GUI.ViewModels.Installer;

namespace System.Windows {
    public enum Visibility { Visible, Hidden }
    public enum MessageBoxButton { YesNo }
    public enum MessageBoxResult { No, Yes }
    public static class MessageBox {
        public static readonly List<string> Messages = new();
        public static MessageBoxResult Result { get; set; }
        public static MessageBoxResult Show(string message, string? title = null, MessageBoxButton button = default) {
            Messages.Add(message);
            return Result;
        }
    }
}

namespace Microsoft.Win32 {
    public class OpenFileDialog {
        public string Title { get; set; } = "";
        public string FileName => Path.Combine(Path.GetTempPath(), "installer-test-game", "Chivalry2Launcher.exe");
        public bool? ShowDialog() => true;
    }
}

namespace UnchainedLauncher.GUI.Views.Installer {
    public static class LaunchUnchainedLauncherWindow {
        public static InstallationTargetViewModel[] Targets { get; private set; } = Array.Empty<InstallationTargetViewModel>();
        public static void Show(IEnumerable<InstallationTargetViewModel> targets) => Targets = targets.ToArray();
    }
}

namespace UnchainedLauncher.GUI.Services {
    public static class MarkdownRenderer {
        public static string RenderHtml(string input) => input;
    }
}

namespace UnchainedLauncher.Core.Utilities {
    public static class FilePaths {
        public const string LauncherPath = "Chivalry2Launcher.exe";
    }
}

namespace UnchainedLauncher.Core.Services {
    public class FileInfoVersionExtractor : IVersionExtractor {
        public SemVersion? GetVersion(string input) => null;
    }
}

namespace UnchainedLauncher.Core.Services.Installer {
    // Mirror the production service contracts; no filesystem discovery or installs.
    public interface IChivalry2InstallationFinder {
        bool IsValidInstallation(DirectoryInfo path);
        bool IsSteamDir(DirectoryInfo path);
        bool IsEGSDir(DirectoryInfo path);
        DirectoryInfo? FindSteamDir();
        DirectoryInfo? FindEGSDir();
    }

    public class Chivalry2InstallationFinder : IChivalry2InstallationFinder {
        public DirectoryInfo? Steam { get; set; }
        public int Scans { get; private set; }
        public int ScanThread { get; private set; }
        public bool ThrowOnScan { get; set; }
        public bool IsValidInstallation(DirectoryInfo path) => true;
        public bool IsSteamDir(DirectoryInfo path) => true;
        public bool IsEGSDir(DirectoryInfo path) => false;
        public DirectoryInfo? FindSteamDir() {
            Scans++;
            ScanThread = Environment.CurrentManagedThreadId;
            if (ThrowOnScan) throw new IOException("Discovery failed");
            return Steam;
        }
        public DirectoryInfo? FindEGSDir() => null;
    }

    public interface IUnchainedLauncherInstaller {
        Task<bool> Install(DirectoryInfo targetDir, ReleaseTarget release, bool replaceCurrent, Func<string, Task>? logProgress = null);
    }

    public class MockInstaller : IUnchainedLauncherInstaller {
        public Queue<bool> Results { get; } = new();
        public List<DirectoryInfo> Calls { get; } = new();
        public bool ThrowOnInstall { get; set; }
        public Task<bool> Install(DirectoryInfo targetDir, ReleaseTarget release, bool replaceCurrent, Func<string, Task>? logProgress = null) {
            Calls.Add(targetDir);
            if (ThrowOnInstall) throw new IOException("Installation failed");
            return Task.FromResult(Results.Count == 0 || Results.Dequeue());
        }
    }
}