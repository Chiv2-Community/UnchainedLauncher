using CommunityToolkit.Mvvm.Input;
using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class HelpVM(
        SettingsVM SettingsVM,
        IUnchainedLauncherInstaller Installer,
        IReleaseLocator UnchainedReleaseLocator,
        IPakDir PakDir,
        IUserDialogueSpawner UserDialogueSpawner,
        Action<int> ExitProgram) {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(HelpVM));

        [RelayCommand]
        private void OpenDiscord() {
            Process.Start(new ProcessStartInfo("https://discord.gg/chiv2unchained") { UseShellExecute = true });
        }

        [RelayCommand]
        private void OpenWiki() {
            Process.Start(new ProcessStartInfo("https://unchained.wiki") { UseShellExecute = true });
        }


        [RelayCommand]
        private void UninstallLauncher() {
            const string originalPath = FilePaths.OriginalLauncherPath;
            const string launcherPath = FilePaths.LauncherPath;

            var originalExists = File.Exists(originalPath);
            var message = new List<string>() {
                "Are you sure? This will disable all mods, reset all settings to their defaults, and uninstall the launcher. This will delete the following:",
                "* All files in .mod_cache",
                "* All files in TBL\\Binaries\\Win64\\Plugins",
                "* All non-vanilla paks in TBL\\Content\\Paks.",
                "",
                originalExists
                    ? "IMPORTANT: Wait at least 1 second and then launch normally."
                    : "IMPORTANT: The original launcher is not present. You will need to verify game files after this so it can be re-downloaded."
            }.Aggregate((accumulator, next) => accumulator + "\n" + next);

            var choice = UserDialogueSpawner.DisplayYesNoMessage(message, "Really uninstall?");
            Logger.Info($"Are you sure? User selects: {choice}");

            if (choice == UserDialogueChoice.No) return;

            CleanUpInstallation_actions();
            const string replaceCommand = $@" 
                while (Test-Path '{originalPath}' -PathType Leaf) {{
                    try {{
                        Move-Item -Path '{originalPath}' -Destination '{launcherPath}' -Force;
                        break;
                    }} catch {{
                        Start-Sleep -Milliseconds 200;
                        Write-Error $_.Exception.Message;
                    }}
                }};";

            const string deleteCommand = $@" 
                while (Test-Path '{launcherPath}' -PathType Leaf) {{
                    try {{
                        Remove-Item -Path '{launcherPath}' -Force;
                        break;
                    }} catch {{
                        Start-Sleep -Milliseconds 200;
                        Write-Error $_.Exception.Message;
                    }}
                }};";

            // flatten the script so newlines don't cause problems
            var flatReplaceCommand = Regex.Replace(
                replaceCommand,
                @"[\n\r]+\s*",
                " ",
                RegexOptions.Singleline
            );
            // flatten the script so newlines don't cause problems
            var flatDeleteCommand = Regex.Replace(
                deleteCommand,
                @"[\n\r]+\s*",
                " ",
                RegexOptions.Singleline
            );

            // if there's no original, then there's nothing we can do but delete ourselves
            if (originalExists) {
                PowerShell.Run(
                    new List<string>() { $"Wait-Process -Id {Process.GetCurrentProcess().Id}", flatReplaceCommand },
                    false).Dispose();
            }
            else {
                PowerShell.Run(
                    new List<string>() { $"Wait-Process -Id {Process.GetCurrentProcess().Id}", flatDeleteCommand },
                    false).Dispose();
            }

            ExitProgram(0);
        }

        private void CleanUpInstallation_actions() {
            FileHelpers.DeleteDirectory(FilePaths.ModCachePath);
            FileHelpers.DeleteDirectory(FilePaths.PluginDir);
            PakDir.Reset();
        }

        [RelayCommand]
        private void CleanUpInstallation() {
            Logger.Info("CleanUpInstallation button clicked.");
            var message = new List<string>() {
                "Are you sure? This will disable all mods and reset all settings to their defaults. This will delete the following:",
                "* All files in .mod_cache",
                "* All files in TBL\\Binaries\\Win64\\Plugins",
                "* All non-vanilla paks in TBL\\Content\\Paks.",
                "",
                "After deleting, the launcher will restart itself."
            }.Aggregate((accumulator, next) => accumulator + "\n" + next);

            var choice = UserDialogueSpawner.DisplayYesNoMessage(message, "Really clean up installation?");
            Logger.Info($"Are you sure? User selects: {choice}");

            if (choice == UserDialogueChoice.No) return;
            CleanUpInstallation_actions();
            RestartLauncher();
        }

        private void RestartLauncher() {
            Logger.Info("Restarting launcher...");

            var currentExecutableName = Process.GetCurrentProcess().ProcessName;

            var cliPass =
                string.Join(" ",
                    Environment.GetCommandLineArgs()
                        .Skip(1)
                        .ToList()
                        .Select(ArgumentEscaper.Escape)
                );

            var commandLinePass = string.Join(" ", cliPass);
            var powershellCommands = new List<string>() {
                $"Wait-Process -Id {Environment.ProcessId} -ErrorAction 'Ignore'",
                $"Start-Sleep -Milliseconds 500",
                $".\\{currentExecutableName} {commandLinePass}"
            };

            PowerShell.Run(powershellCommands);
            UserDialogueSpawner.DisplayMessage("The launcher will now restart. No further action must be taken.");

            Logger.Info("Closing");
            ExitProgram(0);
        }

        [RelayCommand]
        public async Task CheckForUpdate() {
            Logger.Info("Checking for updates...");

            var latestRelease = await UnchainedReleaseLocator.GetLatestRelease(SettingsVM.Version.IsPrerelease);
            if (latestRelease == null) {
                UserDialogueSpawner.DisplayMessage("Failed to check for updates. Check the logs for more details.");
                return;
            }

            if (latestRelease.Version.ComparePrecedenceTo(SettingsVM.Version) > 0) {
                Logger.Info($"Latest version: {latestRelease.Version}, Current version: {SettingsVM.CurrentVersion}");
                await ChangeVersion(latestRelease);
            }
            else {
                UserDialogueSpawner.DisplayMessage("You are currently running the latest version.");
            }
        }

        private async Task ChangeVersion(ReleaseTarget release) {
            var dialogResult =
                UserDialogueSpawner.DisplayUpdateMessage(
                    "Chivalry 2 Unchained Launcher Update",
                    "Update the Unchained Launcher?",
                    "Yes",
                    "No",
                    null,
                    new DependencyUpdate("Launcher", SettingsVM.CurrentVersion, release.Version.ToString(), release.PageUrl, "")
                );

            switch (dialogResult) {
                case UserDialogueChoice.No:
                    Logger.Info("User chose not to update.");
                    return;
                case UserDialogueChoice.Yes:
                    Logger.Info("User chose to update.");
                    await Installer.Install(new DirectoryInfo(Environment.CurrentDirectory), release, true,
                        (message) => Task.Run(() => Logger.Info(message)));
                    break;
                case UserDialogueChoice.Cancel:
                case null:
                default:
                    MessageBox.Show("This should not be possible. Please report.");
                    throw new Exception("This should be impossible. Please report.");
            }
        }
    }
}