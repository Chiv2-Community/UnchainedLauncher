using PropertyChanged;
using System;
using System.Windows;
using UnchainedLauncher.GUI.ViewModels;
using System.Windows.Controls;
using UnchainedLauncher.Core;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using System.Linq;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Installer;
using UnchainedLauncher.Core.Mods.Registry.Downloader;
using Wpf.Ui.Controls;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : FluentWindow {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow(MainWindowViewModel vm) {
            DataContext = ViewModel = vm;
            InitializeComponent();
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            ViewModel.Dispose();
        }
    }

}