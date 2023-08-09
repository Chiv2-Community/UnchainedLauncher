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
using System.IO;
using System.Threading;
using System.Collections.ObjectModel;
using C2GUILauncher.ViewModels;
using C2GUILauncher.Mods;
using C2GUILauncher.JsonModels;
using System.Diagnostics;
using C2GUILauncher.src.ViewModels;
using System.IO.Compression;
using PropertyChanged;

namespace C2GUILauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : Window
    {

        public ModListViewModel ModManagerViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public LauncherViewModel LauncherViewModel { get; }

        private readonly ModManager ModManager;


        public MainWindow()
        {
            InitializeComponent();

            var needsClose = InstallerViewModel.AttemptInstall();
            if(needsClose)
                this.Close();

            this.ModManager = ModManager.ForRegistry(
                "Chiv2-Community",
                "C2ModRegistry",
                "TBL\\Content\\Paks"
            );

            this.SettingsViewModel = SettingsViewModel.LoadSettings();
            this.ModManagerViewModel = new ModListViewModel(ModManager);
            this.LauncherViewModel = new LauncherViewModel(SettingsViewModel, ModManager);

            this.SettingsTab.DataContext = this.SettingsViewModel;
            this.ModManagerTab.DataContext = this.ModManagerViewModel;
            this.LauncherTab.DataContext = this.LauncherViewModel;

            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            this.SettingsViewModel.SaveSettings();
        }

    }

}
