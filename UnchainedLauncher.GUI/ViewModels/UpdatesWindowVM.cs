using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class UpdatesWindowVM : INotifyPropertyChanged {
        public string TitleText { get; }
        public string MessageText { get; }
        public string YesButtonText { get; }
        public string NoButtonText { get; }
        public string? CancelButtonText { get; }
        public bool ShowCancelButton => CancelButtonText != null;
        public string CancelColumnWidth => ShowCancelButton ? "40*" : "0";
        public string NoButtonMargin => ShowCancelButton ? "5,10,0,10" : "5,10,10,10";

        public IEnumerable<DependencyUpdateViewModel> Updates { get; }

        public MessageBoxResult Result { get; private set; }

        public ICommand YesCommand { get; set; }
        public ICommand NoCommand { get; set; }
        public ICommand CancelCommand { get; set; }
        private Action CloseWindow { get; }

        public UpdatesWindowVM(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdateViewModel> updates, Action closeWindow) {
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

    public record DependencyUpdateViewModel(DependencyUpdate Update) {
        public string CurrentVersionString => Update.CurrentVersion ?? "None";
        public string VersionString => Update.CurrentVersion == null ? Update.LatestVersion : $"{CurrentVersionString} -> {Update.LatestVersion}";
        public ICommand HyperlinkCommand => new RelayCommand(Hyperlink_Click);

        public void Hyperlink_Click() {
            Process.Start(new ProcessStartInfo { FileName = Update.ReleaseUrl, UseShellExecute = true });
        }
    };

}