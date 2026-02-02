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
            ContentRendered += MainWindow_ContentRendered;
        }

        /// <summary>
        /// Brings the window to the foreground after content is rendered.
        /// This is necessary because when launched from external applications (like EGS),
        /// the window may appear behind other windows due to Windows' foreground restrictions.
        /// </summary>
        private void MainWindow_ContentRendered(object? sender, EventArgs e) {
            // Unsubscribe immediately - we only need this to run once on startup
            ContentRendered -= MainWindow_ContentRendered;

            BringToForeground();
        }

        /// <summary>
        /// Forcefully brings the window to the foreground.
        /// Uses the Topmost toggle trick to bypass Windows' foreground window restrictions.
        /// </summary>
        private void BringToForeground() {
            // First try standard activation
            Activate();

            // If the window still isn't in the foreground, use the Topmost trick
            // This works around Windows' restrictions on SetForegroundWindow
            if (!IsActive) {
                Topmost = true;
                Topmost = false;
            }

            Focus();
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
                BringToForeground();
            }
            else {
                if (IsVisible) Hide();
            }
        }
    }
}