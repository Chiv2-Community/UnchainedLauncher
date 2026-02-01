using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Views {
    public partial class ProgressWindow : UnchainedWindow {
        public ProgressWindow(MemoryProgress progress) {
            DataContext = Progress = progress;
            InitializeComponent();
        }

        public MemoryProgress Progress { get; private set; }
    }
}