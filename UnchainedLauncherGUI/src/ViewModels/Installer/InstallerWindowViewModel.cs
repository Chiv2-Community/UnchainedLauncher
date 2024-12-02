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
using System.Windows.Input;

namespace UnchainedLauncher.GUI.ViewModels.Installer
{
    public partial class InstallerWindowViewModel: INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(typeof(VersionSelectionPageViewModel));

        private static readonly ObservableCollection<IInstallerPageViewModel> DefaultPages = new ObservableCollection<IInstallerPageViewModel>() {
            new InstallationSelectionPageViewModel(),
            new VersionSelectionPageViewModel(),
            new InstallationLogPageViewModel()
        };

        public ObservableCollection<IInstallerPageViewModel> InstallerPages { get; set; }
        public IInstallerPageViewModel CurrentPage { get { return InstallerPages[CurrentPageIndex]; } }
        public int CurrentPageIndex { get; set; }
        public string CurrentPageTitle { get { return CurrentPage.TitleText; } }
        public string CurrentPageDescription { get { return CurrentPage.DescriptionText ?? ""; } }

        public string ContinueButtonText { get { return CurrentPage.ContinueButtonText; } }
        public bool CanContinue { get; set; }

        public string GoBackButtonText { get { return CurrentPage.GoBackButtonText; } }
        public bool CanGoBack { get { return CurrentPage.CanGoBack; } }
        public bool Finished { get; set; }

        public bool DisplayDescription { get; set; }
        public string DescriptionColumnWidth => DisplayDescription ? "1*" : "0";
        public string PageColumnWidth => DisplayDescription ? "2*" : "1*";
        public ICommand NextButtonCommand { get; }
        public ICommand BackButtonCommand { get; }


        public InstallerWindowViewModel(): this(DefaultPages) { }

        public InstallerWindowViewModel(ObservableCollection<IInstallerPageViewModel> installerPages) {
            InstallerPages = installerPages;
            CurrentPageIndex = 0;
            Finished = false;
            CanContinue = false;


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
            CurrentPageIndex++;
            UpdateCurrentPage();

            if (CurrentPageIndex == InstallerPages.Count) {
                logger.Info("Installer finished");
                Finished = true;
                return;
            }
            await CurrentPage.Load();
            UpdateCurrentPage();
        }

        private async Task PreviousPage() {
            CurrentPageIndex--;
            await CurrentPage.Load();
            UpdateCurrentPage();
        }

        private void CurrentPagePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender != null && sender == CurrentPage){
                if(e.PropertyName == "CanContinue") {
                    UpdateCurrentPage();
                }   
            }
        }

        private void UpdateCurrentPage() {
            if(CurrentPageIndex < 0 || CurrentPageIndex >= InstallerPages.Count) {
                return;
            }

            CanContinue = CurrentPage.CanContinue;
            DisplayDescription = CurrentPage.DescriptionText != null;
        }
    }
}
