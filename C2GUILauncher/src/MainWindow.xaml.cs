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
using System.Runtime.InteropServices;
using Octokit;
using System.Diagnostics;

namespace C2GUILauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private IList<DownloadTarget> PendingDownloads;
        private string CLIArgs;
        private bool CLIArgsModified;

        public MainWindow()
        {
            InitializeComponent();

            this.PendingDownloads = new List<DownloadTarget>();
            this.Downloads.ItemsSource = PendingDownloads;

            // Skip 1, because the first arg is the exe path
            this.CLIArgs = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
            this.CLIArgsTextBox.Text = this.CLIArgs;

            this.CLIArgsModified = false;
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

            // Pass args through if the args box has been modified, or if we're an EGS install
            var shouldSendArgs = installationType == InstallationType.EpicGamesStore || CLIArgsModified;

            // pass empty string for args, if we shouldn't send any.
            var args = shouldSendArgs ? CLIArgs : "";

            var debugMode = EnableDebugDLLs.IsChecked ?? false;
            var disablePluginDownloads = DisablePluginDownload.IsChecked ?? false;


            // Download the mod files, potentially using debug dlls
            var launchThread = new Thread(async () =>
            {
                try
                {
                    if (!disablePluginDownloads) { 
                        List<DownloadTask> downloadTasks = ModDownloader.DownloadModFiles(debugMode).ToList();
                        PendingDownloads.Concat(downloadTasks.Select(x => x.Target));
                        await Task.WhenAll(downloadTasks.Select(x => x.Task));
                    }
                    var dlls = Directory.EnumerateFiles(Chivalry2Launchers.PluginDir, "*.dll").ToArray();
                    Chivalry2Launchers.ModdedLauncher.Dlls = dlls;
                    var process = Chivalry2Launchers.ModdedLauncher.Launch(args);

                    await process.WaitForExitAsync();
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            });

            launchThread.Start();
            DisableButtons();
        }

        static int[] version = { 0, 0, 0 };
        private void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var github = new GitHubClient(new ProductHeaderValue("C2GUILauncher"));

            var repoCall = github.Repository.Release.GetLatest(667470779); //C2GUILauncher repo id
            repoCall.Wait();
            if (!repoCall.IsCompletedSuccessfully){
                MessageBox.Show("Could not connect to github to retrieve latest version information:\n" + repoCall.Exception.Message);
                return;
            }
            var latestInfo = repoCall.Result;
            string tagName = latestInfo.TagName;
            int[] latest = tagName
                .Split(".")
                .Select(
                    s => int.Parse( //parse as int
                        string.Concat(s.Where(c => char.IsDigit(c))) //filter out non-numeric characters
                    )
                ).ToArray(); //join to array representing version
            //if latest is newer than current version
            if (latest[0] > version[0] ||
                latest[1] > version[1] ||
                latest[2] > version[2]){
                string currentVersionString = string.Join(".", version.Select(i => i.ToString()));
                MessageBoxResult dialogResult = MessageBox.Show(
                    $"A newer version was found.\n " +
                    $"{tagName} > v{currentVersionString}\n\n" +
                    $"Download the new update?",
                    "Update?", MessageBoxButton.YesNo);

                if (dialogResult == MessageBoxResult.No)
                {
                    return;
                }
                else if (dialogResult == MessageBoxResult.Yes)
                {
                    try
                    {
                        var url = latestInfo.Assets.Where(
                                    a => a.Name.Contains("Chivalry2Launcher.exe") //find the launcher exe
                                ).First().BrowserDownloadUrl; //get the download URL
                        var newDownloadTask = HttpHelpers.DownloadFileAsync(new DownloadTarget(url, "C2GUILauncher.exe"));
                        string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        string exeDir = System.IO.Path.GetDirectoryName(exePath) ?? "";

                        newDownloadTask.Wait();
                        if (!repoCall.IsCompletedSuccessfully)
                        {
                            MessageBox.Show("Failed to download the new version:\n" + newDownloadTask.Exception.Message);
                            return;
                        }
                        
                        Process pwsh = new Process();
                        pwsh.StartInfo.FileName = "powershell.exe";
                        var commandLinePass = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                        string powershellCommand =
                        $"Wait-Process -Id {Environment.ProcessId}; " +
                        $"Start-Sleep -Milliseconds 500; " +
                        $"Move-Item -Force C2GUILauncher.exe Chivalry2Launcher.exe;" +
                        $"Start-Sleep -Milliseconds 500; " +
                        $".\\Chivalry2Launcher.exe {commandLinePass}";
                        pwsh.StartInfo.Arguments = $"-Command \"{powershellCommand}\"";
                        pwsh.StartInfo.CreateNoWindow = true;
                        pwsh.Start();
                        MessageBox.Show("The launcher will now close and start the new version. No further action must be taken.");
                        this.Close(); //close the program
                        return;
                    }catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
                    }
                    
                }
            }
            else
            {
                MessageBox.Show("You are currently running the latest version.");
            }
        }

        private void CLIArgsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CLIArgsModified = true;
            CLIArgs = CLIArgsTextBox.Text.Replace(Environment.NewLine, " ");
        }
    }
}
