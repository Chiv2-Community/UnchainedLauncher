using log4net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Dlls;

namespace UnchainedLauncher.Core.Services.Processes {
    //Code modified/adapted from https://codingvision.net/c-inject-a-dll-into-a-process-w-createremotethread
    public class DllInjector : IProcessInjector {
        private readonly ILog _logger = LogManager.GetLogger(nameof(DllInjector));

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
            uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress,
            uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        static extern IntPtr CreateRemoteThread(IntPtr hProcess,
            IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll")]
        static extern uint WaitForSingleObject(IntPtr hProcess, uint dwMilliseconds);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

        // privileges
        const int ProcessCreateThread = 0x0002;
        const int ProcessQueryInformation = 0x0400;
        const int ProcessVmOperation = 0x0008;
        const int ProcessVmWrite = 0x0020;
        const int ProcessVmRead = 0x0010;

        // used for memory allocation
        const uint MemCommit = 0x00001000;
        const uint MemReserve = 0x00002000;
        const uint MemRelease = 0x00008000;

        const uint PageReadwrite = 4;

        private readonly DllManager? _dllManager;
        private readonly string? _dllDirectory;
        private readonly bool _unsafeLoadAllDlls;
        public DllInjector(DllManager dllManager, string? dllDirectory = null, bool unsafeLoadAllDlls = false) {
            _dllManager = dllManager;
            _dllDirectory = dllDirectory;
            _unsafeLoadAllDlls = unsafeLoadAllDlls;
            if (unsafeLoadAllDlls && dllDirectory == null) throw new ArgumentException("Unsafe injection requires a plugin directory.", nameof(dllDirectory));
        }

        public DllInjector(string dllDirectory, bool unsafeLoadAllDlls = false) {
            _dllDirectory = dllDirectory;
            _unsafeLoadAllDlls = unsafeLoadAllDlls;
        }

        public IEnumerable<string> GetDllPaths(bool server = false) {
            if (_unsafeLoadAllDlls) return UnsafePaths();
            if (_dllManager == null) return [];
            var files = _dllManager.OpenForInjection(server);
            try { return files.Select(file => file.Path).ToArray(); }
            finally { foreach (var file in files) file.Dispose(); }
        }

        private string[] UnsafePaths() => _dllDirectory != null && Directory.Exists(_dllDirectory)
            ? Directory.EnumerateFiles(_dllDirectory, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath).OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : [];

        public bool Inject(Process p) => Inject(p, server: false);

        public bool Inject(Process p, bool server) {
            IReadOnlyList<ValidatedDll> files = Array.Empty<ValidatedDll>();
            var procHandle = IntPtr.Zero;
            var allocMemAddress = IntPtr.Zero;
            try {
                if (_dllManager == null && !_unsafeLoadAllDlls) return false;
                if (_dllManager != null) files = _dllManager.OpenForInjection(server);
                var paths = files.Select(file => file.Path).ToArray();
                if (_unsafeLoadAllDlls) paths = paths.Concat(UnsafePaths()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                if (paths.Length == 0) {
                    _logger.Info("No dlls present for injection");
                    return true;
                }

                //Paths to be injected MUST be absolute
                paths = paths.Select(p => Path.GetFullPath(p)).ToArray();

                _logger.LogListInfo("Injecting DLLs: ", paths);

                procHandle = OpenProcess(ProcessCreateThread |
                                             ProcessQueryInformation | ProcessVmOperation |
                                             ProcessVmWrite | ProcessVmRead, false, p.Id);
                if (procHandle == IntPtr.Zero) {
                    return false;
                }

                var maxLen = paths.Max(p => p.Length);
                var allocSize = checked((uint)((maxLen + 1) * sizeof(char)));

                // Unicode paths must be encoded exactly as LoadLibraryW expects.
                var loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                if (loadLibraryAddr == IntPtr.Zero) {
                    return false;
                }

                // alocating some memory on the target process - enough to store the name of the dll
                // and storing its address in a pointer
                allocMemAddress = VirtualAllocEx(procHandle,
                    IntPtr.Zero,
                    allocSize,
                    MemCommit | MemReserve, PageReadwrite);
                if (allocMemAddress == IntPtr.Zero) {
                    return false;
                }

                foreach (var path in paths) {
                    // writing the name of the dll there
                    var bytes = Encoding.Unicode.GetBytes(path + '\0');
                    var res = WriteProcessMemory(procHandle, allocMemAddress,
                        bytes, (uint)bytes.Length,
                        out var bytesWritten);
                    if (!res || bytesWritten.ToUInt64() != (ulong)bytes.Length) { return false; }

                    //inject
                    var thread = CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0,
                        IntPtr.Zero);
                    if (thread == IntPtr.Zero) return false;
                    try {
                        if (WaitForSingleObject(thread, unchecked((uint)-1)) != 0 ||
                            !GetExitCodeThread(thread, out var result) || result == 0) return false;
                    }
                    finally { CloseHandle(thread); }
                }

                return true;
            }
            catch (Exception e) {
                _logger.Error("Injection failed", e);
                return false;
            }
            finally {
                if (allocMemAddress != IntPtr.Zero) VirtualFreeEx(procHandle, allocMemAddress, 0, MemRelease);
                if (procHandle != IntPtr.Zero) CloseHandle(procHandle);
                foreach (var file in files) file.Dispose();
            }
        }
    }
}