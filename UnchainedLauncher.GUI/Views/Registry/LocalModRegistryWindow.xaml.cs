using System.Windows;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.Registry {
    public partial class LocalModRegistryWindow {
        public LocalModRegistryWindow(LocalModRegistryWindowVM vm) {
            InitializeComponent();
            DataContext = vm;
        }
    }
}