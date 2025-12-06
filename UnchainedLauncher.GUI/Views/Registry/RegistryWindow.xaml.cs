using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.Registry {
    public partial class RegistryWindow {
        public RegistryWindow(RegistryWindowVM vm) {
            InitializeComponent();
            DataContext = ViewModel = vm;
        }

        public RegistryWindowVM ViewModel { get; }
    }
}