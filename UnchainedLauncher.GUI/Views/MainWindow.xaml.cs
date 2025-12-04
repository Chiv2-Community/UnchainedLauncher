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
        public MainWindowVM ViewModel { get; }

        public MainWindow(MainWindowVM vm) {
            DataContext = ViewModel = vm;
            InitializeComponent();
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            ViewModel.Dispose();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            try {
                if (e.ClickCount == 2) {
                    // Toggle maximize on double-click
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                } else {
                    DragMove();
                }
            } catch {
                // ignore drag exceptions (e.g., starting drag during maximize)
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            Close();
        }
    }

}