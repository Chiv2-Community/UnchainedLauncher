using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace C2GUILauncher.ViewModels {

    [AddINotifyPropertyChangedInterface]
    public class MessageBoxExViewModel {
        public string TitleText { get; }
        public string MessageText { get; }
        public string YesButtonText { get; }
        public string NoButtonText { get; }
        public string CancelButtonText { get; }

        public MessageBoxResult Result { get; private set; }

        public ICommand YesCommand { get; set; }
        public ICommand NoCommand { get; set; }
        public ICommand CancelCommand { get; set; }

        private Action CloseWindow { get; }

        public Window Window { get; }

        public MessageBoxExViewModel(string titleText, string messageText, string yesButtonText, string noButtonText, string cancelButtonText, Action closeWindow) {
            TitleText = titleText;
            MessageText = messageText;
            YesButtonText = yesButtonText;
            NoButtonText = noButtonText;
            CancelButtonText = cancelButtonText;

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

}
