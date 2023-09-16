using C2GUILauncher.JsonModels;
using C2GUILauncher.src;
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
        public bool EnablePluginLogging { get; set; }
        public bool EnablePluginAutomaticUpdates { get; set; }

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

        public SettingsViewModel(MainWindow window, InstallationType installationType, bool enablePluginLogging, bool enablePluginAutomaticUpdates, FileBackedSettings<LauncherSettings> launcherSettings, string cliArgs) {
            InstallationType = installationType;
            EnablePluginLogging = enablePluginLogging;
            EnablePluginAutomaticUpdates = enablePluginAutomaticUpdates;
            LauncherSettings = launcherSettings;

            _cliArgs = cliArgs;
            CLIArgsModified = false;

            CheckForUpdateCommand = new RelayCommand(CheckForUpdate);
            CleanUpInstallationCommand = new RelayCommand(CleanUpInstallation);

            this.Window = window;
        }

        public static SettingsViewModel LoadSettings(MainWindow window) {
            var cliArgsList = Environment.GetCommandLineArgs();
            var cliArgs = cliArgsList.Length > 1 ? Environment.GetCommandLineArgs().Skip(1).Aggregate((x, y) => x + " " + y) : "";

            var defaultSettings = new LauncherSettings(InstallationTypeUtils.AutoDetectInstallationType(), false, true);
            var fileBackedSettings = new FileBackedSettings<LauncherSettings>(SettingsFilePath, defaultSettings);

            var loadedSettings = fileBackedSettings.LoadSettings();

            return new SettingsViewModel(
                window,
                loadedSettings.InstallationType,
                loadedSettings.EnablePluginLogging,
                loadedSettings.EnablePluginAutomaticUpdates,
                fileBackedSettings,
                cliArgs
            );
        }

        public void SaveSettings() {
            LauncherSettings.SaveSettings(
                new LauncherSettings(InstallationType, EnablePluginLogging, EnablePluginAutomaticUpdates)
            );
        }


        // TODO: This function knows too much.
        //       It should be telling the mod manager and other things which
        //       manage files to clean themselves up, rather than this class
        //       being aware of everything.
        private void CleanUpInstallation() {
            logger.Info("CleanUpInstallation button clicked.");
            var message = new List<string>() {
                "Are you sure? This will delete the following:",
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

            Version? latest = null;
            try {
                Version.Parse(tagName);
            } catch {
                // If parsing fails, just say they're equal.
                latest = version;
            }
            //if latest is newer than current version
            if (latest > version) {
                string currentVersionString = version.ToString();

                MessageBoxResult dialogResult = MessageBox.Show(
                    $"A newer version was found.\n " +
                    $"{tagName} > v{currentVersionString}\n\n" +
                    $"Download the new update?",
                    "Update?", MessageBoxButton.YesNo);

                if (dialogResult == MessageBoxResult.No) {
                    return;
                } else if (dialogResult == MessageBoxResult.Yes) {
                    try {
                        var url = latestInfo.Assets.Where(
                            a => a.Name.Contains("Launcher.exe") //find the launcher exe
                        ).First().BrowserDownloadUrl; //get the download URL

                        var currentExecutableName = Process.GetCurrentProcess().ProcessName;

                        var newDownloadTask = HttpHelpers.DownloadFileAsync(url, "UnchainedLauncher-update.exe");
                        string exePath = Assembly.GetExecutingAssembly().Location;
                        string exeDir = Path.GetDirectoryName(exePath) ?? "";

                        newDownloadTask.Task.Wait();
                        if (!repoCall.IsCompletedSuccessfully) {
                            MessageBox.Show("Failed to download the new version:\n" + newDownloadTask?.Task.Exception?.Message);
                            return;
                        }

                        var commandLinePass = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                        var powershellCommand = new List<string>() {
                            $"Wait-Process -Id {Environment.ProcessId}",
                            $"Start-Sleep -Milliseconds 500",
                            $"Move-Item -Force UnchainedLauncher-update.exe {currentExecutableName}",
                            $"Start-Sleep -Milliseconds 500",
                            $".\\{currentExecutableName} {commandLinePass}"
                        };

                        PowerShell.Run(powershellCommand);
                        MessageBox.Show("The launcher will now close and start the new version. No further action must be taken.");
                        Window.Close(); //close the program
                        return;
                    } catch (Exception ex) {
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
