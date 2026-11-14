using log4net;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.Views.Servers.Sections {
    public partial class ServerModActorsSectionView : UserControl {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ServerModActorsSectionView));
        private FileSystemWatcher? watcher;
        private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

        public ServerModActorsSectionView() {
            InitializeComponent();
            Loaded += (_, _) => ObserveLocalMods();
            Unloaded += (_, _) => StopObserving();
            DataContextChanged += (_, _) => { if (IsLoaded) ObserveLocalMods(); };
            refreshTimer.Tick += async (_, _) => {
                refreshTimer.Stop();
                if (DataContext is ServerConfigurationVM vm) await vm.LocalModScan.RefreshAsync();
            };
        }

        private async void ObserveLocalMods() {
            StopObserving();
            if (DataContext is not ServerConfigurationVM vm) return;
            var directory = vm.LocalModScan.PakDirectory;
            try {
                if (Directory.Exists(directory)) {
                    watcher = new FileSystemWatcher(directory, "*.pak") {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    watcher.Created += OnPakChanged;
                    watcher.Deleted += OnPakChanged;
                    watcher.Changed += OnPakChanged;
                    watcher.Renamed += OnPakChanged;
                    watcher.Error += (_, _) => QueueRefresh();
                    watcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                StopObserving();
                Logger.Warn("Could not watch the local pak directory.", error);
            }
            await vm.LocalModScan.RefreshAsync();
        }

        private void OnPakChanged(object sender, FileSystemEventArgs args) => QueueRefresh();

        private void QueueRefresh() => Dispatcher.InvokeAsync(() => {
            if (!IsLoaded) return;
            refreshTimer.Stop();
            refreshTimer.Start();
        });

        private void StopObserving() {
            watcher?.Dispose();
            watcher = null;
            refreshTimer.Stop();
        }

        private async void ScanLocalMods_OnClick(object sender, RoutedEventArgs e) {
            e.Handled = true;
            if (DataContext is not ServerConfigurationVM vm) return;
            try { await vm.LocalModScan.ScanAsync(); }
            catch (Exception error) {
                MessageBox.Show($"Pak scan failed: {error.Message}", "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CheckBox_OnChecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerConfigurationVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is BlueprintDto blueprint) {
                vm.EnableServerBlueprintMod(blueprint);
            }
        }

        private void CheckBox_OnUnchecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerConfigurationVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is BlueprintDto blueprint) {
                vm.DisableServerBlueprintMod(blueprint);
            }
        }
    }
}
