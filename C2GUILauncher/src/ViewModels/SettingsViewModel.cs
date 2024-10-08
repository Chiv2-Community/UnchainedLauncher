﻿using C2GUILauncher.JsonModels;
using CommunityToolkit.Mvvm.Input;
using log4net;
using log4net.Repository.Hierarchy;
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

namespace C2GUILauncher.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public class SettingsViewModel {
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

            CheckForUpdateCommand = new RelayCommand(CheckForUpdate);
            CleanUpInstallationCommand = new RelayCommand(CleanUpInstallation);

            this.Window = window;
        }

        public static SettingsViewModel LoadSettings(MainWindow window) {
            var cliArgsList = Environment.GetCommandLineArgs();
            var cliArgs = cliArgsList.Length > 1 ? Environment.GetCommandLineArgs().Skip(1).Aggregate((x, y) => x + " " + y) : "";

            var defaultSettings = new LauncherSettings(InstallationTypeUtils.AutoDetectInstallationType(), true, "", "https://servers.polehammer.net");
            var fileBackedSettings = new FileBackedSettings<LauncherSettings>(SettingsFilePath, defaultSettings);

            var loadedSettings = fileBackedSettings.LoadSettings();

            #pragma warning disable CS8629 // All calls to .Value and ! below are safe because all defaults are non-null.
            return new SettingsViewModel(
                window,
                loadedSettings.InstallationType ?? defaultSettings.InstallationType.Value,
                loadedSettings.EnablePluginAutomaticUpdates ?? defaultSettings.EnablePluginAutomaticUpdates.Value,
                loadedSettings.AdditionalModActors ?? defaultSettings.AdditionalModActors!,
                loadedSettings.ServerBrowserBackend ?? defaultSettings.ServerBrowserBackend!,
                fileBackedSettings,
                cliArgs
            );
            #pragma warning restore CS8629 // Nullable value type may be null.
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

            var vanillaPaks = new List<string>() { "pakchunk0-WindowsNoEditor" };
            var filePaths =
                Directory
                    .GetFiles(FilePaths.PakDir)
                    .Where(pakName => {
                        if (vanillaPaks.Any(vanillaPak => (pakName.EndsWith(vanillaPak+".pak") || pakName.EndsWith(vanillaPak + ".sig")))) {
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
        private void CheckForUpdate() {
            var github = new GitHubClient(new ProductHeaderValue("C2GUILauncher"));

            var repoCall = github.Repository.Release.GetLatest(667470779); //C2GUILauncher repo id
            repoCall.Wait();
            if (!repoCall.IsCompletedSuccessfully) {
                MessageBox.Show("Could not connect to github to retrieve latest version information:\n" + repoCall?.Exception?.Message);
                return;
            }
            var latestInfo = repoCall.Result;
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
                MessageBoxResult dialogResult = MessageBox.Show(
                    $"A newer version was found.\n " +
                    $"{tagName} > {CurrentVersion}\n\n" +
                    $"Download the new update?",
                    "Update?", MessageBoxButton.YesNo);

                if (dialogResult == MessageBoxResult.No) {
                    logger.Info("User chose not to update.");
                    return;
                } else if (dialogResult == MessageBoxResult.Yes) {
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
                            $"Move-Item -Force UnchainedLauncher-update.exe {currentExecutableName}",
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
