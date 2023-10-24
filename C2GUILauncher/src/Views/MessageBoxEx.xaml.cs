using C2GUILauncher.ViewModels;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace C2GUILauncher.Views {
    /// <summary>
    /// Interaction logic for MessageBoxEx.xaml
    /// </summary>
    public partial class MessageBoxEx : Window {

        public MessageBoxExViewModel ViewModel { get; private set; }
        public MessageBoxResult Result => ViewModel.Result;

        public MessageBoxEx(string titleText, string messageText, string yesButtonText, string noButtonText, string cancelButtonText) {
            InitializeComponent();
            ViewModel = new MessageBoxExViewModel(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, Close);
            DataContext = ViewModel;
        }

        public static MessageBoxResult Show(string titleText, string messageText, string yesButtonText, string noButtonText, string cancelButtonText) {
            var window = new MessageBoxEx(titleText, messageText, yesButtonText, noButtonText, cancelButtonText);
            window.ShowDialog();
            return window.ViewModel.Result;
        }
    }
}
