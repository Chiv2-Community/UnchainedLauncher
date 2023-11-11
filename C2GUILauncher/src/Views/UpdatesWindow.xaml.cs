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
    public partial class UpdatesWindow : Window {

        public UpdatesWindowViewModel ViewModel { get; private set; }
        public MessageBoxResult Result => ViewModel.Result;

        public UpdatesWindow(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) {
            InitializeComponent();
            ViewModel = new UpdatesWindowViewModel(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates, Close);
            DataContext = ViewModel;
        }

        public static MessageBoxResult Show(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) {
            var window = new UpdatesWindow(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates);
            window.ShowDialog();
            return window.ViewModel.Result;
        }
    }
}
