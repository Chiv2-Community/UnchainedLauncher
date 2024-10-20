using CommunityToolkit.Mvvm.Input;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core;
using System.Threading;
using LanguageExt;
using System.Collections.Immutable;
using LanguageExt.SomeHelp;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.GUI.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels {

    [AddINotifyPropertyChangedInterface]
    public class LauncherViewModel {
        private static readonly ILog logger = LogManager.GetLogger(nameof(LauncherViewModel));
        public ICommand LaunchVanillaCommand { get; }
        public ICommand LaunchModdedCommand { get; }

        private SettingsViewModel Settings { get; }
        private ModManager ModManager { get; }

        public bool CanClick { get; set; }

        private readonly MainWindow Window;

        public Chivalry2Launcher Launcher { get; }


        public LauncherViewModel(MainWindow window, SettingsViewModel settings, ModManager modManager, Chivalry2Launcher launcher) {
            CanClick = true;

            Settings = settings;
            ModManager = modManager;

            this.LaunchVanillaCommand = new RelayCommand(LaunchVanilla);
            this.LaunchModdedCommand = new RelayCommand(() => LaunchModded(Prelude.None));

            Window = window;

            Launcher = launcher;
        }

        public void LaunchVanilla() {
            try {
                // For a vanilla launch we need to pass the args through to the vanilla launcher.
                // Skip the first arg which is the path to the exe.
                var launchResult = Launcher.LaunchVanilla(System.Environment.GetCommandLineArgs().Skip(1));
                CanClick = false;

                launchResult.Match(
                    Left: error => {
                        MessageBox.Show("Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                        CanClick = true;
                    },
                    Right: process => {
                        new Thread(async () => {
                            await process.WaitForExitAsync();
                            CanClick = true;

                            if (process.ExitCode != 0) {
                                logger.Error($"Chivalry 2 exited with code {process.ExitCode}.");
                                logger.Info(process.StandardOutput.ReadToEnd().ToList());
                                logger.Error(process.StandardError.ReadToEnd().ToList());

                                MessageBox.Show($"Chivalry 2 exited with code {process.ExitCode}. Check the logs for details.");
                            }
                        }).Start();
                    }
                );
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        public void LaunchModded(Option<ServerLaunchOptions> serverOpts) {
            CanClick = false;

            var options = new ModdedLaunchOptions(
                Settings.ServerBrowserBackend,
                ModManager.EnabledModReleases.AsEnumerable().ToSome(),
                Prelude.None
            );

            var exArgs = Window.SettingsViewModel.CLIArgs.Split(" ");

            try {
                var launchResult = Launcher.LaunchModded(Settings.InstallationType, options, serverOpts, exArgs);

                launchResult.Match(
                    None: () => {
                        MessageBox.Show("Installation type not set. Please set it in the settings tab.");
                    },
                    Some: errorOrSuccess => {
                        errorOrSuccess.Match(
                            Left: error => {
                                MessageBox.Show($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                                CanClick = true;
                            },
                            Right: process => {
                                new Thread(async () => {
                                    await process.WaitForExitAsync();
                                    CanClick = true;

                                    if (process.ExitCode != 0) {
                                        logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                                        logger.Info(process.StandardOutput.ReadToEnd().ToList());
                                        logger.Error(process.StandardError.ReadToEnd().ToList());


                                        MessageBox.Show($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
                                    }
                                }).Start();

                                // look upon this next code and weep, for this is my offering to
                                // the flying spaghetti monster. this is the only point where
                                // everything I need in order to do this is available.

                                // if this is a server launch, add it to the servers list page

                                // mega TOCTOU violations incurred by the
                                // nullability/optionality of serverOpts::GamePort,PingPort,etc
                                // and general spaghetti.
                                // Electing to fail hard here rather than make the error
                                // rarer/harder to find by delegating the problem code
                                // to a default
                                PublicPorts ports = new(
                                    Window.ServerSettingsViewModel.GamePort,
                                    Window.ServerSettingsViewModel.PingPort,
                                    Window.ServerSettingsViewModel.A2sPort
                                );
                                C2ServerInfo serverInfo = new() {
                                    Ports = ports,
                                    Name = Window.ServerSettingsViewModel.ServerName,
                                    Description = Window.ServerSettingsViewModel.ServerDescription,
                                    PasswordProtected = Window.ServerSettingsViewModel.ServerPassword.Length == 0,
                                    Mods = ModManager.EnabledModReleases.Select(release =>
                                        new ServerBrowserMod(
                                            release.Manifest.Name,
                                            release.Version.ToString(),
                                            release.Manifest.Organization
                                        )
                                    ).ToArray()
                                };

                                Window.ServersViewModel.RegisterServer("127.0.0.1", ports, Window.ServerSettingsViewModel.RconPort, serverInfo, process);
                            }
                        );
                    }
                );
            } catch (Exception) {
                MessageBox.Show("Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                return;
            } finally {
                CanClick = true;
            }

        }
    }
}
