using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Views.Progress.DesignInstances {
    public static class MemoryProgressDesignInstances {
        public static MemoryProgress DEFAULT_PROGRESS => new MemoryProgressDesignVM();
        public static ProgressWindow DEFAULT_WINDOW => new ProgressWindowDesignVM();
    }

    public class MemoryProgressDesignVM : MemoryProgress {
        public MemoryProgressDesignVM() : base("Downloading something...", 30) { }
    }

    public class ProgressWindowDesignVM : ProgressWindow {
        public ProgressWindowDesignVM() : base(MemoryProgressDesignInstances.DEFAULT_PROGRESS) { }
    }
}