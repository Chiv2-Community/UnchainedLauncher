﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using C2GUILauncher.Mods;
using System.IO;
using System.Threading;

namespace C2GUILauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private IList<DownloadTarget> pendingDownloads = new List<DownloadTarget>();
        public MainWindow()
        {
            InitializeComponent();
            this.Downloads.ItemsSource = pendingDownloads;
        }

        private void DisableButtons()
        {
            LaunchModdedButton.IsEnabled = false;
            LaunchVanillaButton.IsEnabled = false;
            Tabs.IsEnabled = false;
        }

        private InstallationType GetInstallationType()
        {
            var installationType = (InstallationType)InstallationTypeSelection.SelectedIndex;
            if (installationType == InstallationType.NotSet)
            {
                installationType = InstallationTypeUtils.AutoDetectInstallationType();
                if (installationType == InstallationType.NotSet)
                {
                    MessageBox.Show("Could not detect installation type. Please select one manually.");
                }
            }

            return installationType;
        }

        private void LaunchVanillaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // For a vanilla launch we need to pass the args through to the vanilla launcher.
                // Skip the first arg which is the path to the exe.
                var args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                Chivalry2Launchers.VanillaLauncher.Launch(args);
                DisableButtons();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void LaunchModdedButton_Click(object sender, RoutedEventArgs e)
        {
            // For a modded installation we need to download the mod files and then launch via the modded launcher.
            // For steam installations, args do not get passed through.

            // Get the installation type. If auto detect fails, exit this function.
            var installationType = GetInstallationType();
            if (installationType == InstallationType.NotSet) return;

            var isSteam = installationType == InstallationType.Steam;

            // don't pass args through for steam
            var args = isSteam ? "" : string.Join(" ", Environment.GetCommandLineArgs().Skip(1));

            var debugMode = EnableDebugDLLs.IsChecked ?? false;

            // Download the mod files, potentially using debug dlls
            var launchThread = new Thread(async () =>
            {
                try
                {
                    List<DownloadTask> downloadTasks = ModDownloader.DownloadModFiles(debugMode).ToList();
                    pendingDownloads.Concat(downloadTasks.Select(x => x.Target));
                    await Task.WhenAll(downloadTasks.Select(x => x.Task));
                    var dlls = Directory.EnumerateFiles(Chivalry2Launchers.PluginDir, "*.dll").ToArray();
                    Chivalry2Launchers.ModdedLauncher.Dlls = dlls;
                    var process = Chivalry2Launchers.ModdedLauncher.Launch(args);

                    await process.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            });

            launchThread.Start();
            DisableButtons();

        }

        private void RunLaunchModdedProcess() { }
    }
}
