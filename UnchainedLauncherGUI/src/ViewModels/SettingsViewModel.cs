﻿using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.Core.JsonModels;
using CommunityToolkit.Mvvm.Input;
using log4net;
using Octokit;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core;
using System.Threading.Tasks;
using LanguageExt.Common;
using UnchainedLauncher.GUI.Views;
using LanguageExt;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public class SettingsViewModel : IDisposable {
        private static readonly ILog logger = LogManager.GetLogger(nameof(SettingsViewModel));
        private static readonly Version version = Assembly.GetExecutingAssembly().GetName().Version!;

        private static readonly string SettingsFilePath = $"{FilePaths.ModCachePath}\\unchained_launcher_settings.json";

        private MainWindow Window { get; }

        public InstallationType InstallationType { get; set; }
        public bool EnablePluginAutomaticUpdates { get; set; }
        public string AdditionalModActors { get; set; }
        public string ServerBrowserBackend { get; set; }

        public string _cliArgs;
        public string CLIArgs {
            get { return _cliArgs; }
            set {
                if (value != _cliArgs) {
                    CLIArgsModified = true;
                }
                _cliArgs = value;
            }
        }
        public bool CLIArgsModified { get; set; }
        public string CurrentVersion {
            get => "v" + version.ToString(3);
        }

        public ICommand CheckForUpdateCommand { get; }
        public ICommand CleanUpInstallationCommand { get; }

        public static IEnumerable<InstallationType> AllInstallationTypes {
            get { return Enum.GetValues(typeof(InstallationType)).Cast<InstallationType>(); }
        }

        public FileBackedSettings<LauncherSettings> LauncherSettings { get; set; }

        public SettingsViewModel(MainWindow window, InstallationType installationType, bool enablePluginAutomaticUpdates, string additionalModActors, string serverBrowserBackend, FileBackedSettings<LauncherSettings> launcherSettings, string cliArgs) {
            InstallationType = installationType;
            EnablePluginAutomaticUpdates = enablePluginAutomaticUpdates;
            AdditionalModActors = additionalModActors;
            LauncherSettings = launcherSettings;
            ServerBrowserBackend = serverBrowserBackend;

            _cliArgs = cliArgs;
            CLIArgsModified = false;

            CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdate);
            CleanUpInstallationCommand = new RelayCommand(CleanUpInstallation);

            this.Window = window;
        }

        public static SettingsViewModel LoadSettings(MainWindow window) {
            var cliArgsList = Environment.GetCommandLineArgs();
            var cliArgs = cliArgsList.Length > 1 ? Environment.GetCommandLineArgs().Skip(1).Aggregate((x, y) => x + " " + y) : "";

            var fileBackedSettings = new FileBackedSettings<LauncherSettings>(SettingsFilePath);

            var loadedSettings = fileBackedSettings.LoadSettings();

            return new SettingsViewModel(
                window,
                loadedSettings?.InstallationType ?? InstallationTypeUtils.AutoDetectInstallationType(),
                loadedSettings?.EnablePluginAutomaticUpdates ?? true,
                loadedSettings?.AdditionalModActors ?? "",
                loadedSettings?.ServerBrowserBackend ?? "https://servers.polehammer.net",
                fileBackedSettings,
                cliArgs
            );
        }

        public void SaveSettings() {
            LauncherSettings.SaveSettings(
                new LauncherSettings(InstallationType, EnablePluginAutomaticUpdates, AdditionalModActors, ServerBrowserBackend)
            );
        }


        // TODO: This function knows too much.
        //       It should be telling the mod manager and other things which
        //       manage files to clean themselves up, rather than this class
        //       being aware of everything.
        private void CleanUpInstallation() {
            logger.Info("CleanUpInstallation button clicked.");
            var message = new List<string>() {
                "Are you sure? This will disable all mods and reset all settings to their defaults. This will delete the following:",
                "* All files in .mod_cache",
                "* All files in TBL\\Binaries\\Win64\\Plugins",
                "* All non-vanilla paks in TBL\\Content\\Paks.",
                "",
                "After deleting, the launcher will restart itself."
            }.Aggregate((accumulator, next) => accumulator + "\n" + next);

            var choice = MessageBox.Show(message, "Really clean up installation?", MessageBoxButton.YesNo);
            logger.Info($"Are you sure? User selects: {choice}");

            if (choice == MessageBoxResult.No) return;

            FileHelpers.DeleteDirectory(FilePaths.ModCachePath);
            FileHelpers.DeleteDirectory(FilePaths.PluginDir); 

            var vanillaPaks = new List<string>() { "pakchunk0-WindowsNoEditor.pak" };
            var filePaths =
                Directory
                    .GetFiles(FilePaths.PakDir)
                    .Where(pakName => {
                        if (vanillaPaks.Any(vanillaPak => pakName.EndsWith(vanillaPak))) {
                            logger.Info($"Skipping vanilla pak {pakName}");
                            return false;
                        }

                        return true;
                    });

            FileHelpers.DeleteFiles(filePaths);

            RestartLauncher();
        }

        private void RestartLauncher() {
            logger.Info("Restarting launcher...");

            var currentExecutableName = Process.GetCurrentProcess().ProcessName;

            var commandLinePass = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
            var powershellCommands = new List<string>() {
                $"Wait-Process -Id {Environment.ProcessId}",
                $"Start-Sleep -Milliseconds 500",
                $".\\{currentExecutableName} {commandLinePass}"
            };

            PowerShell.Run(powershellCommands);
            MessageBox.Show("The launcher will now restart. No further action must be taken.");

            logger.Info("Closing");
            Window.DisableSaveSettings = true;
            Window.Close(); //close the program
        }

        // TODO: Somehow generalize the updater and installer
        private async Task CheckForUpdate() {
            var github = new GitHubClient(new ProductHeaderValue("UnchainedLauncher"));

            var repoCall = github.Repository.Release.GetLatest(667470779); //UnchainedLauncher repo id
            Release latestInfo;
            try {
                latestInfo = await repoCall;
            } catch(Exception e) {
                logger.Error("Failed to connect to github to retrieve latest version information", e);
                MessageBox.Show("Failed to check for updates.");
                return;
            }

            string tagName = latestInfo.TagName;

            logger.Info($"Latest version tag: {tagName}");

            Version? latest = null;
            try {
                var versionString = tagName;
                if(versionString.StartsWith("v")) {
                    versionString = versionString[1..];
                }
                latest = Version.Parse(versionString);
            } catch {
                // If parsing fails, just say they're equal.
                logger.Info($"Failed to parse version tag {tagName}");
                MessageBox.Show("Failed to check for updates.");
                return;
            }

            logger.Info($"Latest version: {tagName}, Current version: {CurrentVersion}");
            //if latest is newer than current version
            if (latest > version) {
                Option<MessageBoxResult> dialogResult = None;
                await Window.Dispatcher.BeginInvoke(delegate () {
                    dialogResult =
                        UpdatesWindow.Show("Chivalry 2 Unchained Launcher Update", "Update the Unchained Launcher?", "Yes", "No", null, List(
                            new DependencyUpdate("Launcher", Some(CurrentVersion), latest.ToString(), latestInfo.Url, "")
                        ));
                });

                if (dialogResult.Contains(MessageBoxResult.No)) {
                    logger.Info("User chose not to update.");
                    return;
                } else if (dialogResult.Contains(MessageBoxResult.Yes)) {
                    logger.Info("User chose to update.");
                    try {
                        var url = latestInfo.Assets.Where(
                            a => a.Name.Contains("Launcher.exe") //find the launcher exe
                        ).First().BrowserDownloadUrl; //get the download URL

                        var currentExecutableName = Process.GetCurrentProcess().ProcessName;

                        if(!currentExecutableName.EndsWith(".exe")) {
                            currentExecutableName += ".exe";
                        }

                        var newDownloadTask = HttpHelpers.DownloadFileAsync(url, "UnchainedLauncher-update.exe");
                        string exePath = Assembly.GetExecutingAssembly().Location;
                        string exeDir = Path.GetDirectoryName(exePath) ?? "";

                        newDownloadTask.Task.Wait();
                        if (!repoCall.IsCompletedSuccessfully) {
                            logger.Error("Failed to download the new version", newDownloadTask?.Task.Exception);
                            MessageBox.Show("Failed to download the new version:\n" + newDownloadTask?.Task.Exception?.Message);
                            return;
                        }

                        string hashPath = Path.Combine(FilePaths.ModCachePath, "unchained-launcher.sha512");

                        var commandLinePass = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                        var powershellCommand = new List<string>() {
                            $"Wait-Process -Id {Environment.ProcessId}",
                            $"Start-Sleep -Milliseconds 1000",
                            $"Move-Item -Force {newDownloadTask.Target.OutputPath!} {currentExecutableName}",
                            $"Start-Sleep -Milliseconds 500",
                            $"$hashResult = Get-FileHash {currentExecutableName} -Algorithm SHA512",
                            $"$hashResult.Hash | Set-Content -Path \\\"{hashPath}\\\"",
                            $".\\{currentExecutableName} {commandLinePass}"
                        };

                        PowerShell.Run(powershellCommand);
                        MessageBox.Show("The launcher will now close and start the new version. No further action must be taken.");
                        Window.Close(); //close the program
                        return;
                    } catch (Exception ex) {
                        logger.Error(ex);
                        MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
                    }
                } 
            } else {
                MessageBox.Show("You are currently running the latest version.");
            }
        }

        public void Dispose() {
            SaveSettings();
        }

        private static class InstallationTypeUtils {
            const string SteamPathSearchString = "Steam";
            const string EpicGamesPathSearchString = "Epic Games";

            public static InstallationType AutoDetectInstallationType() {
                var currentDir = Directory.GetCurrentDirectory();
                return currentDir switch {
                    var _ when currentDir.Contains(SteamPathSearchString) => InstallationType.Steam,
                    var _ when currentDir.Contains(EpicGamesPathSearchString) => InstallationType.EpicGamesStore,
                    _ => InstallationType.NotSet,
                };
            }
        }

    }

}