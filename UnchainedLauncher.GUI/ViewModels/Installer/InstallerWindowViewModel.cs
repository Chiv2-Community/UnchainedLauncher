using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.GUI.Views.Installer;


namespace UnchainedLauncher.GUI.ViewModels.Installer
{
    public partial class InstallerWindowViewModel: INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(typeof(VersionSelectionPageViewModel));

        private static readonly ObservableCollection<IInstallerPageViewModel> DefaultPages = new ObservableCollection<IInstallerPageViewModel>() {
            new InstallationSelectionPageViewModel(),
            new VersionSelectionPageViewModel(),
            new InstallerLogPageViewModel()
        };

        public ObservableCollection<IInstallerPageViewModel> InstallerPages { get; set; }
        public IInstallerPageViewModel CurrentPage { get { return InstallerPages[CurrentPageIndex]; } }
        public int CurrentPageIndex { get; set; }
        public string CurrentPageTitle { get { return CurrentPage.TitleText; } }
        public string CurrentPageDescription { get { return CurrentPage.DescriptionText ?? ""; } }

        public string ContinueButtonText { get { return CurrentPage.ContinueButtonText; } }
        public bool CanContinue { get; set; }

        public string GoBackButtonText { get { return CurrentPage.GoBackButtonText ?? ""; } }
        public bool CanGoBack { get; set; }
        public bool Finished { get; set; }
        public Visibility WindowVisibility { get; set; }
        public Visibility DisplayDescription { get; set; }
        public Visibility DisplayGoBackButton { get; set; }

        public string DescriptionColumnWidth => DisplayDescription == Visibility.Visible ? "1*" : "0";
        public string PageColumnWidth => DisplayDescription == Visibility.Visible ? "2*" : "1*";
        public ICommand NextButtonCommand { get; }
        public ICommand BackButtonCommand { get; }

        public ObservableCollection<InstallationTargetViewModel> InstallTargets;


        public InstallerWindowViewModel(): this(DefaultPages, new ObservableCollection<InstallationTargetViewModel> { new()}) { }

        public InstallerWindowViewModel(ObservableCollection<IInstallerPageViewModel> installerPages, ObservableCollection<InstallationTargetViewModel> installTargets) {
            InstallerPages = installerPages;
            InstallTargets = installTargets;

            CurrentPageIndex = 0;
            Finished = false;
            CanContinue = false;
            CanGoBack = false;

            WindowVisibility = Visibility.Visible;

            NextButtonCommand = new AsyncRelayCommand(NextPage);
            BackButtonCommand = new AsyncRelayCommand(PreviousPage);

            CurrentPage.Load().RunSynchronously();

            InstallerPages.ToList().ForEach(page => page.PropertyChanged += CurrentPagePropertyChanged);

            logger.Info("InstallerWindowViewModel initialized");
            logger.Info("Current Page: " + CurrentPage.TitleText);

            UpdateCurrentPage();
        }

        private async Task NextPage() {
            await CurrentPage.Continue();

            if ((CurrentPageIndex + 1) == InstallerPages.Count) {
                FinalizeInstallation();
                return;
            }

            CurrentPageIndex++;
            UpdateCurrentPage();

           
            await CurrentPage.Load();
            UpdateCurrentPage();
        }

        private void FinalizeInstallation() {
            logger.Info("Installer finished");
            WindowVisibility = Visibility.Hidden;
            var result = MessageBox.Show("Chivalry 2 Unchained Launcher has been installed successfully! Would you like to launch it now?", "Installation Complete", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes) {
                LaunchUnchainedLauncherWindow.Show(InstallTargets);
            }
            Finished = true;
        }

        private async Task PreviousPage() {
            CurrentPageIndex--;
            await CurrentPage.Load();
            UpdateCurrentPage();
        }

        private void CurrentPagePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender != null && sender == CurrentPage){
                if(e.PropertyName == "CanContinue" || e.PropertyName == "CanGoBack") {
                    UpdateCurrentPage();
                }   
            }
        }

        private void UpdateCurrentPage() {
            if(CurrentPageIndex < 0 || CurrentPageIndex >= InstallerPages.Count) {
                return;
            }

            CanContinue = CurrentPage.CanContinue;
            CanGoBack = CurrentPage.CanGoBack;
            DisplayDescription = CurrentPage.DescriptionText != null ? Visibility.Visible : Visibility.Hidden;
            DisplayGoBackButton = CurrentPage.GoBackButtonText != null ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
