using PropertyChanged;
using System;
using System.ComponentModel;
using System.Windows;
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

            // Bridge VM visibility intent to actual Window.Show/Hide calls.
            // Binding to Window.Visibility is not reliable for showing/hiding a Window in WPF.
            vm.HomeVM.PropertyChanged += HomeVmOnPropertyChanged;

            Closed += MainWindow_Closed;
        }

        public MainWindowVM ViewModel { get; }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            ViewModel.Dispose();
        }

        private void HomeVmOnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(HomeVM.MainWindowVisibility)) return;

            var desired = ViewModel.HomeVM.MainWindowVisibility;
            if (desired == Visibility.Visible) {
                if (!IsVisible) Show();
            }
            else {
                if (IsVisible) Hide();
            }
        }
    }
}