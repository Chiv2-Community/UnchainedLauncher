using log4net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnchainedLauncher.Core.Extensions;

namespace UnchainedLauncher.Core.Services.Processes {
    //Code modified/adapted from https://codingvision.net/c-inject-a-dll-into-a-process-w-createremotethread
    public class DllInjector : IProcessInjector {
        private readonly ILog logger = LogManager.GetLogger(nameof(DllInjector));

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
        const int PROCESS_CREATE_THREAD = 0x0002;
        const int PROCESS_QUERY_INFORMATION = 0x0400;
        const int PROCESS_VM_OPERATION = 0x0008;
        const int PROCESS_VM_WRITE = 0x0020;
        const int PROCESS_VM_READ = 0x0010;

        // used for memory allocation
        const uint MEM_COMMIT = 0x00001000;
        const uint MEM_RESERVE = 0x00002000;
        const uint MEM_RELEASE = 0x00008000;

        const uint PAGE_READWRITE = 4;

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
                    logger.Info("No dlls present for injection");
                    return true;
                }

                //Paths to be injected MUST be absolute
                paths = paths.Select(p => Path.GetFullPath(p));

                logger.LogListInfo("Injecting DLLs: ", paths);

                IntPtr procHandle = OpenProcess(PROCESS_CREATE_THREAD |
                                                PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION |
                                                PROCESS_VM_WRITE | PROCESS_VM_READ, false, p.Id);
                if (procHandle == IntPtr.Zero) {
                    return false;
                }

                int maxLen = paths.Max(p => p.Length);
                uint allocSize = (uint)((maxLen + 1) * Marshal.SizeOf(typeof(char)));

                // searching for the address of LoadLibraryA and storing it in a pointer
                IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                if (loadLibraryAddr == IntPtr.Zero) {
                    return false;
                }

                // alocating some memory on the target process - enough to store the name of the dll
                // and storing its address in a pointer
                IntPtr allocMemAddress = VirtualAllocEx(procHandle,
                    IntPtr.Zero,
                    allocSize,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (allocMemAddress == IntPtr.Zero) {
                    return false;
                }

                foreach (string path in paths) {
                    // writing the name of the dll there
                    var res = WriteProcessMemory(procHandle, allocMemAddress,
                        Encoding.Default.GetBytes(path + '\0'),
                        (uint)((path.Length + 1) * Marshal.SizeOf(typeof(char))),
                        out UIntPtr bytesWritten);
                    if (!res) { return false; }

                    //inject
                    var thread = CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0,
                        IntPtr.Zero);
                    var signalEvent = WaitForSingleObject(thread, unchecked((uint)-1));
                }

                VirtualFreeEx(procHandle, allocMemAddress, allocSize, MEM_RELEASE);
                return true;
            }
            catch (Exception e) {
                logger.Error("Injection failed", e);
                return false;
            }
        }
    }
}