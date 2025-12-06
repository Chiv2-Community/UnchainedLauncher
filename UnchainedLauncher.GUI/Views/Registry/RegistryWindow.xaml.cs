using System.Windows;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.Registry {
    public partial class RegistryWindow {
        public RegistryWindowVM ViewModel { get; }
        public RegistryWindow(RegistryWindowVM vm) {
            InitializeComponent();
            DataContext = ViewModel = vm;
        }
    }
}