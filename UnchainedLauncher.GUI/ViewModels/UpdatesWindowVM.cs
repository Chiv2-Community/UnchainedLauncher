using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using UnchainedLauncher.Core.Services;
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

        private Action CloseWindow { get; }

        public UpdatesWindowVM(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdateViewModel> updates, Action closeWindow) {
            TitleText = titleText;
            MessageText = messageText;
            YesButtonText = yesButtonText;
            NoButtonText = noButtonText;
            CancelButtonText = cancelButtonText;

            Updates = updates;
            CloseWindow = closeWindow;
            Result = MessageBoxResult.None;
        }

        [RelayCommand]
        public void Yes() {
            Result = MessageBoxResult.Yes;
            CloseWindow();
        }

        [RelayCommand]
        public void No() {
            Result = MessageBoxResult.No;
            CloseWindow();
        }

        [RelayCommand]
        public void Cancel() {
            Result = MessageBoxResult.Cancel;
            CloseWindow();
        }


    }

    public record DependencyUpdateViewModel(DependencyUpdate Update) {
        public string CurrentVersionString => Update.CurrentVersion ?? "None";
        public string VersionString => Update.CurrentVersion == null ? Update.LatestVersion : $"{CurrentVersionString} -> {Update.LatestVersion}";

        [RelayCommand]
        public void Hyperlink() {
            Process.Start(new ProcessStartInfo { FileName = Update.ReleaseUrl, UseShellExecute = true });
        }
    };

}