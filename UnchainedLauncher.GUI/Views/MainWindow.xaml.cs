using PropertyChanged;
using System;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : UnchainedWindow {
        public MainWindow(MainWindowVM vm) {
            DataContext = ViewModel = vm;
            InitializeComponent();
            Closed += MainWindow_Closed;
        }

        public MainWindowVM ViewModel { get; }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            ViewModel.Dispose();
        }
    }
}