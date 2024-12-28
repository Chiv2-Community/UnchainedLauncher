using PropertyChanged;
using System;
using System.Windows;
using UnchainedLauncher.GUI.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : FluentWindow {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow(INavigationService navigationService, MainWindowViewModel vm) {
            DataContext = ViewModel = vm;
            InitializeComponent();
            Closed += MainWindow_Closed;

            navigationService.SetNavigationControl(Navigation);

        }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            ViewModel.Dispose();
            Application.Current.Shutdown();
        }
    }
}