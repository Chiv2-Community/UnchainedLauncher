using System;
using System.Windows;
using System.Windows.Input;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    /// Base window that centralizes window command bindings and common title bar behaviors.
    /// Inherit from this class for all app windows to get working minimize/close buttons
    /// and default drag/maximize handling.
    ///
    /// Make sure to set Style="{StaticResource UnchainedChrome}" on your window.
    /// </summary>
    public class UnchainedWindow : Window {
        static UnchainedWindow() {
            // Register command bindings on this class type so any derived window benefits.
            CommandManager.RegisterClassCommandBinding(typeof(UnchainedWindow),
                new CommandBinding(SystemCommands.CloseWindowCommand, OnCloseWindow));

            CommandManager.RegisterClassCommandBinding(typeof(UnchainedWindow),
                new CommandBinding(SystemCommands.MinimizeWindowCommand, OnMinimizeWindow));

            CommandManager.RegisterClassCommandBinding(typeof(UnchainedWindow),
                new CommandBinding(SystemCommands.MaximizeWindowCommand, OnMaximizeWindow));

            CommandManager.RegisterClassCommandBinding(typeof(UnchainedWindow),
                new CommandBinding(SystemCommands.RestoreWindowCommand, OnRestoreWindow));
        }

        public bool ShowWindowTitle {
            get => (bool)GetValue(ShowWindowTitleProperty);
            set => SetValue(ShowWindowTitleProperty, value);
        }

        public static readonly DependencyProperty ShowWindowTitleProperty =
            DependencyProperty.Register(
                nameof(ShowWindowTitle),
                typeof(bool),
                typeof(UnchainedWindow),
                new PropertyMetadata(true, OnUseStandardChromeChanged));

        private static void OnUseStandardChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }

        private static void OnCloseWindow(object sender, ExecutedRoutedEventArgs e) {
            var win = (e.Parameter as Window) ?? Window.GetWindow(sender as DependencyObject);
            if (win != null) SystemCommands.CloseWindow(win);
        }

        private static void OnMinimizeWindow(object sender, ExecutedRoutedEventArgs e) {
            var win = (e.Parameter as Window) ?? Window.GetWindow(sender as DependencyObject);
            if (win != null) SystemCommands.MinimizeWindow(win);
        }

        private static void OnMaximizeWindow(object sender, ExecutedRoutedEventArgs e) {
            var win = (e.Parameter as Window) ?? Window.GetWindow(sender as DependencyObject);
            if (win != null) SystemCommands.MaximizeWindow(win);
        }

        private static void OnRestoreWindow(object sender, ExecutedRoutedEventArgs e) {
            var win = (e.Parameter as Window) ?? Window.GetWindow(sender as DependencyObject);
            if (win != null) SystemCommands.RestoreWindow(win);
        }

    }
}
