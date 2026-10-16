using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LanguageExt;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes;

internal static class SuspendedProcessLauncher {
    private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(typeof(SuspendedProcessLauncher));
    private const uint CreateSuspended = 0x00000004;
    internal static Either<LaunchFailed, Process> Launch(string executable, string workingDirectory, string args,
        Func<Process, bool> initialize) {
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        ProcessInformation native = default;
        Process? process = null;
        var resumed = false;
        try {
            var path = Path.GetFullPath(executable);
            var commandLine = new StringBuilder($"\"{path}\" {args}");
            if (!CreateProcessW(path, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateSuspended,
                IntPtr.Zero, Path.GetFullPath(workingDirectory), ref startup, out native))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create suspended process.");

            process = Process.GetProcessById(native.ProcessId);
            // Open the managed handle while the original process handle still pins its identity.
            _ = process.SafeHandle;
            if (!initialize(process)) throw new InvalidOperationException("DLL injection failed.");
            var previousSuspendCount = ResumeThread(native.Thread);
            if (previousSuspendCount == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resume the game main thread.");
            if (previousSuspendCount != 1)
                throw new InvalidOperationException("Unexpected game main-thread suspend count.");
            resumed = true;
            return Right(process);
        }
        catch (Exception error) {
            return Left(new LaunchFailed(executable, args, error));
        }
        finally {
            if (!resumed && native.Process != IntPtr.Zero) {
                // Never resume a game after failed injection, including exceptions from the injector.
                if (!TerminateProcess(native.Process, 1))
                    Logger.Error("Could not terminate failed suspended launch.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
            if (native.Thread != IntPtr.Zero) CloseHandle(native.Thread);
            if (native.Process != IntPtr.Zero) CloseHandle(native.Process);
            if (!resumed) process?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}