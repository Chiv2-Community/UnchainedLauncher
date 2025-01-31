using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public partial class SettingsVM : IDisposable {
        private static readonly ILog logger = LogManager.GetLogger(nameof(SettingsVM));
        private static readonly Version version = Assembly.GetExecutingAssembly().GetName().Version!;

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

        public bool IsLauncherReusable() => InstallationType == InstallationType.Steam;


        private IUserDialogueSpawner UserDialogueSpawner { get; }

        public static IEnumerable<InstallationType> AllInstallationTypes {
            get { return Enum.GetValues(typeof(InstallationType)).Cast<InstallationType>(); }
        }

        public FileBackedSettings<LauncherSettings> LauncherSettings { get; set; }
        public IUnchainedLauncherInstaller Installer { get; }
        public IReleaseLocator UnchainedReleaseLocator { get; set; }
        public readonly Action<int> ExitProgram;
        public bool CanClick { get; set; }

        public SettingsVM(IUnchainedLauncherInstaller installer, IReleaseLocator unchainedReleaseLocator, IUserDialogueSpawner dialogueSpawner, InstallationType installationType, bool enablePluginAutomaticUpdates, string additionalModActors, string serverBrowserBackend, FileBackedSettings<LauncherSettings> launcherSettings, string cliArgs, Action<int> exitProgram) {
            Installer = installer;
            UnchainedReleaseLocator = unchainedReleaseLocator;
            UserDialogueSpawner = dialogueSpawner;
            InstallationType = installationType;
            EnablePluginAutomaticUpdates = enablePluginAutomaticUpdates;
            AdditionalModActors = additionalModActors;
            LauncherSettings = launcherSettings;
            ServerBrowserBackend = serverBrowserBackend;
            ExitProgram = exitProgram;
            CanClick = true;

            _cliArgs = cliArgs;
            CLIArgsModified = false;
        }


        public static SettingsVM LoadSettings(IChivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer, IReleaseLocator unchainedReleaseLocator, IUserDialogueSpawner userDialogueSpawner, Action<int> exitProgram) {
            var cliArgsList = Environment.GetCommandLineArgs();
            var cliArgs = cliArgsList.Length > 1 ? Environment.GetCommandLineArgs().Skip(1).Aggregate((x, y) => $"{x} {y}") : "";

            var fileBackedSettings = new FileBackedSettings<LauncherSettings>(FilePaths.LauncherSettingsFilePath);
            var loadedSettings = fileBackedSettings.LoadSettings();

            return new SettingsVM(
                installer,
                unchainedReleaseLocator,
                userDialogueSpawner,
                loadedSettings?.InstallationType ?? DetectInstallationType(installationFinder),
                loadedSettings?.EnablePluginAutomaticUpdates ?? true,
                loadedSettings?.AdditionalModActors ?? "",
                loadedSettings?.ServerBrowserBackend ?? "https://servers.polehammer.net",
                fileBackedSettings,
                cliArgs,
                exitProgram
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
        [RelayCommand]
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

            var choice = UserDialogueSpawner.DisplayYesNoMessage(message, "Really clean up installation?");
            logger.Info($"Are you sure? User selects: {choice}");

            if (choice == UserDialogueChoice.No) return;

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
                $"Wait-Process -Id {Environment.ProcessId} -ErrorAction 'Ignore'",
                $"Start-Sleep -Milliseconds 500",
                $".\\{currentExecutableName} {commandLinePass}"
            };

            PowerShell.Run(powershellCommands);
            UserDialogueSpawner.DisplayMessage("The launcher will now restart. No further action must be taken.");

            logger.Info("Closing");
            ExitProgram(0);
        }

        [RelayCommand]
        public async Task CheckForUpdate() {
            logger.Info("Checking for updates...");

            var latestRelease = await UnchainedReleaseLocator.GetLatestRelease();
            if (latestRelease == null) {
                UserDialogueSpawner.DisplayMessage("Failed to check for updates. Check the logs for more details.");
                return;
            }

            if (latestRelease.Version.ComparePrecedenceTo(new Semver.SemVersion(version.Major, version.Minor, version.Build)) > 0) {
                logger.Info($"Latest version: {latestRelease.Version}, Current version: {CurrentVersion}");
                await ChangeVersion(latestRelease);
            }
            else {
                UserDialogueSpawner.DisplayMessage("You are currently running the latest version.");
            }
        }

        private async Task ChangeVersion(ReleaseTarget release) {
            UserDialogueChoice? dialogResult =
                UserDialogueSpawner.DisplayUpdateMessage(
                    "Chivalry 2 Unchained Launcher Update",
                    "Update the Unchained Launcher?",
                    "Yes",
                    "No",
                    null,
                    new DependencyUpdate("Launcher", CurrentVersion, release.Version.ToString(), release.PageUrl, "")
                );

            if (dialogResult == UserDialogueChoice.No) {
                logger.Info("User chose not to update.");
                return;
            }

            if (dialogResult == UserDialogueChoice.Yes) {
                logger.Info("User chose to update.");
                await Installer.Install(new DirectoryInfo(Environment.CurrentDirectory), release, true, (_) => { });
            }
        }

        public void Dispose() {
            SaveSettings();
            GC.SuppressFinalize(this);
        }

        private static InstallationType DetectInstallationType(IChivalry2InstallationFinder finder) {
            var curDir = new DirectoryInfo(Directory.GetCurrentDirectory());

            if (finder.IsEGSDir(curDir)) return InstallationType.EpicGamesStore;

            if (finder.IsSteamDir(curDir)) return InstallationType.Steam;

            logger.Warn("Could not detect installation type.");
            return InstallationType.NotSet;
        }
    }

}