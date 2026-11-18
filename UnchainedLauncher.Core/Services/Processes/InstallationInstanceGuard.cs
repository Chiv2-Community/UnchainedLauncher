using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UnchainedLauncher.Core.Services.Processes {
    /// <summary>Presence of a launcher or installer for one physical local Windows directory.</summary>
    public sealed class InstallationInstanceGuard : IDisposable {
        private readonly Mutex _mutex;
        private readonly string _identity;
        private bool _disposed;

        private InstallationInstanceGuard(Mutex mutex, string identity) {
            _mutex = mutex;
            _identity = identity;
        }

        public static bool TryAcquire(DirectoryInfo root, out InstallationInstanceGuard? guard) {
            var identity = GetIdentity(root);
            // Existence, not ownership: never wait or release on a thread-affine mutex.
            var mutex = new Mutex(false, identity, out var createdNew);
            if (!createdNew) {
                mutex.Dispose();
                guard = null;
                return false;
            }

            guard = new InstallationInstanceGuard(mutex, identity);
            return true;
        }

        public bool Protects(DirectoryInfo root) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _identity == GetIdentity(root);
        }

        public void Dispose() {
            _disposed = true;
            _mutex.Dispose();
        }

        private static string GetIdentity(DirectoryInfo root) {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Installation presence requires Windows.");

            const uint readAttributes = 0x80, shareReadWriteDelete = 7, openExisting = 3, backupSemantics = 0x02000000;
            // Follow directory/ancestor reparse points; retain only the physical identity, not this handle.
            using var handle = CreateFileW(root.FullName, readAttributes, shareReadWriteDelete, IntPtr.Zero,
                openExisting, backupSemantics, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return $"Global\\UnchainedLauncher.Installation.{info.VolumeSerialNumber:X8}.{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    }
}
