using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace C2GUILauncher.ViewModels {

    [AddINotifyPropertyChangedInterface]
    public class UpdatesWindowViewModel {
        public string TitleText { get; }
        public string MessageText { get; }
        public string YesButtonText { get; }
        public string NoButtonText { get; }
        public string? CancelButtonText { get; }
        public bool ShowCancelButton => CancelButtonText != null;
        public string CancelColumnWidth => ShowCancelButton ? "40*" : "0";
        public string NoButtonMargin => ShowCancelButton ? "5,10,0,10" : "5,10,10,10";

        public IEnumerable<DependencyUpdate> Updates { get; }

        public MessageBoxResult Result { get; private set; }

        public ICommand YesCommand { get; set; }
        public ICommand NoCommand { get; set; }
        public ICommand CancelCommand { get; set; }
        private Action CloseWindow { get; }

        public UpdatesWindowViewModel(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates, Action closeWindow) {
            TitleText = titleText;
            MessageText = messageText;
            YesButtonText = yesButtonText;
            NoButtonText = noButtonText;
            CancelButtonText = cancelButtonText;

            Updates = updates;

            YesCommand = new RelayCommand(YesButton_Click);
            NoCommand = new RelayCommand(NoButton_Click);
            CancelCommand = new RelayCommand(CancelButton_Click);

            CloseWindow = closeWindow;

            Result = MessageBoxResult.None;
        }

        public void YesButton_Click() {
            Result = MessageBoxResult.Yes;
            CloseWindow();
        }

        public void NoButton_Click() {
            Result = MessageBoxResult.No;
            CloseWindow();
        }

        public void CancelButton_Click() {
            Result = MessageBoxResult.Cancel;
            CloseWindow();
        }


    }

    public record DependencyUpdate(string Name, string? CurrentVersion, string LatestVersion, string ReleaseUrl, string Reason) {
        public string VersionString => CurrentVersion == null ? LatestVersion : $"{CurrentVersion} -> {LatestVersion}";
        public ICommand HyperlinkCommand => new RelayCommand(Hyperlink_Click);

        public void Hyperlink_Click() {
            Process.Start(new ProcessStartInfo { FileName = ReleaseUrl, UseShellExecute = true });
        }
    };

}
