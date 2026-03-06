using log4net;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Installer {
    public interface IUnchainedLauncherInstaller {
        /// <summary>
        /// Installs the launcher by downloading the latest release and replacing the current executable with the new one.
        /// </summary>
        /// <param name="targetDir"></param>
        /// <param name="release"></param>
        /// <param name="replaceCurrent">If true, closes the current executable and launches the installed executable with the same args used to launch the current executable.</param>
        /// <param name="logProgress">When set, reports all logs to the provided action
        /// <returns>
        /// Task of bool indicating success or failure
        /// </returns>
        public Task<bool> Install(DirectoryInfo targetDir, ReleaseTarget release, bool replaceCurrent, Func<string, Task>? logProgress = null);
    }

    public class UnchainedLauncherInstaller : IUnchainedLauncherInstaller {
        public static readonly ILog Logger = LogManager.GetLogger(nameof(UnchainedLauncherInstaller));

        private Action<int> EndProgram { get; }
        private readonly IVersionExtractor _versionExtractor;


        public UnchainedLauncherInstaller(Action<int> endProgram, IVersionExtractor? versionExtractor = null) {
            EndProgram = endProgram;
            _versionExtractor = versionExtractor ?? new FileInfoVersionExtractor();
        }

        /// <summary>
        /// Installs the launcher by downloading the latest release and replacing the current executable with the new one.
        /// </summary>
        /// <param name="targetDir"></param>
        /// <param name="release"></param>
        /// <param name="replaceCurrent">If true, closes the current executable and launches the installed executable with the same args used to launch the current executable.</param>
        /// <param name="logProgress">When set, reports all logs to the provided action
        /// <returns>
        /// Task of bool indicating success or failure
        /// </returns>
        public async Task<bool> Install(DirectoryInfo targetDir, ReleaseTarget release, bool replaceCurrent, Func<string, Task>? logProgress = null) {
            async Task log(string s) {
                if (logProgress != null) {
                    await logProgress!.Invoke(s);
                }

                Logger.Info(s);
            }
            
            try {
                var url =
                    (from releaseAssets in release.Assets
                     where releaseAssets.Name.Contains("Launcher.exe")
                     select releaseAssets.DownloadUrl).First();


                var fileName = $"UnchainedLauncher-{release.Version}.exe";
                var downloadFilePath = Path.Combine(targetDir.FullName, fileName);

                var currentPath = Environment.ProcessPath;
                var currentVersion = currentPath != null && File.Exists(currentPath) ? _versionExtractor.GetVersion(currentPath) : null;

                if (currentVersion.MatchesRelease(release.Version) && currentPath != null) {
                    await log($"✨ Current version matches selected version v{release.Version}.");
                    await log($"📑 Copying local executable \n    from {currentPath}\n    to {downloadFilePath}");
                    try {
                        File.Copy(currentPath, downloadFilePath, true);
                        await log("✅️ Successfully copied launcher executable.");
                    } catch (Exception ex) {
                        await log("❌ Failed to copy executable");
                        await log(ex.ToString());

                        return false;
                    }
                } else {
                    await log($"📥 Downloading release 'v{release.Version}'\n    from {url}\n    to {downloadFilePath}");

                    // We only want to download the Launcher executable, even if the release contains multiple assets
                    string? AssetFilter(ReleaseAsset asset) => (asset.Name.Contains("Launcher.exe") ? downloadFilePath : null);
                    var downloadResult = await HttpHelpers.DownloadReleaseTarget(release, AssetFilter, log);

                    if (!downloadResult) {
                        await log($"❌ Failed to download the launcher version {release.Version}.");
                        return false;
                    }
                }
                

                var launcherPath = Path.Combine(targetDir.FullName, FilePaths.LauncherPath);
                await MoveExistingLauncher(targetDir, log);

                if (replaceCurrent) {
                    var currentExecutablePath = Environment.ProcessPath ?? Path.Combine(targetDir.FullName, Process.GetCurrentProcess().ProcessName);

                    if (!currentExecutablePath.EndsWith(".exe")) {
                        currentExecutablePath += ".exe";
                    }

                    await log($"🔄 Replacing current launcher executable \"{currentExecutablePath}\" with downloaded launcher \"{downloadFilePath}\"");



                    var commandLinePass = string.Join(" ",
                        Environment.GetCommandLineArgs()
                            .Skip(1)
                            .ToList()
                            .Select(ArgumentEscaper.Escape)
                            );

                    var powershellCommand = new List<string>() {
                        $"Wait-Process -Id {Environment.ProcessId} -ErrorAction 'Ignore'",
                        $"Start-Sleep -Milliseconds 1000",
                        $"Move-Item -Force '{downloadFilePath}' '{currentExecutablePath}'",
                        $"Start-Sleep -Milliseconds 500",
                        $"& '{launcherPath}' {commandLinePass}"
                    };

                    var proc = PowerShell.Run(powershellCommand, createWindow: true);

                    await log("👋 Exiting current process to launch new launcher...");
                    EndProgram(0);
                }
                else {
                    await log($"📦 Replacing launcher \n    at {launcherPath} \n    with launcher from {downloadFilePath}");
                    File.Move(downloadFilePath, launcherPath, true);
                    await log($"✨ Successfully installed launcher version {release.Version}");
                }


                return true;
            }
            catch (Exception ex) {
                await log(ex.ToString());
                Logger.Error(ex);
            }

            return false;
        }

        private static async Task MoveExistingLauncher(DirectoryInfo targetDir, Func<string, Task> log) {
            var launcherPath = Path.Combine(targetDir.FullName, FilePaths.LauncherPath);
            var originalLauncherPath = Path.Combine(targetDir.FullName, FilePaths.OriginalLauncherPath);

            await log("🔍 Checking if the existing launcher needs to be moved...");

            // Only if the Product Name of the file at the launcher path is not the same as the current executable
            if (!File.Exists(launcherPath)) {
                return;
            }

            var launcherProductName = FileVersionInfo.GetVersionInfo(launcherPath)?.ProductName;
            var currentExecutablePath = Environment.ProcessPath;

            if (currentExecutablePath == null || !File.Exists(currentExecutablePath)) {
                throw new Exception("Failed to get the product name of the current executable. Aborting");
            }

            var currentExecutableProductName = FileVersionInfo.GetVersionInfo(currentExecutablePath)?.ProductName;

            if (currentExecutableProductName == null) {
                throw new Exception("Failed to get the product name of the current executable. Aborting");
            }

            if (launcherProductName != currentExecutableProductName) {
                await log($"📂 Existing launcher is not {currentExecutableProductName}. Moving existing launcher to {originalLauncherPath}");
                File.Move(launcherPath, originalLauncherPath, true);
            }
            else {
                await log("📝 Existing launcher is a modified launcher. Overwriting with version selected in the installer..");
            }
        }
    }
    public class MockInstaller : IUnchainedLauncherInstaller {
        public Task<bool> Install(DirectoryInfo targetDir, ReleaseTarget release, bool replaceCurrent, Func<string, Task>? logProgress = null) => Task.FromResult(true);

    }
}