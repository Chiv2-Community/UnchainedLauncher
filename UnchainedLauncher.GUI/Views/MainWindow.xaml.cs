using UnchainedLauncher.GUI.ViewModels;
using log4net;
using PropertyChanged;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.Core;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using System.Linq;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Installer;
using UnchainedLauncher.Core.Mods.Registry.Downloader;

namespace UnchainedLauncher.GUI.Views
{
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : Window {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow(MainWindowViewModel vm) {
            DataContext = ViewModel = vm;
            InitializeComponent();
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            ViewModel.Dispose();
        }
    }

}
