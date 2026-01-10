using log4net;
using System.Diagnostics;
using System.Reflection;
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
        public Task<bool> Install(DirectoryInfo targetDir, ReleaseTarget release, bool replaceCurrent, Action<string>? logProgress = null);
    }

    public class UnchainedLauncherInstaller : IUnchainedLauncherInstaller {
        public static readonly ILog Logger = LogManager.GetLogger(nameof(UnchainedLauncherInstaller));

        private Action<int> EndProgram { get; }


        public UnchainedLauncherInstaller(Action<int> endProgram) {
            EndProgram = endProgram;
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
        public async Task<bool> Install(DirectoryInfo targetDir, ReleaseTarget release, bool replaceCurrent, Action<string>? logProgress = null) {
            var log = new Action<string>(s => {
                logProgress?.Invoke(s);
                Logger.Info(s);
            });

            try {
                var url =
                    (from releaseAssets in release.Assets
                     where releaseAssets.Name.Contains("Launcher.exe")
                     select releaseAssets.DownloadUrl).First();


                var fileName = $"UnchainedLauncher-{release.Version}.exe";
                var downloadFilePath = Path.Combine(targetDir.FullName, fileName);

                log($"Downloading release 'v{release.Version}'\n    from {url}\n    to {downloadFilePath}");

                // We only want to download the Launcher executable, even if the release contains multiple assets
                string? AssetFilter(ReleaseAsset asset) => (asset.Name.Contains("Launcher.exe") ? downloadFilePath : null);
                var downloadResult = await HttpHelpers.DownloadReleaseTarget(release, AssetFilter, log);

                if (!downloadResult) {
                    log($"Failed to download the launcher version {release.Version}.");
                    return false;
                }

                var launcherPath = Path.Combine(targetDir.FullName, FilePaths.LauncherPath);
                MoveExistingLauncher(targetDir, log);

                if (replaceCurrent) {
                    var currentExecutableName = Process.GetCurrentProcess().ProcessName;
                    var currentExecutablePath = Path.Combine(targetDir.FullName, currentExecutableName);

                    if (!currentExecutablePath.EndsWith(".exe")) {
                        currentExecutablePath += ".exe";
                    }

                    log($"Replacing current executable \"{currentExecutablePath}\" with downloaded launcher \"{downloadFilePath}\"");



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

                    log("Exiting current process to launch new launcher");
                    EndProgram(0);
                }
                else {
                    log($"Replacing launcher \n    at {launcherPath} \n    with downloaded launcher from {downloadFilePath}");
                    File.Move(downloadFilePath, launcherPath, true);
                    log($"Successfully installed launcher version {release.Version}");
                }


                return true;
            }
            catch (Exception ex) {
                log(ex.ToString());
                Logger.Error(ex);
            }

            return false;
        }

        private static void MoveExistingLauncher(DirectoryInfo targetDir, Action<string> log) {
            var launcherPath = Path.Combine(targetDir.FullName, FilePaths.LauncherPath);
            var originalLauncherPath = Path.Combine(targetDir.FullName, FilePaths.OriginalLauncherPath);

            log("Checking if the existing launcher needs to be moved...");

            // Only if the Product Name of the file at the launcher path is not the same as the current executable
            if (File.Exists(launcherPath)) {
                var launcherProductName = FileVersionInfo.GetVersionInfo(launcherPath)?.ProductName;
                var currentAssembly = Assembly.GetEntryAssembly();

                if (currentAssembly?.Location == null) {
                    throw new Exception("Failed to get the product name of the current executable. Aborting");
                }

                var currentExecutableProductName = FileVersionInfo.GetVersionInfo(currentAssembly!.Location)?.ProductName;

                if (currentExecutableProductName == null) {
                    throw new Exception("Failed to get the product name of the current executable. Aborting");
                }

                if (launcherProductName != currentExecutableProductName) {
                    log($"Existing launcher is not {currentExecutableProductName}. Moving existing launcher to {originalLauncherPath}");
                    File.Move(launcherPath, originalLauncherPath, true);
                }
                else {
                    log("Existing launcher is a modified launcher. Overwriting with version selected in the installer..");
                }
            }
        }
    }
    public class MockInstaller : IUnchainedLauncherInstaller {
        public Task<bool> Install(DirectoryInfo targetDir, ReleaseTarget release, bool replaceCurrent, Action<string>? logProgress = null) => Task.FromResult(true);

    }
}