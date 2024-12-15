using System.ComponentModel;
using System.Threading.Tasks;

namespace UnchainedLauncher.GUI.ViewModels.Installer {

    public interface IInstallerPageViewModel : INotifyPropertyChanged {

        public string TitleText { get; }
        public string? DescriptionText { get; }

        public bool CanContinue { get; }
        public string ContinueButtonText { get; }

        public bool CanGoBack { get; }
        public string? GoBackButtonText { get; }


        public Task Load();
        public Task Continue();

    }
}