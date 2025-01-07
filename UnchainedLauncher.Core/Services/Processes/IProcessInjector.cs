using System.Diagnostics;

namespace UnchainedLauncher.Core.Services.Processes {
    
    /// <summary>
    /// Injects processes with some alternate code, like a DLL.
    /// </summary>
    public interface IProcessInjector {
        public bool Inject(Process p);
    }
}