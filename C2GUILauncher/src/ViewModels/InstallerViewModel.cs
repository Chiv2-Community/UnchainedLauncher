//using System.Windows.Shapes;
using C2GUILauncher.JsonModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace C2GUILauncher.ViewModels {
    // This isn't a view model in the normal sense that we hold on to a view of data and bind to it.
    // I just figure this is the "View" for the installer workflow.
    // TODO: Somehow generalize the updater and installer
    class InstallerViewModel {

        /// <summary>
        /// Attempts to install the launcher at the given install directory.
        /// Returns true if installation was successful and we need a restart
        /// Returns false if installation was unsuccessful and we don't need a restart
        /// </summary>
        /// <param name="installDir"></param>
        /// <param name="installType"></param>
        /// <returns></returns>
        public static bool AttemptInstall(string installDir, InstallationType installType) {
            bool specific = installType != InstallationType.NotSet;
            string exeName = Process.GetCurrentProcess().ProcessName;
            if (exeName != "Chivalry2Launcher") {
                MessageBoxResult dialogResult = MessageBox.Show(
                   $"This program is not currently running in place of the default Chivalry 2 launcher.\n\n" +
                   $"Do you want this launcher to move itself in place of the Chivalry 2 launcher? The " +
                   $"default launcher will remain as 'Chivalry2Launcher-ORIGINAL.exe'\n\n" +
                   $"This will make the launcher start automatically when you launch via Epic Games or" +
                   $"Steam.\n\nDoing this is required if you are playing on Epic!",
                   "Replace launcher?", MessageBoxButton.YesNoCancel);

                if (dialogResult == MessageBoxResult.Yes ||
                        dialogResult == MessageBoxResult.No) {
                    Process pwsh = new Process();
                    pwsh.StartInfo.FileName = "powershell.exe";
                    string commandLinePass = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));

                    string? originalLauncherPath = GetLauncherOnPath(specific ? installDir : "Chivalry2Launcher.exe");
                    if (originalLauncherPath == null) {
                        MessageBox.Show("Unable to move: Failed to get the path to the original launcher");
                        return false;
                    }

                    string originalLauncherDir = Path.GetDirectoryName(originalLauncherPath) ?? ".";
                    if (originalLauncherDir == "") {
                        originalLauncherDir = ".";
                    }
                    //MessageBox.Show(originalLauncherPath);
                    string powershellCommand;
                    if (dialogResult == MessageBoxResult.Yes) {
                        powershellCommand =
                            $"Wait-Process -Id {Environment.ProcessId}; " +
                            $"Start-Sleep -Milliseconds 500; " +
                            $"Move-Item -Force \\\"{originalLauncherPath}\\\" \\\"{originalLauncherDir}\\Chivalry2Launcher-ORIGINAL.exe\\\"; " +
                            $"Move-Item -Force \\\"{exeName}.exe\\\" \\\"{originalLauncherDir}\\Chivalry2Launcher.exe\\\"; " +
                            $"Start-Sleep -Milliseconds 500; " +
                            $"Start-Process \\\"{originalLauncherDir}\\Chivalry2Launcher.exe\\\" -WorkingDirectory \\\"{originalLauncherDir}\\\" {commandLinePass}";
                    } else {
                        powershellCommand =
                            $"Wait-Process -Id {Environment.ProcessId}; " +
                            $"Start-Sleep -Milliseconds 500; " +
                            $"Move-Item -Force \\\"{exeName}.exe\\\" \\\"{originalLauncherDir}\\{exeName}.exe\\\"; " +
                            $"Start-Sleep -Milliseconds 500; " +
                            $"Start-Process \\\"{originalLauncherDir}\\{exeName}.exe\\\" -WorkingDirectory \\\"{originalLauncherDir}\\\" {commandLinePass}";
                    }
                    //MessageBox.Show(powershellCommand);
                    pwsh.StartInfo.Arguments = $"-Command \"{powershellCommand}\"";
                    pwsh.StartInfo.CreateNoWindow = true;
                    pwsh.Start();
                    return true;
                }
            }
            return false;
        }

        private static string? SearchDirForLauncher(string dir) {
            string launcherDir = Path.GetDirectoryName(dir) ?? "";
            string launcherOriginalPath = Path.Combine(launcherDir, "Chivalry2Launcher-ORIGINAL.exe");
            string launcherDefaultPath = Path.Combine(launcherDir, "Chivalry2Launcher.exe");

            if (File.Exists(launcherOriginalPath)) {
                return launcherOriginalPath;
            } else if (File.Exists(launcherDefaultPath)) {
                return launcherDefaultPath;
            } else {
                return null;
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
