using CommunityToolkit.Mvvm.Input;
using log4net;
using PropertyChanged;
using Semver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.ViewModels {
    public class SettingsCodec(
        RegistryWindowVM registryWindowVM,
        RegistryWindowService registryWindowService,
        IChivalry2InstallationFinder installationFinder,
        string cliArgs)
        : DerivedJsonCodec<LauncherSettings, SettingsVM>(ToJsonType,
            settings => ToClassType(
                settings,
                registryWindowVM,
                registryWindowService,
                installationFinder,
                cliArgs
            )) {
        private static LauncherSettings ToJsonType(SettingsVM vm) {
            return new LauncherSettings(
                vm.InstallationType,
                vm.EnablePluginAutomaticUpdates,
                vm.IsUnrealScannerEnabled,
                vm.AdditionalModActors,
                vm.ServerBrowserBackend,
                vm.UseLightTheme,
                vm.AllowUnstablePluginReleases,
                SettingsVM.Version
            );
        }

        public static SettingsVM ToClassType(
            LauncherSettings settings,
            RegistryWindowVM registryWindowVM,
            RegistryWindowService registryWindowService,
            IChivalry2InstallationFinder installationFinder,
            string cliArgs
        ) {
            return new SettingsVM(
                registryWindowVM,
                registryWindowService,
                settings?.InstallationType ?? SettingsVM.DetectInstallationType(installationFinder),
                settings?.EnablePluginAutomaticUpdates ?? true,
                settings?.IsUnrealScannerEnabled ?? false,
                settings?.AdditionalModActors ?? "",
                settings?.ServerBrowserBackend ?? "https://servers.polehammer.net",
                settings?.UseLightTheme ?? false,
                settings?.LastLaunchVersion?.WithoutMetadata() == SettingsVM.Version.WithoutMetadata()
                    ? (settings?.AllowUnstablePluginReleases ?? SettingsVM.Version.IsPrerelease)
                    : SettingsVM.Version.IsPrerelease,
                cliArgs
            );
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class SettingsVM {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(SettingsVM));
        public static readonly SemVersion Version = SemVersion.Parse(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, SemVersionStyles.Any);

        public InstallationType InstallationType { get; set; }
        public bool EnablePluginAutomaticUpdates { get; set; }
        public bool IsUnrealScannerEnabled { get; set; }
        public string AdditionalModActors { get; set; }
        public string ServerBrowserBackend { get; set; }
        public bool UseLightTheme { get; set; }
        public bool AllowUnstablePluginReleases { get; set; }

        public bool HasLaunched { get; set; }

        public bool CanLaunch => !HasLaunched || IsLauncherReusable();

        public string CLIArgs { get; set; }
        public string CurrentVersion => "v" + Version.WithoutMetadata();

        public bool IsLauncherReusable() => InstallationType == InstallationType.Steam;

        public static IEnumerable<InstallationType> AllInstallationTypes => Enum.GetValues<InstallationType>();

        private RegistryWindowService RegistryWindowService { get; }
        private RegistryWindowVM RegistryWindowVM { get; }

        public SettingsVM(RegistryWindowVM registryWindowVM, RegistryWindowService registryWindowService, InstallationType installationType, bool enablePluginAutomaticUpdates, bool enableModScanner, string additionalModActors, string serverBrowserBackend, bool useLightTheme, bool allowUnstablePluginReleases, string cliArgs) {
            RegistryWindowVM = registryWindowVM;
            RegistryWindowService = registryWindowService;
            InstallationType = installationType;
            EnablePluginAutomaticUpdates = enablePluginAutomaticUpdates;
            IsUnrealScannerEnabled = enableModScanner;
            AdditionalModActors = additionalModActors;
            ServerBrowserBackend = serverBrowserBackend;
            UseLightTheme = useLightTheme;
            AllowUnstablePluginReleases = allowUnstablePluginReleases;

            CLIArgs = cliArgs;

            ThemeService.Apply(UseLightTheme ? ThemeVariant.Light : ThemeVariant.Dark);
        }

        private void OnUseLightThemeChanged() {
            ThemeService.Apply(UseLightTheme ? ThemeVariant.Light : ThemeVariant.Dark);
        }

        [RelayCommand]
        private void OpenRegistryWindow() {
            RegistryWindowService.ShowAllRegistriesWindow(RegistryWindowVM);
        }

        public void Dispose() {
            GC.SuppressFinalize(this);
        }

        public static InstallationType DetectInstallationType(IChivalry2InstallationFinder finder) {
            var curDir = new DirectoryInfo(Directory.GetCurrentDirectory());

            if (finder.IsEGSDir(curDir)) return InstallationType.EpicGamesStore;

            if (finder.IsSteamDir(curDir)) return InstallationType.Steam;

            Logger.Warn("Could not detect installation type.");
            return InstallationType.NotSet;
        }
    }

}