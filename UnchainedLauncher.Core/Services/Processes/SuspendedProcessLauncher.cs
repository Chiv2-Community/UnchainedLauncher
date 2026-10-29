using LanguageExt;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes;

internal static class SuspendedProcessLauncher {
    private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(typeof(SuspendedProcessLauncher));
    private const uint CreateSuspended = 0x00000004;
    internal static Either<LaunchFailed, Process> Launch(string executable, string workingDirectory, string args,
        Func<Process, bool> initialize, ProcessOutputCapture? output = null) {
        var startup = new StartupInfoEx { Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() } };
        ProcessInformation native = default;
        Process? process = null;
        var resumed = false;
        var handles = IntPtr.Zero;
        var attributesInitialized = false;
        try {
            if (output != null) {
                startup.Info.Size = Marshal.SizeOf<StartupInfoEx>();
                startup.Info.Flags = 0x00000100; // STARTF_USESTDHANDLES
                startup.Info.StandardOutput = output.OutputHandle;
                startup.Info.StandardError = output.ErrorHandle;
                startup.Info.StandardInput = output.InputHandle;
                nuint size = 0;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                startup.Attributes = Marshal.AllocHGlobal(checked((nint)size));
                if (!InitializeProcThreadAttributeList(startup.Attributes, 1, 0, ref size))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                attributesInitialized = true;
                var inherited = output.InheritedHandles;
                handles = Marshal.AllocHGlobal(inherited.Length * IntPtr.Size);
                for (var i = 0; i < inherited.Length; i++) Marshal.WriteIntPtr(handles, i * IntPtr.Size, inherited[i]);
                // Inherit only this child's stdio, never unrelated handles.
                if (!UpdateProcThreadAttribute(startup.Attributes, 0, (IntPtr)0x00020002, handles,
                    (nuint)(inherited.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            var path = Path.GetFullPath(executable);
            var commandLine = new StringBuilder($"\"{path}\" {args}");
            if (!CreateProcessW(path, commandLine, IntPtr.Zero, IntPtr.Zero, output != null,
                CreateSuspended
                    | (output == null ? 0u : 0x08080000u), // CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT
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
            if (attributesInitialized) DeleteProcThreadAttributeList(startup.Attributes);
            if (startup.Attributes != IntPtr.Zero) Marshal.FreeHGlobal(startup.Attributes);
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
            if (!resumed && native.Process != IntPtr.Zero) {
                // Never resume a game after failed injection, including exceptions from the injector.
                if (!TerminateProcess(native.Process, 1))
                    Logger.Error("Could not terminate failed suspended launch.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
            if (native.Thread != IntPtr.Zero) CloseHandle(native.Thread);
            if (native.Process != IntPtr.Zero) CloseHandle(native.Process);
            if (!resumed) process?.Dispose();
            output?.SealClientHandles();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx {
        public StartupInfo Info;
        public IntPtr Attributes;
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
        uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute,
        IntPtr value, nuint size, IntPtr previous, IntPtr returnedSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
