// Unused update-path dependencies let the real HelpVM commands run without WPF.
namespace UnchainedLauncher.GUI.ViewModels {
    public sealed class SettingsVM {
        public Semver.SemVersion Version => throw new NotSupportedException();
        public string CurrentVersion => throw new NotSupportedException();
    }
}

namespace System.Windows {
    public static class MessageBox {
        public static void Show(string message) => throw new NotSupportedException();
    }
}
