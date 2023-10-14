//using System.Windows.Shapes;
using UnchainedLauncherCore.JsonModels;
using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using UnchainedLauncherCore;

namespace UnchainedLauncherGUI.ViewModels {
    // This isn't a view model in the normal sense that we hold on to a view of data and bind to it.
    // I just figure this is the "View" for the installer workflow.
    // TODO: Somehow generalize the updater and installer
    class InstallerViewModel {
        private static readonly ILog logger = LogManager.GetLogger(nameof(InstallerViewModel));

        /// <summary>
        /// Attempts to install the launcher at the given install directory.
        /// Returns true if installation was successful and we need a restart
        /// Returns false if installation was unsuccessful and we don't need a restart
        /// </summary>
        /// <param name="installDir"></param>
        /// <param name="installType"></param>
        /// <returns></returns>
        public static bool AttemptInstall(string installDir, InstallationType installType) {
            logger.Info($"Attempting to install launcher to {installDir} as {installType}");
            bool specific = installType != InstallationType.NotSet;
            string exeName = Process.GetCurrentProcess().ProcessName;
            if (exeName != "Chivalry2Launcher") {
                var messageLines = new List<string>() {
                    "Set Unchained Modloader as Default?",
                    "",
                    "Would you like to set the Unchained Modloader as your default launcher for Chivalry 2? This allows you to easily choose between vanilla Chivalry 2 and the modded Unchained version upon startup.",
                    "",
                    "Your original Chivalry 2 launcher will still be accessible and will be renamed to 'Chivalry2Launcher-ORIGINAL.exe'.",
                    "",
                    "Yes (Simple) - Replace Chivalry2Launcher.exe with the Unchained Launcher",
                    "",
                    "No (Advanced) - Move the Unchained Launcher in to the Chivalry 2 Directory, but Keep Chivalry2Launcher.exe as the default launcher.",
                    "",
                    "Cancel - Do not install the Unchained Launcher."
                };

                MessageBoxResult dialogResult = MessageBox.Show(
                    messageLines.Aggregate((a, b) => a + "\n" + b),
                    "Replace Current Launcher?", MessageBoxButton.YesNoCancel
                );
                logger.Info($"Replace Current Launcher? User Selects: {dialogResult}");

                if (dialogResult == MessageBoxResult.Yes ||
                        dialogResult == MessageBoxResult.No) {
                    string commandLinePass = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));

                    string? originalLauncherPath = GetLauncherOnPath(specific ? installDir : "Chivalry2Launcher.exe");
                    if (originalLauncherPath == null) {
                        MessageBox.Show("Unable to move: Failed to get the path to the original launcher");
                        return false;
                    }

                    string originalLauncherDir = Path.GetDirectoryName(originalLauncherPath) ?? ".";
                    originalLauncherDir = originalLauncherDir == "" ? "." : originalLauncherDir;

                    //MessageBox.Show(originalLauncherPath);
                    List<string> powershellCommand = new List<string>() {
                        $"Wait-Process -Id {Environment.ProcessId}",
                        $"Start-Sleep -Milliseconds 500"
                    };

                    var exeOutputPath = "";

                    if (dialogResult == MessageBoxResult.Yes) {
                        exeOutputPath = $"\\\"{originalLauncherDir}\\Chivalry2Launcher.exe\\\"";
                        powershellCommand.AddRange(new List<string>() {
                            $"Move-Item -Force \\\"{originalLauncherPath}\\\" \\\"{originalLauncherDir}\\Chivalry2Launcher-ORIGINAL.exe\\\"",
                            $"Copy-Item -Force \\\"{exeName}.exe\\\" {exeOutputPath}"
                        });
                    } else {
                        exeOutputPath = $"\\\"{originalLauncherDir}\\{exeName}.exe\\\"";
                        powershellCommand.AddRange(new List<string>() {
                            $"Copy-Item -Force \\\"{exeName}.exe\\\" {exeOutputPath}"
                        });
                    }

                    string hashPath = Path.Combine(originalLauncherDir, FilePaths.ModCachePath, "unchained-launcher.sha512");
                    powershellCommand.AddRange(new List<string>() {
                        $"$hashResult = Get-FileHash {exeOutputPath} -Algorithm SHA512",
                        $"$hashResult.Hash | Set-Content -Path \\\"{hashPath}\\\""
                    });

                    PowerShell.Run(powershellCommand);
                    logger.Info($"Launcher installed successfully.");
                    return true;
                }
            }
            logger.Info($"Already installed. (exe is named Chivalry2Launcher.exe)");
            return false;
        }

        private static string? SearchDirForLauncher(string dir) {
            string launcherDir = Path.GetDirectoryName(dir) ?? "";
            string launcherOriginalPath = Path.Combine(launcherDir, "Chivalry2Launcher-ORIGINAL.exe");
            string launcherDefaultPath = Path.Combine(launcherDir, "Chivalry2Launcher.exe");

            string hashPath = Path.Combine(launcherDir, FilePaths.ModCachePath, "unchained-launcher.sha512");

            if (File.Exists(hashPath)) {
                logger.Info("Hash file from previous install found. Checking current launcher against hash...");
                string hash = File.ReadAllText(hashPath).Trim().ToUpper();
                string currentHash = FileHelpers.Sha512(launcherDefaultPath).Trim().ToUpper();

                logger.Info("Launcher hash: " + currentHash);
                logger.Info("Cached hash: " + hash);

                if (hash == currentHash && File.Exists(launcherOriginalPath)) {
                    logger.Info("Hashes match. Using existing ORIGINAL launcher.");
                    return launcherOriginalPath;
                } else {
                    logger.Info("Hashes do not match or no ORIGINAL launcher exists. Update detected. Assuming Chivalry2Launcher.exe is the vanilla executable.");
                    return launcherDefaultPath;
                }
            } else {
                logger.Info("No hash file from previous install found.");
                if (File.Exists(launcherOriginalPath)) {
                    return launcherOriginalPath;
                } else if (File.Exists(launcherDefaultPath)) {
                    return launcherDefaultPath;
                } else {
                    return null;
                }
            }
        }

        private static string? GetLauncherOnPath(string dir) {
            //string? originalLauncherPath = SearchDirForLauncher("Chivalry2Launcher.exe");
            string? originalLauncherPath = SearchDirForLauncher(Path.Combine(dir, "Chivalry2Launcher.exe"));
            //try to find it as a relative path
            if (originalLauncherPath == null || !File.Exists(originalLauncherPath)) {
                do {
                    //MessageBox.Show("Starting file dialogue");
                    var filePicker = new Microsoft.Win32.OpenFileDialog {
                        Title = "Select chivalry2Launcher.exe in your chivalry 2 install folder",
                        Filter = "Executable file | *.exe",
                        Multiselect = false,
                        InitialDirectory = "C:\\Program Files (x86)\\Epic Games\\Games\\Chivalry2",
                        CheckFileExists = true
                    };

                    if (!(filePicker.ShowDialog() ?? false)) {
                        return null;
                    }

                    originalLauncherPath = SearchDirForLauncher(filePicker.FileName);

                    if (originalLauncherPath == null) {
                        MessageBox.Show($"`{filePicker.FileName}` is not a valid launcher, and" +
                            $"no valid launcher could be found in that folder. Please try again.");
                    }

                } while (originalLauncherPath == null);
            }

            return originalLauncherPath;

        }
    }
}
