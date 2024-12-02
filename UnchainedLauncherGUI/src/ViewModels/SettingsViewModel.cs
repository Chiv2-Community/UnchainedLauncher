using UnchainedLauncher.GUI.JsonModels;
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
using System.Threading.Tasks;
using UnchainedLauncher.GUI.Views;
using LanguageExt;
using UnchainedLauncher.Core.JsonModels;
using LanguageExt.UnsafeValueAccess;
using UnchainedLauncher.Core.Installer;

namespace UnchainedLauncher.GUI.ViewModels
{
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
        public IUnchainedLauncherInstaller Installer { get; }

        public SettingsViewModel(MainWindow window, IUnchainedLauncherInstaller installer, InstallationType installationType, bool enablePluginAutomaticUpdates, string additionalModActors, string serverBrowserBackend, FileBackedSettings<LauncherSettings> launcherSettings, string cliArgs) {
            InstallationType = installationType;
            EnablePluginAutomaticUpdates = enablePluginAutomaticUpdates;
            AdditionalModActors = additionalModActors;
            LauncherSettings = launcherSettings;
            ServerBrowserBackend = serverBrowserBackend;
            Installer = installer;

            _cliArgs = cliArgs;
            CLIArgsModified = false;

            CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdate);
            CleanUpInstallationCommand = new RelayCommand(CleanUpInstallation);

            this.Window = window;
        }

        public static SettingsViewModel LoadSettings(MainWindow window, IUnchainedLauncherInstaller installer) {
            var cliArgsList = Environment.GetCommandLineArgs();
            var cliArgs = cliArgsList.Length > 1 ? Environment.GetCommandLineArgs().Skip(1).Aggregate((x, y) => x + " " + y) : "";

            var fileBackedSettings = new FileBackedSettings<LauncherSettings>(SettingsFilePath);

            var loadedSettings = fileBackedSettings.LoadSettings();

            return new SettingsViewModel(
                window,
                installer,
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

        
        public async Task CheckForUpdate() {
            logger.Info("Checking for updates...");

            var maybeLatestRelease = await Installer.GetLatestRelease();
            if (maybeLatestRelease.IsNone) {
                MessageBox.Show("Failed to check for updates. Check the logs for more details.");
                return;
            }

            var latestRelease = maybeLatestRelease.ValueUnsafe();
            if (latestRelease.Version.ComparePrecedenceTo(new Semver.SemVersion(version.Major, version.Minor, version.Build)) > 0) {
                logger.Info($"Latest version: {latestRelease.Version}, Current version: {CurrentVersion}");
                await ChangeVersion(latestRelease);
            } else {
                MessageBox.Show("You are currently running the latest version.");
                return;
            }
        }

        private async Task ChangeVersion(VersionedRelease release) {
            Option<MessageBoxResult> dialogResult =
                UpdatesWindow.Show(
                    "Chivalry 2 Unchained Launcher Update",
                    "Update the Unchained Launcher?",
                    "Yes",
                    "No",
                    null,
                    List(
                        new DependencyUpdate("Launcher", Some(CurrentVersion.ToString()), release.Version.ToString(), release.Release.Url, "")
                    )
                );

            if (dialogResult.Contains(MessageBoxResult.No)) {
                logger.Info("User chose not to update.");
                return;
            } else if (dialogResult.Contains(MessageBoxResult.Yes)) {
                logger.Info("User chose to update.");
                await Installer.Install(new DirectoryInfo(Assembly.GetExecutingAssembly().Location), release, true, logger.Warn);
            }
        }

        public void Dispose() {
            SaveSettings();
            GC.SuppressFinalize(this);
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
