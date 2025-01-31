using log4net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnchainedLauncher.Core.Extensions;

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

        private string DllDir { get; }

        public DllInjector(string dllDir) {
            DllDir = dllDir;
        }

        public bool Inject(Process p) {
            try {
                var paths =
                    Directory.Exists(DllDir)
                        ? Directory.EnumerateFiles(DllDir, "*.dll", SearchOption.AllDirectories)
                        : Enumerable.Empty<string>();

                if (paths.Length() == 0) {
                    _logger.Info("No dlls present for injection");
                    return true;
                }

                //Paths to be injected MUST be absolute
                paths = paths.Select(p => Path.GetFullPath(p));

                _logger.LogListInfo("Injecting DLLs: ", paths);

                var procHandle = OpenProcess(ProcessCreateThread |
                                             ProcessQueryInformation | ProcessVmOperation |
                                             ProcessVmWrite | ProcessVmRead, false, p.Id);
                if (procHandle == IntPtr.Zero) {
                    return false;
                }

                var maxLen = paths.Max(p => p.Length);
                var allocSize = (uint)((maxLen + 1) * Marshal.SizeOf(typeof(char)));

                // searching for the address of LoadLibraryA and storing it in a pointer
                var loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                if (loadLibraryAddr == IntPtr.Zero) {
                    return false;
                }

                // alocating some memory on the target process - enough to store the name of the dll
                // and storing its address in a pointer
                var allocMemAddress = VirtualAllocEx(procHandle,
                    IntPtr.Zero,
                    allocSize,
                    MemCommit | MemReserve, PageReadwrite);
                if (allocMemAddress == IntPtr.Zero) {
                    return false;
                }

                foreach (var path in paths) {
                    // writing the name of the dll there
                    var res = WriteProcessMemory(procHandle, allocMemAddress,
                        Encoding.Default.GetBytes(path + '\0'),
                        (uint)((path.Length + 1) * Marshal.SizeOf(typeof(char))),
                        out var bytesWritten);
                    if (!res) { return false; }

                    //inject
                    var thread = CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0,
                        IntPtr.Zero);
                    var signalEvent = WaitForSingleObject(thread, unchecked((uint)-1));
                }

                VirtualFreeEx(procHandle, allocMemAddress, allocSize, MemRelease);
                return true;
            }
            catch (Exception e) {
                _logger.Error("Injection failed", e);
                return false;
            }
        }
    }
}