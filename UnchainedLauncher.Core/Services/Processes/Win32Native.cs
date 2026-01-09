using System.Diagnostics;
using System.Runtime.InteropServices;

/**
 * Stolen from SO https://stackoverflow.com/questions/2633628/can-i-get-command-line-arguments-of-other-processes-from-net-c
 *
 * This might flag things as a virus lel
 */
namespace UnchainedLauncher.Core.Services.Processes {
    public static class ExternalProcess {
        private static bool ReadStructFromProcessMemory<TStruct>(
            IntPtr hProcess, IntPtr lpBaseAddress, out TStruct val) {
            val = default;
            var structSize = Marshal.SizeOf<TStruct>();
            var mem = Marshal.AllocHGlobal(structSize);
            try {
                if (Win32Native.ReadProcessMemory(
                        hProcess, lpBaseAddress, mem, (uint)structSize, out var len) &&
                    (len == structSize)) {
                    val = Marshal.PtrToStructure<TStruct>(mem);
                    return true;
                }
            }
            finally {
                Marshal.FreeHGlobal(mem);
            }

            return false;
        }

        public static string ErrorToString(int error) =>
            new string[] {
                "Success", "Failed to open process for reading", "Failed to query process information",
                "PEB address was null", "Failed to read PEB information", "Failed to read process parameters",
                "Failed to read command line from process"
            }[Math.Abs(error)];

        public static int Retrieve(Process process, out string commandLine) {
            int rc = 0;
            commandLine = null;
            var hProcess = Win32Native.OpenProcess(
                Win32Native.OpenProcessDesiredAccessFlags.PROCESS_QUERY_INFORMATION |
                Win32Native.OpenProcessDesiredAccessFlags.PROCESS_VM_READ, false, (uint)process.Id);
            if (hProcess != IntPtr.Zero) {
                try {
                    var sizePBI = Marshal.SizeOf<Win32Native.ProcessBasicInformation>();
                    var memPBI = Marshal.AllocHGlobal(sizePBI);
                    try {
                        var ret = Win32Native.NtQueryInformationProcess(
                            hProcess, Win32Native.PROCESS_BASIC_INFORMATION, memPBI,
                            (uint)sizePBI, out var len);
                        if (0 == ret) {
                            var pbiInfo = Marshal.PtrToStructure<Win32Native.ProcessBasicInformation>(memPBI);
                            if (pbiInfo.PebBaseAddress != IntPtr.Zero) {
                                if (ReadStructFromProcessMemory<Win32Native.PEB>(hProcess,
                                        pbiInfo.PebBaseAddress, out var pebInfo)) {
                                    if (ReadStructFromProcessMemory<Win32Native.RtlUserProcessParameters>(
                                            hProcess, pebInfo.ProcessParameters, out var ruppInfo)) {
                                        var clLen = ruppInfo.CommandLine.MaximumLength;
                                        var memCL = Marshal.AllocHGlobal(clLen);
                                        try {
                                            if (Win32Native.ReadProcessMemory(hProcess,
                                                    ruppInfo.CommandLine.Buffer, memCL, clLen, out len)) {
                                                commandLine = Marshal.PtrToStringUni(memCL);
                                                rc = 0;
                                            }
                                            else {
                                                // couldn't read command line buffer
                                                rc = -6;
                                            }
                                        }
                                        finally {
                                            Marshal.FreeHGlobal(memCL);
                                        }
                                    }
                                    else {
                                        // couldn't read ProcessParameters
                                        rc = -5;
                                    }
                                }
                                else {
                                    // couldn't read PEB information
                                    rc = -4;
                                }
                            }
                            else {
                                // PebBaseAddress is null
                                rc = -3;
                            }
                        }
                        else {
                            // NtQueryInformationProcess failed
                            rc = -2;
                        }
                    }
                    finally {
                        Marshal.FreeHGlobal(memPBI);
                    }
                }
                finally {
                    Win32Native.CloseHandle(hProcess);
                }
            }
            else {
                // couldn't open process for VM read
                rc = -1;
            }

            return rc;
        }

        private static class Win32Native {
            public const uint PROCESS_BASIC_INFORMATION = 0;

            [Flags]
            public enum OpenProcessDesiredAccessFlags : uint {
                PROCESS_VM_READ = 0x0010,
                PROCESS_QUERY_INFORMATION = 0x0400,
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct ProcessBasicInformation {
                public IntPtr Reserved1;
                public IntPtr PebBaseAddress;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
                public IntPtr[] Reserved2;
                public IntPtr UniqueProcessId;
                public IntPtr Reserved3;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct UnicodeString {
                public ushort Length;
                public ushort MaximumLength;
                public IntPtr Buffer;
            }

            // This is not the real struct!
            // I faked it to get ProcessParameters address.
            // Actual struct definition:
            // https://learn.microsoft.com/en-us/windows/win32/api/winternl/ns-winternl-peb
            [StructLayout(LayoutKind.Sequential)]
            public struct PEB {
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
                public IntPtr[] Reserved;
                public IntPtr ProcessParameters;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct RtlUserProcessParameters {
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
                public byte[] Reserved1;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
                public IntPtr[] Reserved2;
                public UnicodeString ImagePathName;
                public UnicodeString CommandLine;
            }

            [DllImport("ntdll.dll")]
            public static extern uint NtQueryInformationProcess(
                IntPtr ProcessHandle,
                uint ProcessInformationClass,
                IntPtr ProcessInformation,
                uint ProcessInformationLength,
                out uint ReturnLength);

            [DllImport("kernel32.dll")]
            public static extern IntPtr OpenProcess(
                OpenProcessDesiredAccessFlags dwDesiredAccess,
                [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
                uint dwProcessId);

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ReadProcessMemory(
                IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer,
                uint nSize, out uint lpNumberOfBytesRead);

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);

            [DllImport("shell32.dll", SetLastError = true,
                CharSet = CharSet.Unicode, EntryPoint = "CommandLineToArgvW")]
            public static extern IntPtr CommandLineToArgv(string lpCmdLine, out int pNumArgs);
        }

    }
}