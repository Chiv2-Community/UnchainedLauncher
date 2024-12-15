using PropertyChanged;
using System;
using System.Windows;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
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