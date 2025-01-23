using System.Diagnostics;

namespace UnchainedLauncher.Core.Services.Processes {

    /// <summary>
    /// Injects processes with some alternate code, like a DLL.
    /// </summary>
    public interface IProcessInjector {
        public bool Inject(Process p);
    }


    /// <summary>
    /// Pretends to inject code, but really just always fails or always
    /// succeeds based on constructor arg
    /// </summary>
    public class NullInjector : IProcessInjector {
        private readonly bool _result;

        public NullInjector(bool result = true) {
            _result = result;
        }
        public bool Inject(Process p) => _result;
    }
}