using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using UnchainedLauncher.Core.Mods;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    public partial class UpdatesWindowViewModel : INotifyPropertyChanged {
        public string TitleText { get; }
        public string MessageText { get; }
        public string YesButtonText { get; }
        public string NoButtonText { get; }
        public Option<string> CancelButtonText { get; }
        public bool ShowCancelButton => CancelButtonText.IsSome;
        public string CancelColumnWidth => ShowCancelButton ? "40*" : "0";
        public string NoButtonMargin => ShowCancelButton ? "5,10,0,10" : "5,10,10,10";

        public IEnumerable<DependencyUpdate> Updates { get; }

        public MessageBoxResult Result { get; private set; }

        public ICommand YesCommand { get; set; }
        public ICommand NoCommand { get; set; }
        public ICommand CancelCommand { get; set; }
        private Action CloseWindow { get; }

        public UpdatesWindowViewModel() : this("Title", "Message", "Yes", "No", None, new List<DependencyUpdate>(), () => { }) { }

        public UpdatesWindowViewModel(string titleText, string messageText, string yesButtonText, string noButtonText, Option<string> cancelButtonText, IEnumerable<DependencyUpdate> updates, Action closeWindow) {
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

    public record DependencyUpdate(string Name, Option<string> CurrentVersion, string LatestVersion, string ReleaseUrl, string Reason) {
        public string CurrentVersionString => CurrentVersion.IfNone("None");
        public string VersionString => CurrentVersion == null ? LatestVersion : $"{CurrentVersionString} -> {LatestVersion}";
        public ICommand HyperlinkCommand => new RelayCommand(Hyperlink_Click);

        public void Hyperlink_Click() {
            Process.Start(new ProcessStartInfo { FileName = ReleaseUrl, UseShellExecute = true });
        }

        public static DependencyUpdate FromUpdateCandidate(UpdateCandidate modUpdate) {
            return new DependencyUpdate(modUpdate.CurrentlyEnabled.Manifest.Name, modUpdate.CurrentlyEnabled.Tag, modUpdate.AvailableUpdate.Tag, modUpdate.AvailableUpdate.ReleaseUrl, "");
        }
    };

}