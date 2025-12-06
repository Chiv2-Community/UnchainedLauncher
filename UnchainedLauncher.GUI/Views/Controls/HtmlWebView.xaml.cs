using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace UnchainedLauncher.GUI.Views.Controls {
    /// <summary>
    /// Simple reusable WebView2 wrapper that accepts raw HTML via the Html dependency property.
    /// Initializes an isolated user data folder per control instance to avoid state bleed.
    /// </summary>
    public partial class HtmlWebView : UserControl {
        // Shared app-wide WebView2 environment to avoid per-control mismatches
        private static readonly object EnvLock = new();
        private static Task<CoreWebView2Environment>? _sharedEnvTask;

        private bool _initialized;
        private string _lastNavigated = string.Empty;
        private bool _pendingSizeRefresh;
        private readonly SemaphoreSlim _initGate = new(1, 1);

        public HtmlWebView() {
            InitializeComponent();
            Loaded += async (_, _) => await EnsureInitialized();
            WebView.SizeChanged += WebView_SizeChanged;
        }

        public static readonly DependencyProperty HtmlProperty = DependencyProperty.Register(
            nameof(Html), typeof(string), typeof(HtmlWebView),
            new PropertyMetadata(string.Empty, OnHtmlChanged));

        public string Html {
            get => (string)GetValue(HtmlProperty);
            set => SetValue(HtmlProperty, value);
        }

        private static async void OnHtmlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is HtmlWebView ctrl) {
                await ctrl.EnsureInitialized();
                ctrl.NavigateToHtml(e.NewValue as string ?? string.Empty);
                // If size is 0 (not yet measured), mark for a refresh once sized
                if (ctrl.ActualWidth <= 0 || ctrl.ActualHeight <= 0) ctrl._pendingSizeRefresh = true;
            }
        }

        private async Task EnsureInitialized() {
            if (_initialized) return;

            await _initGate.WaitAsync();
            
            // Another thread may have already initialized this while waiting, so check again
            if (_initialized) return;
            
            try {

                
                var environment = await GetSharedEnvironmentAsync();
                await WebView.EnsureCoreWebView2Async(environment);
                _initialized = true;

                // After init, if Html already set but not navigated (due to race), navigate now
                if (!string.IsNullOrEmpty(Html) && _lastNavigated != Html) {
                    NavigateToHtml(Html);
                }
            }
            finally {
                _initGate.Release();
            }
        }

        private static Task<CoreWebView2Environment> GetSharedEnvironmentAsync() {
            lock (EnvLock) {
                if (_sharedEnvTask != null) return _sharedEnvTask;

                // Use a stable, app-specific user data folder to share cookies/cache and avoid conflicts
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var userDataFolder = Path.Combine(localAppData, "UnchainedLauncher", "WebView2");
                Directory.CreateDirectory(userDataFolder);
                _sharedEnvTask = CoreWebView2Environment.CreateAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions());
                return _sharedEnvTask;
            }
        }

        private void NavigateToHtml(string html) {
            try {
                var toNav = html ?? string.Empty;
                _lastNavigated = toNav;
                WebView.NavigateToString(toNav);
            }
            catch (Exception) {
                // ignore navigation issues; this is best-effort rendering
            }
        }

        private void WebView_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (!_initialized) return;
            if (!_pendingSizeRefresh && !string.IsNullOrEmpty(_lastNavigated)) return;
            if (ActualWidth > 0 && ActualHeight > 0 && !string.IsNullOrEmpty(Html)) {
                _pendingSizeRefresh = false;
                NavigateToHtml(Html);
            }
        }
    }
}
