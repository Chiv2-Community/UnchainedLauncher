using System;
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

namespace C2GUILauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private async void LaunchModdedButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // For a modded installation we need to download the mod files and then launch via the modded launcher.
                // For steam installations, args do not get passed through.

                // Get the installation type. If auto detect fails, exit this function.
                var installationType = GetInstallationType();
                if(installationType == InstallationType.NotSet) return;

                var isSteam = installationType == InstallationType.Steam;

                // don't pass args through for steam
                var args = isSteam ? "" : string.Join(" ", Environment.GetCommandLineArgs().Skip(1));

                // Download the mod files, potentially using debug dlls
                List<DownloadTask> downloadTasks = ModDownloader.DownloadModFiles(EnableDebugDLLs.IsChecked ?? false).ToList();

                await Task.WhenAll(downloadTasks.Select(s => s.Task));

                var dlls = Directory.EnumerateFiles(Chivalry2Launchers.PluginDir, "*.dll").ToArray();
                Chivalry2Launchers.ModdedLauncher.Dlls = dlls;
                Chivalry2Launchers.ModdedLauncher.Launch(args);  
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
    }
}
