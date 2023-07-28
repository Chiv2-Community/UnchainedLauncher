using C2GUILauncher.src;
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
                var args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                Chivalry2Launchers.VanillaLauncher.Launch(args);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void LaunchModdedButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var installationType = GetInstallationType();
                if(installationType == InstallationType.NotSet) return;

                var isSteam = installationType == InstallationType.Steam;

                // don't pass args through for steam
                var args = isSteam ? "" : string.Join(" ", Environment.GetCommandLineArgs().Skip(1));

                List<DownloadTask> downloadTasks = ModDownloader.DownloadModFiles(EnableDebugDLLs.IsChecked ?? false).ToList();

                while (downloadTasks.Count() > 0)
                {
                    var downloadingOutput = downloadTasks.Select(dl => dl.Target.Url + " -> " + dl.Target.OutputPath).Aggregate((a, b) => a + "\n" + b);
                    LogOutput.Text = "Waiting for " + downloadTasks.Count() + " downloads to finish...\n";
                    LogOutput.Text += downloadingOutput + "\n";

                    Task[] taskList = downloadTasks.Select(dl => dl.Task).ToArray();
                    var idx = Task.WaitAny(taskList);

                    downloadTasks.RemoveAt(idx);
                }

                Chivalry2Launchers.ModdedLauncher.Launch(args);  
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
    }
}
