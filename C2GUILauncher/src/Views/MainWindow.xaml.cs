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
using System.IO;
using System.Threading;
using System.Collections.ObjectModel;
using C2GUILauncher.ViewModels;
using C2GUILauncher.Mods;
using C2GUILauncher.JsonModels;

namespace C2GUILauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public ModManagerViewModel ModManagerViewModel;
        public SettingsViewModel SettingsViewModel;
        public LauncherViewModel LauncherViewModel;

        public MainWindowViewModel MainWindowViewModel;

        private readonly ModManager ModManager;

        public MainWindow()
        {
            InitializeComponent();

            this.ModManager = new ModManager(
                "Chiv2-Community",
                "C2ModRegistry",
                new ObservableCollection<Mod>(),
                new ObservableCollection<Release>()
            );

            this.SettingsViewModel = SettingsViewModel.LoadSettings();


            this.ModManagerViewModel = new ModManagerViewModel(ModManager);


            this.LauncherViewModel = new LauncherViewModel(SettingsViewModel, ModManager);

            this.MainWindowViewModel = new MainWindowViewModel(ModManagerViewModel, SettingsViewModel, LauncherViewModel);

            this.Closed += MainWindow_Closed;

            this.DataContext = this;


            this.LauncherTab.DataContext = this.LauncherViewModel;
            this.ModManagerTab.DataContext = this.ModManagerViewModel;
            this.SettingsTab.DataContext = this.SettingsViewModel;

        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            this.SettingsViewModel.SaveSettings();
        }
    }

    public class MainWindowViewModel
    {
        public ModManagerViewModel ModManagerViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public LauncherViewModel LauncherViewModel { get; }

        public MainWindowViewModel(ModManagerViewModel modManagerViewModel, SettingsViewModel settingsViewModel, LauncherViewModel launcherViewModel)
        {
            this.ModManagerViewModel = modManagerViewModel;
            this.SettingsViewModel = settingsViewModel;
            this.LauncherViewModel = launcherViewModel;
        }
    }
}
