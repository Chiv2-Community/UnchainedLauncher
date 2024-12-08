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
using System.Windows.Navigation;
using System.Windows.Shapes;
using UnchainedLauncher.GUI.ViewModels.Installer;

namespace UnchainedLauncher.GUI.Views.Installer
{
    public partial class InstallerWindow : Window
    {
        public InstallerWindow(InstallerWindowViewModel installerWindowViewModel) 
        {
            DataContext = installerWindowViewModel;
            InitializeComponent();

            installerWindowViewModel.PropertyChanged += (sender, args) => {
                if (args.PropertyName == nameof(installerWindowViewModel.Finished)) {
                    if(installerWindowViewModel.Finished) {
                        Close();
                    }
                } else if (args.PropertyName == nameof(installerWindowViewModel.WindowVisibility)) {
                    // This is a janky hack because my visibility binding isn't working
                    if(installerWindowViewModel.WindowVisibility == Visibility.Hidden) {
                        Hide();
                    }
                }
            };
        }
    }
}
