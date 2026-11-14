using CommunityToolkit.Mvvm.Input;
using log4net;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.GUI.Views.Installer;


namespace UnchainedLauncher.GUI.ViewModels.Installer {
    public partial class InstallerWindowViewModel : INotifyPropertyChanged {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(VersionSelectionPageViewModel));

        private static ObservableCollection<IInstallerPageViewModel> DefaultPages => new ObservableCollection<IInstallerPageViewModel>() {
            new InstallationSelectionPageViewModel(),
            new VersionSelectionPageViewModel(),
            new InstallerLogPageViewModel()
        };

        public ObservableCollection<IInstallerPageViewModel> InstallerPages { get; set; }
        public IInstallerPageViewModel CurrentPage => InstallerPages[CurrentPageIndex];
        private int _currentPageIndex;
        public int CurrentPageIndex {
            get => _currentPageIndex;
            set {
                if (value < 0 || value >= InstallerPages.Count) return;
                if (_currentPageIndex != value) {
                    // CurrentPage is a computed property based on _currentPageIndex.
                    // We need to unsubscribe from the old page's PropertyChanged event,
                    // update the index, and then subscribe to the new page's event.

                    // Unsubscribe from the OLD page
                    CurrentPage.PropertyChanged -= CurrentPagePropertyChanged;

                    _currentPageIndex = value;

                    // Subscribe to the NEW page
                    CurrentPage.PropertyChanged += CurrentPagePropertyChanged;
                }
            }
        }
        public string CurrentPageTitle => CurrentPage.TitleText;
        public string CurrentPageDescription => CurrentPage.DescriptionText ?? "";

        public string ContinueButtonText => CurrentPage.ContinueButtonText;
        public bool CanContinue { get; set; }

        public string GoBackButtonText => CurrentPage.GoBackButtonText ?? "";
        public bool CanGoBack { get; set; }
        public bool Finished { get; set; }
        public Visibility WindowVisibility { get; set; }
        public Visibility DisplayDescription { get; set; }
        public Visibility DisplayGoBackButton { get; set; }

        public string DescriptionColumnWidth => DisplayDescription == Visibility.Visible ? "1*" : "0";
        public string PageColumnWidth => DisplayDescription == Visibility.Visible ? "2*" : "1*";

        public readonly ObservableCollection<InstallationTargetViewModel> InstallTargets;
        private bool _navigating;


        public InstallerWindowViewModel() : this(DefaultPages, new ObservableCollection<InstallationTargetViewModel> { new() }) { }

        public InstallerWindowViewModel(ObservableCollection<IInstallerPageViewModel> installerPages, ObservableCollection<InstallationTargetViewModel> installTargets) {
            InstallerPages = installerPages;
            InstallTargets = installTargets;

            Finished = false;
            CanContinue = false;
            CanGoBack = false;

            WindowVisibility = Visibility.Visible;

            _currentPageIndex = 0;
            CurrentPage.PropertyChanged += CurrentPagePropertyChanged;

            _ = LoadCurrentPage();

            Logger.Info("InstallerWindowViewModel initialized");
            Logger.Info("Current Page: " + CurrentPage.TitleText);

            UpdateCurrentPage();
        }

        [RelayCommand]
        private async Task NextButton() {
            if (Finished || _navigating || !CurrentPage.CanContinue) return;
            if (CurrentPage is InstallationSelectionPageViewModel && !InstallTargets.Any(t => t.IsSelected)) return;
            _navigating = true;
            UpdateCurrentPage();
            try {
                await CurrentPage.Continue();

                if ((CurrentPageIndex + 1) == InstallerPages.Count) {
                    FinalizeInstallation();
                    return;
                }

                CurrentPageIndex++;
                UpdateCurrentPage();
                await CurrentPage.Load();
            }
            catch (Exception ex) {
                Logger.Error("Error loading installer page", ex);
                MessageBox.Show("Error loading installer page: " + ex.Message);
            }
            finally {
                _navigating = false;
                UpdateCurrentPage();
            }
        }

        private async Task LoadCurrentPage() {
            _navigating = true;
            UpdateCurrentPage();
            try {
                await CurrentPage.Load();
            }
            catch (Exception ex) {
                Logger.Error("Error loading installer page", ex);
                MessageBox.Show("Error loading installer page: " + ex.Message);
            }
            finally {
                _navigating = false;
                UpdateCurrentPage();
            }
        }

        private void FinalizeInstallation() {
            Logger.Info("Installer finished");
            WindowVisibility = Visibility.Hidden;
            var result = MessageBox.Show("Chivalry 2 Unchained Launcher has been installed successfully! Would you like to launch it now?", "Installation Complete", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes) {
                var targets = InstallTargets.Where(t => t.IsSelected);
                if (CurrentPage is InstallerLogPageViewModel logPage)
                    targets = targets.Where(t => logPage.SuccessfulTargets.Any(s => s.FullName == t.Path.FullName));
                var launchTargets = new ObservableCollection<InstallationTargetViewModel>(targets);
                if (launchTargets.Count > 0)
                    LaunchUnchainedLauncherWindow.Show(launchTargets);
            }
            Finished = true;
        }

        [RelayCommand]
        private Task BackButton() {
            if (_navigating || CurrentPageIndex <= 0 || !CurrentPage.CanGoBack) return Task.CompletedTask;
            CurrentPageIndex--;
            UpdateCurrentPage();
            return Task.CompletedTask;
        }

        private void CurrentPagePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender != null && sender == CurrentPage) {
                if (e.PropertyName == "CanContinue" || e.PropertyName == "CanGoBack") {
                    UpdateCurrentPage();
                }
            }
        }

        private void UpdateCurrentPage() {
            if (CurrentPageIndex < 0 || CurrentPageIndex >= InstallerPages.Count) {
                return;
            }

            CanContinue = !_navigating && CurrentPage.CanContinue;
            CanGoBack = !_navigating && CurrentPageIndex > 0 && CurrentPage.CanGoBack;
            DisplayDescription = CurrentPage.DescriptionText != null ? Visibility.Visible : Visibility.Hidden;
            DisplayGoBackButton = CurrentPageIndex > 0 && CurrentPage.GoBackButtonText != null ? Visibility.Visible : Visibility.Hidden;
        }
    }
}