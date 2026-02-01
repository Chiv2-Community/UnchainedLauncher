using System.Threading;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Views {
    public partial class ProgressWindow : UnchainedWindow {
        public ProgressWindow(MemoryProgress progress) {
            // If this is an AccumulatedMemoryProgress and it doesn't have a SynchronizationContext,
            // inject the current UI thread's context
            if (progress is AccumulatedMemoryProgress accProgress) {
                // Create a new instance with the UI SynchronizationContext
                var uiContext = SynchronizationContext.Current;
                if (uiContext != null) {
                    progress = new AccumulatedMemoryProgress(
                        accProgress.Progresses,
                        accProgress.TaskName,
                        uiContext
                    );
                }
            }
            
            DataContext = Progress = progress;
            InitializeComponent();
        }

        public MemoryProgress Progress { get; private set; }
    }
}