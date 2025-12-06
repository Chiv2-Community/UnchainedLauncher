using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Views;

namespace UnchainedLauncher.GUI.Views {
    public partial class ProgressWindow : UnchainedWindow {
        public MemoryProgress Progress { get; private set; }
        public ProgressWindow(MemoryProgress progress) {
            DataContext = Progress = progress;
            InitializeComponent();
        }
    }
}